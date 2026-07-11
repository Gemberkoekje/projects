// Phase 6 — trace pass: the Phase 5 shader plus image-decal shading (plan §3).
//
// Identical to PathTracePhase5.hlsl (spectral NEE path tracing + volumetric fog +
// G-buffer + the debug albedo AOV) with one feature added: primitives whose pattern
// is Decal (3) take their colour from the decal atlas (t7) instead of the material
// reflectance table, converting the sampled linear-RGB texel to a per-wavelength
// reflectance via the RGB→reflectance basis (t6). Scenes with no decal primitives
// render bit-identically to Phase 5. Requires Shader Model 6.5.

#define DETERMINISTIC_COUNT 50u
#define COMPANION_COUNT     4u
#define PI                  3.14159265
#define RNG_MUL             747796405u
#define RNG_ADD             2891336453u
#define SURFACE_MIRROR       1u  // SurfaceKind.Mirror
#define SURFACE_DIELECTRIC   2u  // SurfaceKind.Dielectric
#define MAX_SPECULAR_BOUNCES 8u  // JobSystem.MaxSpecularBounces

// Volumetric constants (mirror Phase4Reference / JobSystem).
#define ISOTROPIC_PHASE  0.07957747
#define VOL_CELL_SIZE    2.0  // MazeGeometryBuilder.CellSize
#define VOL_BIOME_CELLS  4.0  // Phase4Reference.BiomeSizeCells
static const float3 SMOKE_TINT = float3(0.97, 0.97, 0.97);
#define DECAL_SIZE 128u  // atlas layer dimension (must match PropTextures.Size)
#define WALL_HEIGHT 2.0  // MazeGeometryBuilder.WallHeight (build-in reveal cap, §9.3)

// ── Bindings ──────────────────────────────────────────────────────────

struct PrimitiveInfo
{
    float L1X; float L1Y; float L1Z;
    float E1X; float E1Y; float E1Z;
    float E2X; float E2Y; float E2Z;
    float NX;  float NY;  float NZ;
    float InvEdge1LenSq;
    float InvEdge2LenSq;
    uint  Pattern;        // 0 plain, 1 brick, 2 ceiling tile
    uint  MatPrimary;
    uint  MatSecondary;
    float P0; float P1; float P2; float P3;
    uint  Surface;        // SurfaceKind of the primary material (0 diffuse, 1 mirror, 2 dielectric)
    float Ior;            // index of refraction (constant / base Cauchy A) for dielectrics
    float CauchyB;        // Cauchy B dispersion coefficient (µm²); IOR = Ior + CauchyB / λ_µm²  (§2.1)
};

RaytracingAccelerationStructure Scene              : register(t0);
StructuredBuffer<PrimitiveInfo>  Primitives        : register(t1);
StructuredBuffer<float4>         DeterXYZ           : register(t2); // xyz used
StructuredBuffer<float>          MaterialReflectance: register(t3); // [material*50 + index]
StructuredBuffer<float4>         Lights             : register(t4); // xyz = world position
StructuredBuffer<float4>         LightColors        : register(t5); // xyz = colour (inscatter)
StructuredBuffer<float4>         RgbBasis           : register(t6); // [i].xyz = linearRGB→reflectance basis row i (plan §3.1)
StructuredBuffer<float4>         DecalPixels        : register(t7); // linear RGBA atlas, [layer*S*S + y*S + x]

RWStructuredBuffer<float4> Accum            : register(u0); // xyz running mean (total)
RWStructuredBuffer<uint>   SampleCount      : register(u1);
RWStructuredBuffer<uint>   WavelengthCounter: register(u2);
RWStructuredBuffer<uint>   LastHit          : register(u4); // 1 = pixel hit geometry last sample
RWStructuredBuffer<float4> DirectAccum      : register(u5); // xyz running mean (direct)
RWStructuredBuffer<float4> IndirectAccum    : register(u6); // xyz running mean (indirect)
RWStructuredBuffer<float4> HitPointOut      : register(u7); // xyz = world hit point (G-buffer)
RWStructuredBuffer<float4> NormalOut        : register(u8); // xyz = oriented normal, w = albedo (G-buffer)
RWStructuredBuffer<float4> FogOut           : register(u9); // xyz = inscatter*correction, w = transmittance
// Phase 5.2 statistics AOVs.
RWStructuredBuffer<float>  LumaM2           : register(u10); // Welford M2 of luma (total)
RWStructuredBuffer<float>  ClampAmount      : register(u11); // cumulative L1 clamp amount
RWStructuredBuffer<uint>   ClampHitFrame    : register(u12); // 1 = this pixel clamped this frame

cbuffer Constants : register(b0)
{
    float3 CamPos;                float _pad0;
    float4 CamRot;               // quaternion (x, y, z, w)
    float  TanHalfFov;           float AspectTanHalfFov; float InvWidth; float InvHeight;
    float  ImgPlaneZ;            float DeterministicCorrection; float AmbientLevel; float LightIntensity;
    uint   Width;                uint  Height; uint MaxSampleCount; uint ResetFlag;
    uint   NumPrimitives;        uint  SubPixelJitter; uint NumLights; uint LightingMode; // 0 none, 1 direct, 2 NEE
    float  SampleClamp;          uint  SoftResetFlag; uint MotionSampleCap; uint BumpyWalls; // §6
    // Volumetric (Phase 4). SoftResetFlag doubles as the volumetric IsMoving flag.
    uint   VolEnabled;           uint  VolSmokeMode; uint VolMarchSteps; uint VolShadowStepInterval;
    float  VolMaxMarchDistance;  float VolSigmaScaleFog; float VolSigmaScaleGround; float VolAnisotropyG;
    float  VolInscatterStrength; float VolEarlyOutTransmittance; uint BiomeIndicator; float VolTime;
    float  RevealHeight;         float RatPosX; float RatPosY; float RatPosZ; // §9.3 reveal, §8 rat billboard
    float  RatSize;              uint  ShowRat; uint  RatLayer; float _rpad;
};

// ── Pure math (mirrors Phase1Reference.cs / Phase2Reference.cs) ────────

uint Hash2D(int x, int y)
{
    uint h = uint(x * 374761393 + y * 668265263);
    h = (h ^ (h >> 13)) * 1274126177u;
    return h ^ (h >> 16);
}

float3 RotateByQuaternion(float4 q, float3 v)
{
    float x2 = q.x + q.x;
    float y2 = q.y + q.y;
    float z2 = q.z + q.z;
    float wx2 = q.w * x2;
    float wy2 = q.w * y2;
    float wz2 = q.w * z2;
    float xx2 = q.x * x2;
    float xy2 = q.x * y2;
    float xz2 = q.x * z2;
    float yy2 = q.y * y2;
    float yz2 = q.y * z2;
    float zz2 = q.z * z2;

    return float3(
        v.x * (1.0 - yy2 - zz2) + v.y * (xy2 - wz2) + v.z * (xz2 + wy2),
        v.x * (xy2 + wz2) + v.y * (1.0 - xx2 - zz2) + v.z * (yz2 - wx2),
        v.x * (xz2 - wy2) + v.y * (yz2 + wx2) + v.z * (1.0 - xx2 - yy2));
}

bool IsMortar(float u, float w, float bricksAcross, float bricksDown, float mortarU, float mortarV)
{
    float brickU = u * bricksAcross;
    float brickV = w * bricksDown;
    int row = (int)floor(brickV);
    float offsetU = brickU + (((row % 2) == 0) ? 0.0 : 0.5);
    float cellU = offsetU - floor(offsetU);
    float cellV = brickV - floor(brickV);
    return cellU < mortarU || cellU > (1.0 - mortarU)
        || cellV < mortarV || cellV > (1.0 - mortarV);
}

float BevelFactor(float u, float w, float tilesAcross, float tilesDown, float bevelFraction)
{
    float tileU = u * tilesAcross;
    float tileV = w * tilesDown;
    float cellU = tileU - floor(tileU);
    float cellV = tileV - floor(tileV);
    float distU = min(cellU, 1.0 - cellU);
    float distV = min(cellV, 1.0 - cellV);
    float edgeDist = min(distU, distV);
    if (edgeDist >= bevelFraction)
        return 1.0;
    float t = edgeDist / bevelFraction;
    float smoothT = t * t * (3.0 - 2.0 * t);
    return 0.4 + 0.6 * smoothT;
}

// Unlit brick emboss for Classic mode (plan §6.3). Fullbright shading (LightingMode.None)
// has no dot(N,L), so the BrickBumpNormal perturbation (further down) is invisible — the
// mortar relief the original screensaver's "bumpy walls" showed would vanish. Fake it here by
// modulating the brick attenuation with a fixed virtual key direction: the same bevel
// micro-facet tilt BrickBumpNormal builds, dotted with a world-space key light, brightens the
// near shoulder of each groove and darkens the far one. Returns a multiplier centred on 1.0
// (== 1.0 on the flat brick face and mortar bottom, so only the bevels are touched). Only
// called in unlit mode, so the lit Enhanced path stays bit-identical.
float BrickEmboss(PrimitiveInfo prim, float3 hitPoint)
{
    float3 l1 = float3(prim.L1X, prim.L1Y, prim.L1Z);
    float3 e1 = float3(prim.E1X, prim.E1Y, prim.E1Z);
    float3 e2 = float3(prim.E2X, prim.E2Y, prim.E2Z);
    float3 rel = hitPoint - l1;
    float u = dot(rel, e1) * prim.InvEdge1LenSq;
    float w = dot(rel, e2) * prim.InvEdge2LenSq;

    float brickU = u * prim.P0;
    float brickV = w * prim.P1;
    int rowi = (int)floor(brickV);
    float offsetU = brickU + (((rowi % 2) == 0) ? 0.0 : 0.5);
    float cellU = frac(offsetU);
    float cellV = frac(brickV);
    float mortarU = prim.P2, mortarV = prim.P3;

    // Same tangent-space slope BrickBumpNormal uses: +1 on the low edge, -1 on the high edge.
    float su = (cellU < mortarU) ? 1.0 : ((cellU > 1.0 - mortarU) ? -1.0 : 0.0);
    float sv = (cellV < mortarV) ? 1.0 : ((cellV > 1.0 - mortarV) ? -1.0 : 0.0);
    if (su == 0.0 && sv == 0.0)
        return 1.0; // flat face / mortar bottom — no relief

    // World-space bevel tilt · a fixed top-left key = normalize(-1, 2, -1). The relief is
    // consistent across walls of any orientation because both vectors live in world space.
    float3 tilt = su * normalize(e1) + sv * normalize(e2);
    const float3 keyDir = float3(-0.40825, 0.81650, -0.40825);
    const float embossStrength = 0.35;
    return 1.0 + embossStrength * dot(normalize(tilt), keyDir);
}

void PatternShade(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, out uint row, out float atten)
{
    float3 l1 = float3(prim.L1X, prim.L1Y, prim.L1Z);
    float3 e1 = float3(prim.E1X, prim.E1Y, prim.E1Z);
    float3 e2 = float3(prim.E2X, prim.E2Y, prim.E2Z);
    float3 nrm = float3(prim.NX, prim.NY, prim.NZ);

    float3 v = hitPoint - l1;
    float u = dot(v, e1) * prim.InvEdge1LenSq;
    float w = dot(v, e2) * prim.InvEdge2LenSq;

    float cosTheta = abs(dot(rayDir, nrm));

    row = prim.MatPrimary;
    atten = 1.0;
    if (prim.Pattern == 1u) // brick
    {
        bool mortar = IsMortar(u, w, prim.P0, prim.P1, prim.P2, prim.P3);
        atten = 0.4 + 0.6 * cosTheta;
        if (mortar)
        {
            atten *= 0.6;
            row = prim.MatSecondary;
        }
        // §6.3 Classic-mode emboss: in unlit fullbright the bump normal can't affect a
        // dot(N,L), so fake the mortar relief into the attenuation. Lit modes keep the real
        // BrickBumpNormal perturbation (below) and stay bit-identical — this branch is skipped.
        if (BumpyWalls != 0u && LightingMode == 0u)
            atten *= BrickEmboss(prim, hitPoint);
    }
    else if (prim.Pattern == 2u) // ceiling tile
    {
        atten = (0.4 + 0.6 * cosTheta) * BevelFactor(u, w, prim.P0, prim.P1, prim.P2);
    }
}

float3 FaceNormal(PrimitiveInfo prim, float3 rayDir)
{
    float3 n = float3(prim.NX, prim.NY, prim.NZ);
    return (dot(rayDir, n) < 0.0) ? n : -n;
}

// Bump mapping for brick walls (plan §6): fakes a bevelled slope at the mortar edges by
// tilting the shading normal in tangent space. Height is 1 on the brick face and ramps
// down within `mortar*` of each edge; the tilt is the negative tangent-space gradient of
// that height. Only reads in lit modes (perturbs the direct-lighting cosine); the geometric
// normal is kept for the G-buffer + ray offsets. Returns the perturbed unit normal.
float3 BrickBumpNormal(PrimitiveInfo prim, float3 hitPoint, float3 geoNormal)
{
    float3 l1 = float3(prim.L1X, prim.L1Y, prim.L1Z);
    float3 e1 = float3(prim.E1X, prim.E1Y, prim.E1Z);
    float3 e2 = float3(prim.E2X, prim.E2Y, prim.E2Z);
    float3 rel = hitPoint - l1;
    float u = dot(rel, e1) * prim.InvEdge1LenSq;
    float w = dot(rel, e2) * prim.InvEdge2LenSq;

    float brickU = u * prim.P0;   // P0 = bricks across
    float brickV = w * prim.P1;   // P1 = bricks down
    int rowi = (int)floor(brickV);
    float offsetU = brickU + (((rowi % 2) == 0) ? 0.0 : 0.5);
    float cellU = frac(offsetU);
    float cellV = frac(brickV);
    float mortarU = prim.P2, mortarV = prim.P3;

    // Tangent-space slope: +1 near the low edge (height rises with the coord), -1 near the
    // high edge; 0 on the flat brick face and flat mortar bottom.
    float su = (cellU < mortarU) ? 1.0 : ((cellU > 1.0 - mortarU) ? -1.0 : 0.0);
    float sv = (cellV < mortarV) ? 1.0 : ((cellV > 1.0 - mortarV) ? -1.0 : 0.0);
    if (su == 0.0 && sv == 0.0)
        return geoNormal;

    const float strength = 0.7;
    float3 tu = normalize(e1);
    float3 tv = normalize(e2);
    return normalize(geoNormal - strength * (su * tu + sv * tv));
}

// Samples a decal atlas layer (point sampling) at UV (u, w) → linear RGB texel.
// The quad's w runs 0 at L1 (wall bottom) → 1 at the top, but image row 0 is the top,
// so V is flipped to keep the art upright.
float3 DecalTexel(uint layer, float u, float w)
{
    uint px = min((uint)((1.0 - u) * float(DECAL_SIZE)), DECAL_SIZE - 1u);
    uint py = min((uint)((1.0 - w) * float(DECAL_SIZE)), DECAL_SIZE - 1u);
    return DecalPixels[layer * (DECAL_SIZE * DECAL_SIZE) + py * DECAL_SIZE + px].rgb;
}

// Reconstructs the decal's UV from the quad basis (matches PatternShade's u/w).
void DecalUv(PrimitiveInfo prim, float3 hitPoint, out float u, out float w)
{
    float3 l1 = float3(prim.L1X, prim.L1Y, prim.L1Z);
    float3 e1 = float3(prim.E1X, prim.E1Y, prim.E1Z);
    float3 e2 = float3(prim.E2X, prim.E2Y, prim.E2Z);
    float3 rel = hitPoint - l1;
    u = saturate(dot(rel, e1) * prim.InvEdge1LenSq);
    w = saturate(dot(rel, e2) * prim.InvEdge2LenSq);
}

float3 BaseXyz(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, uint pixelHash, uint sampleIdx)
{
    uint heroIdx = ((pixelHash % DETERMINISTIC_COUNT) + (sampleIdx % DETERMINISTIC_COUNT)) % DETERMINISTIC_COUNT;
    uint stride = DETERMINISTIC_COUNT / COMPANION_COUNT;

    if (prim.Pattern == 3u) // decal — colour from the atlas via the RGB→reflectance basis
    {
        float u, w;
        DecalUv(prim, hitPoint, u, w);
        float3 texel = DecalTexel((uint)prim.P0, u, w);
        float3 dxyz = float3(0.0, 0.0, 0.0);
        [unroll]
        for (uint kd = 0u; kd < COMPANION_COUNT; kd++)
        {
            uint idx = (heroIdx + kd * stride) % DETERMINISTIC_COUNT;
            float refl = dot(RgbBasis[idx].xyz, texel);
            dxyz += DeterXYZ[idx].xyz * refl;
        }
        return dxyz / float(COMPANION_COUNT);
    }

    uint row; float atten;
    PatternShade(prim, rayDir, hitPoint, row, atten);
    uint reflBase = row * DETERMINISTIC_COUNT;

    float3 xyz = float3(0.0, 0.0, 0.0);
    [unroll]
    for (uint k = 0u; k < COMPANION_COUNT; k++)
    {
        uint idx = (heroIdx + k * stride) % DETERMINISTIC_COUNT;
        float refl = MaterialReflectance[reflBase + idx] * atten;
        xyz += DeterXYZ[idx].xyz * refl;
    }
    return xyz / float(COMPANION_COUNT);
}

float3 IndirectBaseXyz(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, uint heroIdx)
{
    if (prim.Pattern == 3u) // decal
    {
        float u, w;
        DecalUv(prim, hitPoint, u, w);
        float3 texel = DecalTexel((uint)prim.P0, u, w);
        float refl = dot(RgbBasis[heroIdx].xyz, texel);
        return DeterXYZ[heroIdx].xyz * refl;
    }

    uint row; float atten;
    PatternShade(prim, rayDir, hitPoint, row, atten);
    float refl = MaterialReflectance[row * DETERMINISTIC_COUNT + heroIdx] * atten;
    return DeterXYZ[heroIdx].xyz * refl;
}

float3 CosineHemisphere(float3 n, float r1, float r2)
{
    float sqrtR1 = sqrt(r1);
    float theta = 2.0 * PI * r2;
    float sx = sqrtR1 * cos(theta);
    float sy = sqrtR1 * sin(theta);
    float sz = sqrt(max(0.0, 1.0 - r1));

    float3 tangent = (abs(n.x) > 0.1)
        ? normalize(float3(n.y, -n.x, 0.0))
        : normalize(float3(0.0, n.z, -n.y));
    float3 bitangent = cross(n, tangent);
    return normalize(sx * tangent + sy * bitangent + sz * n);
}

// ── Ray casting (RayQuery) ────────────────────────────────────────────

bool TraceClosest(float3 origin, float3 dir, out uint quadIndex, out float3 hitPoint)
{
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = 1e-4;
    ray.TMax = 1e4;

    // Build-in reveal (§9.3): while RevealHeight is below the wall top, force non-opaque
    // traversal and reject hits above the rising line, so walls appear to grow from the
    // floor (the floor at y=0 always passes; the ceiling appears when the reveal completes).
    // Once RevealHeight >= WALL_HEIGHT the fast opaque path (no candidate loop) is used.
    uint rayFlags = (RevealHeight < WALL_HEIGHT) ? RAY_FLAG_FORCE_NON_OPAQUE : RAY_FLAG_NONE;
    RayQuery<RAY_FLAG_NONE> q;
    q.TraceRayInline(Scene, rayFlags, 0xFF, ray);
    while (q.Proceed())
    {
        if (q.CandidateType() == CANDIDATE_NON_OPAQUE_TRIANGLE)
        {
            float tc = q.CandidateTriangleRayT();
            if ((origin.y + dir.y * tc) <= RevealHeight)
                q.CommitNonOpaqueTriangleHit(); // below the reveal line → solid
            // else: not risen yet — ignore and keep traversing
        }
    }

    if (q.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
    {
        quadIndex = q.CommittedPrimitiveIndex() >> 1;
        float tHit = q.CommittedRayT();
        hitPoint = origin + dir * tHit;
        return true;
    }
    quadIndex = 0u;
    hitPoint = float3(0.0, 0.0, 0.0);
    return false;
}

bool TraceOccluded(float3 origin, float3 dir, float maxDist)
{
    if (maxDist <= 1e-4)
        return false;

    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = 1e-4;
    ray.TMax = maxDist;

    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> q;
    q.TraceRayInline(Scene, RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH, 0xFF, ray);
    q.Proceed();
    return q.CommittedStatus() == COMMITTED_TRIANGLE_HIT;
}

// ── Lighting (mirrors Phase2Reference) ────────────────────────────────

float LightWeight(float3 lightPos, float3 samplePoint, float3 normal)
{
    float3 toLight = lightPos - samplePoint;
    float dsq = dot(toLight, toLight);
    float dist = sqrt(dsq);
    float3 ldir = toLight / dist;
    float cosv = dot(normal, ldir);
    return cosv > 0.0 ? 1.0 / max(dsq, 1e-6) : 0.0;
}

void FillLight(float3 lightPos, float3 samplePoint, float3 normal, out float3 outDir, out float outDistSq, out float outCos)
{
    float3 toLight = lightPos - samplePoint;
    outDistSq = max(dot(toLight, toLight), 1e-6);
    float dist = sqrt(outDistSq);
    outDir = toLight / dist;
    outCos = dot(normal, outDir);
}

void SelectLight(inout uint rng, float3 samplePoint, float3 normal,
                 out float outP, out float3 outDir, out float outDistSq, out float outCos)
{
    float totalW = 0.0;
    for (uint li = 0u; li < NumLights; li++)
        totalW += LightWeight(Lights[li].xyz, samplePoint, normal);

    rng = rng * RNG_MUL + RNG_ADD;

    if (totalW > 0.0)
    {
        float u = float(rng) / 4294967296.0 * totalW;
        float acc = 0.0;
        for (uint li = 0u; li < NumLights; li++)
        {
            float w = LightWeight(Lights[li].xyz, samplePoint, normal);
            acc += w;
            if (u <= acc)
            {
                FillLight(Lights[li].xyz, samplePoint, normal, outDir, outDistSq, outCos);
                outP = w / totalW;
                return;
            }
        }
        uint last = NumLights - 1u;
        FillLight(Lights[last].xyz, samplePoint, normal, outDir, outDistSq, outCos);
        outP = LightWeight(Lights[last].xyz, samplePoint, normal) / totalW;
        return;
    }

    uint idx = rng % NumLights;
    FillLight(Lights[idx].xyz, samplePoint, normal, outDir, outDistSq, outCos);
    outP = 1.0 / float(NumLights);
}

float UniformLightTerm(uint lightIdx, float3 hitPoint, float3 hitNormal)
{
    float3 toLight = Lights[lightIdx].xyz - hitPoint;
    float distSq = dot(toLight, toLight);
    float dist = sqrt(distSq);
    float3 lightDir = toLight / dist;
    float cosTheta = dot(hitNormal, lightDir);
    if (cosTheta <= 0.0)
        return 0.0;

    float3 shadowOrigin = hitPoint + hitNormal * 1e-3;
    if (TraceOccluded(shadowOrigin, lightDir, dist - 2e-3))
        return 0.0;

    return cosTheta / distSq * LightIntensity * float(NumLights);
}

// ── Volumetrics (mirrors Phase4Reference / VolumetricIntegration.cs) ────

uint VolHashCell(int x, int y)
{
    uint h = uint(x * 374761393 + y * 668265263);
    h ^= h >> 16;
    h *= 2246822519u;
    h ^= h >> 13;
    h *= 3266489917u;
    return h ^ (h >> 16);
}

bool IsSmokeBiome(int biomeX, int biomeY)
{
    return ((biomeX + biomeY) % 3) == 1;
}

bool IsFogBiome(int biomeX, int biomeY)
{
    uint biomeHash = VolHashCell(biomeX, biomeY);
    return (biomeHash & 1u) == 0u;
}

float SmokeCoverage(float x, float z)
{
    float bands = 0.5 + 0.5 * sin(x * 0.37 + z * 0.53);
    float swirl = 0.5 + 0.5 * sin(x * 0.19 - z * 0.29 + bands * 3.1);
    return 0.35 + 0.65 * (0.6 * bands + 0.4 * swirl);
}

// Non-turbulence density profiles shared by the Always* modes and the biome
// sub-types (mirror Phase4Reference.FogProfile / GroundProfile).
float FogProfile(float3 p, float coverage)
{
    float midHeight = exp(-abs(p.y - 1.0) * 1.35);
    float thickCoverage = 0.72 + 0.28 * coverage;
    float heightWeight = 0.70 + 0.30 * midHeight;
    return 0.50 * thickCoverage * heightWeight;
}

float GroundProfile(float3 p, float coverage)
{
    float thickCoverage = 0.72 + 0.28 * coverage;
    float groundLayer = exp(-max(p.y, 0.0) * 2.35);
    return 0.50 * thickCoverage * groundLayer;
}

float GetBiomeCellDensity(int biomeX, int biomeY, float3 p, float coverage)
{
    if (!IsSmokeBiome(biomeX, biomeY))
        return 0.0;

    return IsFogBiome(biomeX, biomeY)
        ? FogProfile(p, coverage)
        : GroundProfile(p, coverage);
}

float SmoothStep01(float t)
{
    t = clamp(t, 0.0, 1.0);
    return t * t * (3.0 - 2.0 * t);
}

// ── Smoke turbulence (texture-free 3D value-noise fBm) ─────────────────

float Hash3ToUnit(int x, int y, int z)
{
    uint h = uint(x * 374761393 + y * 668265263 + z * 1013904223);
    h ^= h >> 16;
    h *= 2246822519u;
    h ^= h >> 13;
    h *= 3266489917u;
    h ^= h >> 16;
    return float(h) * (1.0 / 4294967296.0);
}

float ValueNoise3D(float3 p)
{
    float fx = floor(p.x), fy = floor(p.y), fz = floor(p.z);
    int ix = (int)fx, iy = (int)fy, iz = (int)fz;
    float tx = p.x - fx, ty = p.y - fy, tz = p.z - fz;
    float ux = tx * tx * (3.0 - 2.0 * tx);
    float uy = ty * ty * (3.0 - 2.0 * ty);
    float uz = tz * tz * (3.0 - 2.0 * tz);

    float c000 = Hash3ToUnit(ix,     iy,     iz);
    float c100 = Hash3ToUnit(ix + 1, iy,     iz);
    float c010 = Hash3ToUnit(ix,     iy + 1, iz);
    float c110 = Hash3ToUnit(ix + 1, iy + 1, iz);
    float c001 = Hash3ToUnit(ix,     iy,     iz + 1);
    float c101 = Hash3ToUnit(ix + 1, iy,     iz + 1);
    float c011 = Hash3ToUnit(ix,     iy + 1, iz + 1);
    float c111 = Hash3ToUnit(ix + 1, iy + 1, iz + 1);

    float x00 = c000 + (c100 - c000) * ux;
    float x10 = c010 + (c110 - c010) * ux;
    float x01 = c001 + (c101 - c001) * ux;
    float x11 = c011 + (c111 - c011) * ux;
    float y0 = x00 + (x10 - x00) * uy;
    float y1 = x01 + (x11 - x01) * uy;
    return y0 + (y1 - y0) * uz;
}

float SmokeFbm(float3 p)
{
    float sum = 0.0;
    float amp = 0.5;
    [unroll]
    for (int o = 0; o < 3; o++)
    {
        sum += amp * ValueNoise3D(p);
        p *= 2.02;
        amp *= 0.5;
    }
    return sum;
}

float SmokeTurbulence(float3 p, float time)
{
    float3 q = float3(p.x * 0.22 + 11.3, p.y * 0.40 + 4.7, p.z * 0.22 + 19.1);
    q += time * float3(0.30, 0.10, 0.18);
    float f = SmokeFbm(q);
    float n = clamp((f - 0.33) * 2.6, 0.0, 1.0);
    n = n * n * (3.0 - 2.0 * n);
    return 0.05 + 2.15 * n;
}

// Narrow-band biome boundary blend: pure biome outside [0.5 - hb, 0.5 + hb].
float BiomeEdgeBlend(float f)
{
    const float halfBand = 0.2;
    float t = clamp((f - (0.5 - halfBand)) / (2.0 * halfBand), 0.0, 1.0);
    return t * t * (3.0 - 2.0 * t);
}

float GetDensityBiome(float3 p, float time)
{
    float biomeWorldSize = VOL_CELL_SIZE * VOL_BIOME_CELLS;

    // Centre the interpolation on biome centres (shift by half a cell) so a
    // biome's interior samples purely itself; the previous origin-aligned blend
    // mixed the centre of every biome 50/50 with its +X/+Y neighbour.
    float gx = p.x / biomeWorldSize - 0.5;
    float gy = p.z / biomeWorldSize - 0.5;

    int bx0 = (int)floor(gx);
    int by0 = (int)floor(gy);
    int bx1 = bx0 + 1;
    int by1 = by0 + 1;

    float tx = BiomeEdgeBlend(gx - float(bx0));
    float ty = BiomeEdgeBlend(gy - float(by0));

    float coverage = SmokeCoverage(p.x, p.z);
    float d00 = GetBiomeCellDensity(bx0, by0, p, coverage);
    float d10 = GetBiomeCellDensity(bx1, by0, p, coverage);
    float d01 = GetBiomeCellDensity(bx0, by1, p, coverage);
    float d11 = GetBiomeCellDensity(bx1, by1, p, coverage);

    float dx0 = d00 + (d10 - d00) * tx;
    float dx1 = d01 + (d11 - d01) * tx;
    return (dx0 + (dx1 - dx0) * ty) * SmokeTurbulence(p, time);
}

float GetDensityFog(float3 p, float time)
{
    float coverage = SmokeCoverage(p.x, p.z);
    return FogProfile(p, coverage) * SmokeTurbulence(p, time);
}

float GetDensityGround(float3 p, float time)
{
    float coverage = SmokeCoverage(p.x, p.z);
    return GroundProfile(p, coverage) * SmokeTurbulence(p, time);
}

float GetDensity(float3 p, uint smokeMode, float time)
{
    if (smokeMode == 1u) return GetDensityBiome(p, time);   // Biome
    if (smokeMode == 2u) return GetDensityFog(p, time);     // AlwaysFog
    if (smokeMode == 3u) return GetDensityGround(p, time);  // AlwaysGroundSmoke
    return 0.0;                                             // None
}

float VolEvaluatePhase(float3 viewDirection, float3 samplePoint)
{
    float g = clamp(VolAnisotropyG, -0.95, 0.95);
    if (abs(g) <= 1e-4 || NumLights == 0u)
        return ISOTROPIC_PHASE;

    float3 lightDirection = normalize(Lights[0].xyz - samplePoint);
    float cosTheta = clamp(dot(lightDirection, -viewDirection), -1.0, 1.0);
    float denom = 1.0 + g * g - 2.0 * g * cosTheta;
    return (1.0 - g * g) / (4.0 * PI * pow(denom, 1.5));
}

float3 EstimateInscatterLight(float3 samplePoint, float3 viewDirection, bool traceShadow)
{
    if (NumLights == 0u)
        return SMOKE_TINT;

    float3 lighting = float3(0.0, 0.0, 0.0);
    for (uint i = 0u; i < NumLights; i++)
    {
        float3 toLight = Lights[i].xyz - samplePoint;
        float distSq = max(dot(toLight, toLight), 1e-6);
        float dist = sqrt(distSq);
        float3 lightDir = toLight / dist;

        if (traceShadow)
        {
            float3 shadowOrigin = samplePoint + lightDir * 1e-3;
            if (TraceOccluded(shadowOrigin, lightDir, dist - 2e-3))
                continue;
        }

        lighting += LightColors[i].xyz * (LightIntensity / distSq);
    }

    if (all(lighting == float3(0.0, 0.0, 0.0)))
        return SMOKE_TINT * 0.08;

    float3 ambient = SMOKE_TINT * AmbientLevel;
    return clamp(ambient + lighting * SMOKE_TINT, float3(0.0, 0.0, 0.0), float3(1.5, 1.5, 1.5));
}

void IntegrateVolumetric(float3 rayOrigin, float3 hitPoint, float3 rayDirection, float time,
                         out float transmittance, out float3 inscatter)
{
    transmittance = 1.0;
    inscatter = float3(0.0, 0.0, 0.0);

    if (VolEnabled == 0u || VolSmokeMode == 0u || VolMarchSteps == 0u)
        return;

    float3 segment = hitPoint - rayOrigin;
    float rayLength = length(segment);
    if (rayLength <= 1e-5)
        return;

    float marchLength = min(rayLength, max(VolMaxMarchDistance, 0.0));
    if (marchLength <= 1e-5)
        return;

    uint steps = max(1u, VolMarchSteps);
    if (SoftResetFlag != 0u && steps > 1u)
        steps = max(1u, (steps + 1u) / 2u);

    float3 dir = segment / rayLength;
    if (dot(rayDirection, rayDirection) > 1e-10)
        dir = normalize(rayDirection);

    float stepLength = marchLength / float(steps);
    float sigmaScale = (VolSmokeMode == 3u) ? VolSigmaScaleGround : VolSigmaScaleFog;
    float inscatterScale = VolInscatterStrength / ISOTROPIC_PHASE;

    for (uint i = 0u; i < steps; i++)
    {
        float distance = (float(i) + 0.5) * stepLength;
        float3 p = rayOrigin + dir * distance;
        float density = GetDensity(p, VolSmokeMode, time);
        if (density <= 0.0)
            continue;

        float sigmaT = density * sigmaScale;
        if (sigmaT <= 0.0)
            continue;

        float opticalDepth = sigmaT * stepLength;
        float stepTransmittance = exp(-opticalDepth);
        float scatterWeight = transmittance * (1.0 - stepTransmittance);
        float phase = VolEvaluatePhase(dir, p);

        bool traceShadow = false;
        if (VolShadowStepInterval > 0u)
            traceShadow = (i % VolShadowStepInterval) == 0u;
        float3 localLight = EstimateInscatterLight(p, dir, traceShadow);

        inscatter += localLight * (scatterWeight * inscatterScale * phase);
        transmittance *= stepTransmittance;

        if (transmittance < VolEarlyOutTransmittance)
            break;
    }
}

// ── Mirror specular reflection (spectral-effects-plan.md §1.1) ─────────
// Line-for-line port of Phase2Reference.MirrorDiffuseScalar / TraceMirrorRadiance
// (and JobSystem's mirror branch). HLSL has no recursion, so the mirror chain is
// an iterative loop accumulating a reflectance product.

float MirrorDiffuseScalar(float refl, float3 pt, float3 normal, inout uint rng)
{
    float ambient = 1.0;
    float direct = 0.0;
    if (LightingMode != 0u && NumLights > 0u)
    {
        ambient = AmbientLevel;
        float p; float3 ldir; float distSq; float cosv;
        SelectLight(rng, pt, normal, p, ldir, distSq, cosv);
        if (cosv > 0.0)
        {
            bool visible = true;
            if (LightingMode == 2u) // NEE
            {
                float3 shadowOrigin = pt + normal * 1e-3;
                visible = !TraceOccluded(shadowOrigin, ldir, sqrt(distSq) - 2e-3);
            }
            if (visible)
                direct = cosv / distSq * LightIntensity / max(p, 1e-9);
        }
    }
    return refl * (ambient + direct);
}

// Hero-wavelength scalar reflectance at a hit, including the decal atlas colour
// (Pattern 3) via the RGB→reflectance basis — so a decal (wall sign / logo) seen in
// a mirror keeps its texture rather than falling back to its placeholder material.
// Mirrors IndirectBaseXyz's decal branch.
float HeroReflectance(PrimitiveInfo prim, float3 dir, float3 hitPoint, uint heroIdx)
{
    if (prim.Pattern == 3u) // decal
    {
        float u, w;
        DecalUv(prim, hitPoint, u, w);
        float3 texel = DecalTexel((uint)prim.P0, u, w);
        return dot(RgbBasis[heroIdx].xyz, texel);
    }

    uint row; float atten;
    PatternShade(prim, dir, hitPoint, row, atten);
    return MaterialReflectance[row * DETERMINISTIC_COUNT + heroIdx] * atten;
}

// Snell refraction (line-for-line port of Optics.Refract). Returns false on TIR
// (then 'refracted' is the mirror reflection).
bool Refract(float3 incident, float3 normal, float iorFrom, float iorTo, out float3 refracted)
{
    float3 i = normalize(incident);
    float3 n = normal;
    float cosI = dot(i, n);
    if (cosI > 0.0) n = -n; else cosI = -cosI;
    float eta = iorFrom / iorTo;
    float k = 1.0 - eta * eta * (1.0 - cosI * cosI);
    if (k < 0.0) { refracted = reflect(i, n); return false; }
    refracted = normalize(eta * i + (eta * cosI - sqrt(k)) * n);
    return true;
}

// Exact unpolarized Fresnel reflectance (port of Optics.FresnelDielectric).
float FresnelDielectric(float cosThetaI, float iorFrom, float iorTo)
{
    cosThetaI = clamp(abs(cosThetaI), 0.0, 1.0);
    float sinI = sqrt(max(0.0, 1.0 - cosThetaI * cosThetaI));
    float sinT = iorFrom / iorTo * sinI;
    if (sinT >= 1.0) return 1.0;
    float cosT = sqrt(max(0.0, 1.0 - sinT * sinT));
    float rs = (iorFrom * cosThetaI - iorTo * cosT) / (iorFrom * cosThetaI + iorTo * cosT);
    float rp = (iorFrom * cosT - iorTo * cosThetaI) / (iorFrom * cosT + iorTo * cosThetaI);
    return 0.5 * (rs * rs + rp * rp);
}

// Analytic view-facing rat billboard (plan §8): makes the rat a real object for the ray
// tracer, so it appears in reflections/refractions (fogged and occluded correctly),
// unlike the screen-space ApplyRat used for the crisp primary view. Returns the
// hero-wavelength reflectance of an opaque sprite texel hit before maxT. The sprite is
// shaded fullbright (unlit) to match the primary billboard.
bool IntersectRat(float3 origin, float3 dir, float maxT, uint heroIdx, out float3 hitPoint, out float refl)
{
    hitPoint = float3(0.0, 0.0, 0.0);
    refl = 0.0;
    if (ShowRat == 0u)
        return false;

    float3 ratPos = float3(RatPosX, RatPosY, RatPosZ);
    float t = dot(ratPos - origin, dir);            // plane through ratPos ⟂ dir
    if (t <= 1e-3 || t >= maxT)
        return false;

    float3 hp = origin + dir * t;
    float3 rel = hp - ratPos;
    float3 up = abs(dir.y) < 0.99 ? float3(0, 1, 0) : float3(0, 0, 1);
    float3 right = normalize(cross(up, dir));
    float3 vup = cross(dir, right);
    float halfSize = RatSize * 0.5;
    float u = dot(rel, right) / halfSize;
    float v = dot(rel, vup) / halfSize;
    if (abs(u) > 1.0 || abs(v) > 1.0)
        return false;

    uint tx = min((uint)(0.5 * (u + 1.0) * float(DECAL_SIZE)), DECAL_SIZE - 1u);
    uint ty = min((uint)(0.5 * (1.0 - v) * float(DECAL_SIZE)), DECAL_SIZE - 1u); // sprite row 0 = top
    float4 texel = DecalPixels[RatLayer * (DECAL_SIZE * DECAL_SIZE) + ty * DECAL_SIZE + tx];
    if (texel.a < 0.5)
        return false; // transparent sprite pixel

    hitPoint = hp;
    refl = dot(RgbBasis[heroIdx].xyz, texel.rgb);
    return true;
}

// XYZ radiance along a specular chain (mirror reflect + dielectric Fresnel
// reflect/refract), with participating media integrated along every reflected segment
// so smoke shows in reflections/refractions. Iterative (HLSL has no recursion), tracing
// from the camera; the camera→primary segment (depth 1) is fogged by FogOut/resolve, so
// it is skipped here to avoid double-counting. Mirrors JobSystem.TraceSpecularRadiance.
float3 TraceSpecularRadiance(float3 origin, float3 dir, uint heroIdx, inout uint rng)
{
    float weight = 1.0;             // Π (transmittance × reflectance) so far
    float3 accum = float3(0.0, 0.0, 0.0);
    bool inGlass = false;
    [loop]
    for (uint depth = 1u; depth <= MAX_SPECULAR_BOUNCES; depth++)
    {
        uint idx; float3 hitPoint;
        bool sceneHit = TraceClosest(origin, dir, idx, hitPoint);
        float sceneT = sceneHit ? length(hitPoint - origin) : 1e30;

        // The rat is a real object for reflected rays (depth > 1); the primary view keeps
        // the screen-space ApplyRat so a walking rat stays crisp through the accumulator.
        float3 ratHitPoint; float ratRefl;
        bool ratHit = depth > 1u && IntersectRat(origin, dir, sceneT, heroIdx, ratHitPoint, ratRefl);

        if (!sceneHit && !ratHit)
            return accum; // ray escapes; keep the fog it passed through

        float3 segEnd = ratHit ? ratHitPoint : hitPoint;
        if (depth > 1u) // fog the reflected segments (camera→primary is FogOut's job)
        {
            float fogT; float3 fogI;
            IntegrateVolumetric(origin, segEnd, dir, VolTime, fogT, fogI);
            accum += weight * fogI;
            weight *= fogT;
        }

        if (ratHit) // fullbright sprite terminal
        {
            accum += weight * DeterXYZ[heroIdx].xyz * ratRefl;
            return accum;
        }

        PrimitiveInfo prim = Primitives[idx];
        float3 hitNormal = FaceNormal(prim, dir);

        if (prim.Surface == SURFACE_MIRROR && depth < MAX_SPECULAR_BOUNCES)
        {
            weight *= HeroReflectance(prim, dir, hitPoint, heroIdx);
            dir = reflect(dir, hitNormal);
            origin = hitPoint + hitNormal * 1e-3;
            continue;
        }

        if (prim.Surface == SURFACE_DIELECTRIC && depth < MAX_SPECULAR_BOUNCES)
        {
            // Dispersion (§2.1): the IOR varies with the hero wavelength (Cauchy n = A + B/λ²),
            // so each accumulated wavelength refracts by a different angle. The hero wavelength
            // (nm) rides in DeterXYZ[heroIdx].w; CauchyB == 0 leaves n == prim.Ior (non-dispersive).
            float um = DeterXYZ[heroIdx].w * 1e-3;   // hero wavelength in µm
            float n = prim.Ior + prim.CauchyB / (um * um);
            float iorFrom = inGlass ? n : 1.0;
            float iorTo   = inGlass ? 1.0 : n;
            float cosI = abs(dot(normalize(dir), hitNormal));
            float R = FresnelDielectric(cosI, iorFrom, iorTo);
            float3 refr;
            bool transmits = Refract(dir, hitNormal, iorFrom, iorTo, refr);
            rng = rng * RNG_MUL + RNG_ADD;
            float u = float(rng) / 4294967296.0;
            if (!transmits || u < R)
            {
                dir = reflect(dir, hitNormal);
                origin = hitPoint + hitNormal * 1e-3;
            }
            else
            {
                dir = refr;
                origin = hitPoint - hitNormal * 1e-3;
                inGlass = !inGlass;
            }
            continue;
        }

        accum += weight * DeterXYZ[heroIdx].xyz
            * MirrorDiffuseScalar(HeroReflectance(prim, dir, hitPoint, heroIdx), hitPoint, hitNormal, rng);
        return accum;
    }
    return accum;
}

// Full per-sample corrected XYZ; also returns direct/indirect components, the
// primary-hit mask, the primary hit point + oriented normal for the G-buffer, and
// (Phase 5) the primary hit's base reflectance luminance for the Albedo view.
float3 ShadeSample(float3 camPos, float3 primaryDir, uint pixelHash, uint sampleIdx,
                   out bool primaryHit, out float3 correctedDirect, out float3 correctedIndirect,
                   out float3 primaryHitPoint, out float3 primaryNormal, out float primaryAlbedo)
{
    correctedDirect = float3(0.0, 0.0, 0.0);
    correctedIndirect = float3(0.0, 0.0, 0.0);
    primaryHitPoint = float3(0.0, 0.0, 0.0);
    primaryNormal = float3(0.0, 0.0, 0.0);
    primaryAlbedo = 0.0;

    uint primIndex;
    float3 hitPoint;
    primaryHit = TraceClosest(camPos, primaryDir, primIndex, hitPoint);
    if (!primaryHit)
        return float3(0.0, 0.0, 0.0);

    PrimitiveInfo prim = Primitives[primIndex];
    float3 hitNormal = FaceNormal(prim, primaryDir);
    primaryHitPoint = hitPoint;
    primaryNormal = hitNormal;

    uint heroIdx = ((pixelHash % DETERMINISTIC_COUNT) + (sampleIdx % DETERMINISTIC_COUNT)) % DETERMINISTIC_COUNT;

    // Specular surface — mirror reflection (§1.1) or dielectric reflect/refract
    // (§1.2). Hero-only; accumulation builds the spectrum. G-buffer keeps the
    // specular surface so TAA reprojects on it. Mirrors JobSystem.TraceCore.
    if (prim.Surface == SURFACE_MIRROR || prim.Surface == SURFACE_DIELECTRIC)
    {
        primaryAlbedo = HeroReflectance(prim, primaryDir, hitPoint, heroIdx);

        uint specRng = pixelHash + sampleIdx * 2654435761u + 1013904223u;
        // Already CIE-weighted XYZ, with smoke integrated along the reflected segments.
        float3 specXyz = TraceSpecularRadiance(camPos, primaryDir, heroIdx, specRng) * DeterministicCorrection;
        correctedDirect = specXyz;
        return specXyz;
    }

    float3 baseXyz = BaseXyz(prim, primaryDir, hitPoint, pixelHash, sampleIdx);
    primaryAlbedo = baseXyz.y; // base reflectance luminance (Albedo debug view)

    bool lit = (LightingMode != 0u) && (NumLights > 0u);

    float3 xyz;
    float3 bounce0 = float3(0.0, 0.0, 0.0);
    float3 indirect = float3(0.0, 0.0, 0.0);

    if (!lit)
    {
        xyz = baseXyz;
        bounce0 = baseXyz;
    }
    else
    {
        float ambientTerm = AmbientLevel;

        float directTerm = 0.0;
        uint rngLight = pixelHash + sampleIdx * RNG_MUL + RNG_ADD;
        // Bump-mapped shading normal for brick (plan §6); geometric normal kept elsewhere.
        float3 shadeNormal = (BumpyWalls != 0u && prim.Pattern == 1u)
            ? BrickBumpNormal(prim, hitPoint, hitNormal) : hitNormal;
        float lightP; float3 lightDir; float lightDistSq; float lightCos;
        SelectLight(rngLight, hitPoint, shadeNormal, lightP, lightDir, lightDistSq, lightCos);

        if (lightCos > 0.0)
        {
            bool visible = true;
            if (LightingMode == 2u) // NEE
            {
                float3 shadowOrigin = hitPoint + hitNormal * 1e-3;
                visible = !TraceOccluded(shadowOrigin, lightDir, sqrt(lightDistSq) - 2e-3);
            }
            if (visible)
                directTerm += lightCos / lightDistSq * LightIntensity / max(lightP, 1e-9);
        }

        xyz = baseXyz * (ambientTerm + directTerm);

        uint rng = pixelHash + sampleIdx * RNG_MUL + RNG_ADD;
        rng = rng * RNG_MUL + RNG_ADD;
        float r1 = (rng & 0xFFFF) / 65536.0;
        rng = rng * RNG_MUL + RNG_ADD;
        float r2 = (rng & 0xFFFF) / 65536.0;

        float3 sampleDir = CosineHemisphere(hitNormal, r1, r2);
        float3 secOrigin = hitPoint + hitNormal * 1e-3;

        uint secIndex;
        float3 secHitPoint;
        if (TraceClosest(secOrigin, sampleDir, secIndex, secHitPoint))
        {
            PrimitiveInfo secPrim = Primitives[secIndex];
            float3 secHitNormal = FaceNormal(secPrim, sampleDir);
            float3 secBaseXyz = IndirectBaseXyz(secPrim, sampleDir, secHitPoint, heroIdx);

            float secDirectTerm = 0.0;
            rng = rng * RNG_MUL + RNG_ADD;
            uint lightIdx2 = rng % NumLights;
            secDirectTerm += UniformLightTerm(lightIdx2, secHitPoint, secHitNormal);

            float3 secIncoming = secBaseXyz * (AmbientLevel + secDirectTerm);

            float3 secBounce2Plus = float3(0.0, 0.0, 0.0);
            rng = rng * RNG_MUL + RNG_ADD;
            float r3 = (rng & 0xFFFF) / 65536.0;
            rng = rng * RNG_MUL + RNG_ADD;
            float r4 = (rng & 0xFFFF) / 65536.0;

            float3 tertDir = CosineHemisphere(secHitNormal, r3, r4);
            float3 tertOrigin = secHitPoint + secHitNormal * 1e-3;

            uint tertIndex;
            float3 tertHitPoint;
            if (TraceClosest(tertOrigin, tertDir, tertIndex, tertHitPoint))
            {
                PrimitiveInfo tertPrim = Primitives[tertIndex];
                float3 tertHitNormal = FaceNormal(tertPrim, tertDir);
                float3 tertBaseXyz = IndirectBaseXyz(tertPrim, tertDir, tertHitPoint, heroIdx);

                float tertDirectTerm = 0.0;
                rng = rng * RNG_MUL + RNG_ADD;
                uint lightIdx3 = rng % NumLights;
                tertDirectTerm += UniformLightTerm(lightIdx3, tertHitPoint, tertHitNormal);

                float3 tertIncoming = tertBaseXyz * (AmbientLevel + tertDirectTerm);
                secBounce2Plus = secBaseXyz * tertIncoming;
            }

            bounce0 = baseXyz * directTerm;
            float3 localBounce1 = baseXyz * secIncoming;
            float3 localBounce2Plus = baseXyz * secBounce2Plus;
            indirect = localBounce1 + localBounce2Plus;
            xyz += indirect;
        }
    }

    float correction = DeterministicCorrection;
    correctedDirect = bounce0 * correction;
    correctedIndirect = indirect * correction;
    return xyz * correction;
}

// ── Biome indicator (ceiling category overlay) ────────────────────────

float3 XyzToLinRgb(float3 xyz)
{
    return float3(
        xyz.x * 3.2406 + xyz.y * (-1.5372) + xyz.z * (-0.4986),
        xyz.x * (-0.9689) + xyz.y * 1.8758 + xyz.z * 0.0415,
        xyz.x * 0.0557 + xyz.y * (-0.2040) + xyz.z * 1.0570);
}

float3 LinRgbToXyz(float3 c)
{
    return float3(
        c.r * 0.4124 + c.g * 0.3576 + c.b * 0.1805,
        c.r * 0.2126 + c.g * 0.7152 + c.b * 0.0722,
        c.r * 0.0193 + c.g * 0.1192 + c.b * 0.9505);
}

uint BiomeCategory(float3 hitPoint)
{
    if (VolSmokeMode == 2u) return 2u; // AlwaysFog
    if (VolSmokeMode == 3u) return 1u; // AlwaysGroundSmoke
    if (VolSmokeMode == 1u)            // Biome
    {
        float biomeWorldSize = VOL_CELL_SIZE * VOL_BIOME_CELLS;
        int bx = (int)floor(hitPoint.x / biomeWorldSize);
        int by = (int)floor(hitPoint.z / biomeWorldSize);
        if (!IsSmokeBiome(bx, by)) return 0u;
        return IsFogBiome(bx, by) ? 2u : 1u;
    }
    return 0u; // None
}

float3 ApplyBiomeIndicator(float3 xyz, float3 hitPoint)
{
    uint category = BiomeCategory(hitPoint);
    if (category == 0u)
        return xyz; // clear — leave the ceiling untinted

    float3 lin = XyzToLinRgb(xyz);
    float lum = max(dot(lin, float3(0.2126, 0.7152, 0.0722)), 0.0);
    float b = 0.35 + 0.65 * saturate(lum * 0.5);
    float3 hue = (category == 2u) ? float3(0.95, 0.45, 0.12)   // full fog -> amber
                                  : float3(0.25, 0.85, 0.30);  // ground   -> green
    return LinRgbToXyz(hue * b);
}

// ── Kernel ────────────────────────────────────────────────────────────

[numthreads(8, 8, 1)]
void CSMain(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= Width || tid.y >= Height)
        return;

    uint ix = tid.y * Width + tid.x;
    int x = int(tid.x);
    int y = int(tid.y);

    bool reset = (ResetFlag != 0u);
    uint sampleIdx = reset ? 0u : WavelengthCounter[ix];

    float jx, jy;
    if (SubPixelJitter != 0u)
    {
        uint baseHash = Hash2D(x, y);
        uint seed = baseHash + sampleIdx * RNG_MUL + RNG_ADD;
        seed = seed * RNG_MUL + RNG_ADD;
        jx = (seed & 0xFFFF) / 65536.0;
        seed = seed * RNG_MUL + RNG_ADD;
        jy = (seed & 0xFFFF) / 65536.0;
    }
    else
    {
        jx = 0.5;
        jy = 0.5;
    }

    float px = (2.0 * ((x + jx) * InvWidth) - 1.0) * AspectTanHalfFov;
    float py = (1.0 - 2.0 * ((y + jy) * InvHeight)) * TanHalfFov;
    float3 localDir = float3(px, py, ImgPlaneZ);
    float3 dir = normalize(RotateByQuaternion(CamRot, localDir));

    uint pixelHash = Hash2D(x, y);

    bool hit;
    float3 correctedDirect, correctedIndirect, gHitPoint, gNormal;
    float gAlbedo;
    float3 corrected = ShadeSample(CamPos, dir, pixelHash, sampleIdx, hit,
                                   correctedDirect, correctedIndirect, gHitPoint, gNormal, gAlbedo);

    // Ceiling biome-category overlay (debug). Oriented normal points down (-Y)
    // on a ceiling hit; gated off (BiomeIndicator=0) for the parity self-test.
    if (BiomeIndicator != 0u && hit && gNormal.y < -0.5)
        corrected = ApplyBiomeIndicator(corrected, gHitPoint);

    // Per-sample firefly clamp; also record how much was clamped (mirrors the
    // clampDelta L1 sum in PathTracer.cs) for the Phase 5.2 clamp heatmap + stat.
    float clampDelta = 0.0;
    if (SampleClamp > 0.0)
    {
        float3 unclamped = corrected;
        corrected = clamp(corrected, float3(0.0, 0.0, 0.0), float3(SampleClamp, SampleClamp, SampleClamp));
        clampDelta = abs(unclamped.x - corrected.x) + abs(unclamped.y - corrected.y) + abs(unclamped.z - corrected.z);
    }

    bool restart = reset || (LastHit[ix] != (hit ? 1u : 0u));
    uint prevCount = restart ? 0u : SampleCount[ix];
    if (SoftResetFlag != 0u && !restart)
        prevCount = min(prevCount, MotionSampleCap);
    uint count = min(prevCount + 1u, MaxSampleCount);
    bool clearMean = restart || (prevCount == 0u);

    float3 prevAccum = clearMean ? float3(0.0, 0.0, 0.0) : Accum[ix].xyz;
    float3 newAccum = prevAccum + (corrected - prevAccum) / float(count);

    float3 prevDirect = clearMean ? float3(0.0, 0.0, 0.0) : DirectAccum[ix].xyz;
    float3 newDirect = prevDirect + (correctedDirect - prevDirect) / float(count);

    float3 prevIndirect = clearMean ? float3(0.0, 0.0, 0.0) : IndirectAccum[ix].xyz;
    float3 newIndirect = prevIndirect + (correctedIndirect - prevIndirect) / float(count);

    Accum[ix] = float4(newAccum, 1.0);
    DirectAccum[ix] = float4(newDirect, 1.0);
    IndirectAccum[ix] = float4(newIndirect, 1.0);
    SampleCount[ix] = count;
    WavelengthCounter[ix] = sampleIdx + 1u;
    LastHit[ix] = hit ? 1u : 0u;

    // Welford variance of luma (mirrors PathTracer.cs: M2 += (ySample - newMean)^2,
    // using the just-updated mean; reset alongside the running mean). The resolve /
    // reduction derive variance = count > 1 ? M2 / (count - 1) : 0.
    float ySample = corrected.y;
    float dV = ySample - newAccum.y;
    float prevM2 = clearMean ? 0.0 : LumaM2[ix];
    LumaM2[ix] = prevM2 + dV * dV;

    // Clamp heatmap AOV: cumulative L1 clamp amount (reset with the mean) + the
    // per-frame clamped flag the reduction counts for ClampedPixelPercent.
    float prevClamp = clearMean ? 0.0 : ClampAmount[ix];
    ClampAmount[ix] = prevClamp + clampDelta;
    ClampHitFrame[ix] = (clampDelta > 0.0) ? 1u : 0u;

    // G-buffer for the resolve pass. Zeroed on a miss (the resolve gates use on the
    // hit mask). w carries the primary albedo for the Phase 5 Albedo debug view.
    HitPointOut[ix] = float4(gHitPoint, hit ? 1.0 : 0.0);
    NormalOut[ix] = float4(gNormal, hit ? gAlbedo : 0.0);

    // Volumetric fog for this pixel's camera segment (see Phase 4). Composited by
    // the resolve pass onto the converged surface.
    float fogT = 1.0;
    float3 fogInscatter = float3(0.0, 0.0, 0.0);
    if (hit)
        IntegrateVolumetric(CamPos, gHitPoint, dir, VolTime, fogT, fogInscatter);
    FogOut[ix] = float4(fogInscatter * DeterministicCorrection, fogT);
}
