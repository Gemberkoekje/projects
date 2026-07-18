// Phase 4 — trace pass with volumetrics (DXR 1.1 inline ray tracing).
//
// Identical to the Phase 3 trace pass (spectral NEE path tracing + G-buffer for
// the temporal resolve) with one addition: after shading the primary hit, the
// camera segment [CamPos -> hitPoint] is ray-marched through the participating
// medium (smoke/fog). The single-scattering result is composited onto the raw
// pre-correction XYZ as `xyz * T + inscatter` and the per-bounce AOVs are scaled
// by the transmittance T — a line-for-line port of the CPU's
//   if (hit) { volume = IntegrateVolumetricSegment(camPos, hitPoint, dir);
//              xyz = volume.Apply(xyz); direct/indirect/bounce* *= T; }
// in JobSystem.TraceCore (see VolumetricIntegration.cs / PathTracer.cs GetDensity*).
//
// The volumetric math mirrors RayTracer.Core's Phase4Reference.cs, which the unit
// tests pin to JobSystem.IntegrateVolumetricSegment. The resolve pass is unchanged
// (ResolvePhase3.hlsl is reused). Requires Shader Model 6.5 (RayQuery).

#define DETERMINISTIC_COUNT 50u
#define COMPANION_COUNT     4u
#define PI                  3.14159265
#define RNG_MUL             747796405u
#define RNG_ADD             2891336453u
#define SURFACE_MIRROR      1u   // SurfaceKind.Mirror
#define MAX_MIRROR_BOUNCES  8u   // JobSystem.MaxMirrorBounces

// Volumetric constants (mirror Phase4Reference / JobSystem).
#define ISOTROPIC_PHASE  0.07957747
#define VOL_CELL_SIZE    2.0  // MazeGeometryBuilder.CellSize
#define VOL_BIOME_CELLS  4.0  // Phase4Reference.BiomeSizeCells
static const float3 SMOKE_TINT = float3(0.97, 0.97, 0.97);

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
    uint  Surface;        // SurfaceKind of the primary material (0 diffuse, 1 mirror, …)
    float Pad1; float Pad2;
};

RaytracingAccelerationStructure Scene              : register(t0);
StructuredBuffer<PrimitiveInfo>  Primitives        : register(t1);
StructuredBuffer<float4>         DeterXYZ           : register(t2); // xyz used
StructuredBuffer<float>          MaterialReflectance: register(t3); // [material*50 + index]
StructuredBuffer<float4>         Lights             : register(t4); // xyz = world position
StructuredBuffer<float4>         LightColors        : register(t5); // xyz = colour (Phase 4 inscatter)

RWStructuredBuffer<float4> Accum            : register(u0); // xyz running mean (total)
RWStructuredBuffer<uint>   SampleCount      : register(u1);
RWStructuredBuffer<uint>   WavelengthCounter: register(u2);
RWStructuredBuffer<uint>   LastHit          : register(u4); // 1 = pixel hit geometry last sample
RWStructuredBuffer<float4> DirectAccum      : register(u5); // xyz running mean (direct)
RWStructuredBuffer<float4> IndirectAccum    : register(u6); // xyz running mean (indirect)
RWStructuredBuffer<float4> HitPointOut      : register(u7); // xyz = world hit point (G-buffer)
RWStructuredBuffer<float4> NormalOut        : register(u8); // xyz = oriented face normal (G-buffer)
RWStructuredBuffer<float4> FogOut           : register(u9); // xyz = inscatter*correction, w = transmittance

cbuffer Constants : register(b0)
{
    float3 CamPos;                float _pad0;
    float4 CamRot;               // quaternion (x, y, z, w)
    float  TanHalfFov;           float AspectTanHalfFov; float InvWidth; float InvHeight;
    float  ImgPlaneZ;            float DeterministicCorrection; float AmbientLevel; float LightIntensity;
    uint   Width;                uint  Height; uint MaxSampleCount; uint ResetFlag;
    uint   NumPrimitives;        uint  SubPixelJitter; uint NumLights; uint LightingMode; // 0 none, 1 direct, 2 NEE
    float  SampleClamp;          uint  SoftResetFlag; uint MotionSampleCap; float _pad1;
    // Volumetric (Phase 4). SoftResetFlag doubles as the volumetric IsMoving flag.
    uint   VolEnabled;           uint  VolSmokeMode; uint VolMarchSteps; uint VolShadowStepInterval;
    float  VolMaxMarchDistance;  float VolSigmaScaleFog; float VolSigmaScaleGround; float VolAnisotropyG;
    float  VolInscatterStrength; float VolEarlyOutTransmittance; uint BiomeIndicator; float VolTime;
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

float3 BaseXyz(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, uint pixelHash, uint sampleIdx)
{
    uint row; float atten;
    PatternShade(prim, rayDir, hitPoint, row, atten);

    uint heroIdx = ((pixelHash % DETERMINISTIC_COUNT) + (sampleIdx % DETERMINISTIC_COUNT)) % DETERMINISTIC_COUNT;
    uint stride = DETERMINISTIC_COUNT / COMPANION_COUNT;
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

// Scalar hero-wavelength reflectance of an indirect-bounce surface — the spectral throughput a
// bounce propagates (realism finding §1). The CIE weight is applied once, at the primary surface;
// a bounce must carry this scalar, never a CIE-tinted XYZ, or the curve is applied per bounce
// (the CIE²/CIE³ bug). IndirectBaseXyz is this × DeterXYZ(hero).
float IndirectReflHero(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, uint heroIdx)
{
    uint row; float atten;
    PatternShade(prim, rayDir, hitPoint, row, atten);
    return MaterialReflectance[row * DETERMINISTIC_COUNT + heroIdx] * atten;
}

float3 IndirectBaseXyz(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, uint heroIdx)
{
    return DeterXYZ[heroIdx].xyz * IndirectReflHero(prim, rayDir, hitPoint, heroIdx);
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

    RayQuery<RAY_FLAG_NONE> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, ray);
    q.Proceed();

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
// sub-types (mirror Phase4Reference.FogProfile / GroundProfile), so a "fog" biome
// is exactly corridor fog and a "ground" biome exactly ground smoke.
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
// Mirrors Phase4Reference.SmokeTurbulence / JobSystem.SmokeTurbulence: a wispy,
// clumpy 3D density multiplier so the medium billows rather than reading as a
// flat depth haze.

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
    // A slow domain drift makes the billows roll/advect over time (time = 0 gives
    // the original static field). The clumps are sampled by the resolve pass fresh
    // each frame, so this animates smoothly without disturbing the surface.
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

// Single-scattering along the camera segment. Returns transmittance + in-scatter.
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

    // Finding §2: reuse the last traced in-scatter visibility on the steps between traces.
    float3 cachedInscatterLight = float3(0.0, 0.0, 0.0);
    bool haveCachedInscatter = false;

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

        // Finding §2: trace shadow on the configured cadence, reuse the last traced value between.
        float3 localLight;
        if (VolShadowStepInterval == 0u)
        {
            localLight = EstimateInscatterLight(p, dir, false);
        }
        else
        {
            bool traceShadow = (i % VolShadowStepInterval) == 0u;
            if (traceShadow || !haveCachedInscatter)
            {
                cachedInscatterLight = EstimateInscatterLight(p, dir, true);
                haveCachedInscatter = true;
            }
            localLight = cachedInscatterLight;
        }

        inscatter += localLight * (scatterWeight * inscatterScale * phase);
        transmittance *= stepTransmittance;

        if (transmittance < VolEarlyOutTransmittance)
            break;
    }
}

// Full per-sample corrected XYZ; also returns direct/indirect components, the
// primary-hit mask, and the primary hit point + oriented normal for the G-buffer.
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

float TraceMirrorRadiance(float3 origin, float3 dir, uint heroIdx, inout uint rng)
{
    float throughput = 1.0;
    [loop]
    for (uint depth = 1u; depth <= MAX_MIRROR_BOUNCES; depth++)
    {
        uint idx; float3 hitPoint;
        if (!TraceClosest(origin, dir, idx, hitPoint))
            return 0.0; // miss: nothing to reflect

        PrimitiveInfo prim = Primitives[idx];
        float3 hitNormal = FaceNormal(prim, dir);
        uint row; float atten;
        PatternShade(prim, dir, hitPoint, row, atten);
        float refl = MaterialReflectance[row * DETERMINISTIC_COUNT + heroIdx] * atten;

        if (prim.Surface == SURFACE_MIRROR && depth < MAX_MIRROR_BOUNCES)
        {
            throughput *= refl;
            dir = reflect(dir, hitNormal);
            origin = hitPoint + hitNormal * 1e-3;
            continue;
        }

        return throughput * MirrorDiffuseScalar(refl, hitPoint, hitNormal, rng);
    }
    return 0.0;
}

float3 ShadeSample(float3 camPos, float3 primaryDir, uint pixelHash, uint sampleIdx,
                   out bool primaryHit, out float3 correctedDirect, out float3 correctedIndirect,
                   out float3 primaryHitPoint, out float3 primaryNormal)
{
    correctedDirect = float3(0.0, 0.0, 0.0);
    correctedIndirect = float3(0.0, 0.0, 0.0);
    primaryHitPoint = float3(0.0, 0.0, 0.0);
    primaryNormal = float3(0.0, 0.0, 0.0);

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

    // Specular mirror: follow the achromatic reflected ray (hero-only) and let
    // accumulation build the spectrum. G-buffer keeps the mirror surface so TAA
    // reprojects on it. Mirrors JobSystem.TraceCore's mirror branch.
    if (prim.Surface == SURFACE_MIRROR)
    {
        uint mRow; float mAtten;
        PatternShade(prim, primaryDir, hitPoint, mRow, mAtten);
        float mirrorRefl = MaterialReflectance[mRow * DETERMINISTIC_COUNT + heroIdx] * mAtten;

        uint mirrorRng = pixelHash + sampleIdx * 2654435761u + 1013904223u;
        float3 reflectDir = reflect(primaryDir, hitNormal);
        float3 reflectOrigin = hitPoint + hitNormal * 1e-3;
        float reflectedRadiance = mirrorRefl * TraceMirrorRadiance(reflectOrigin, reflectDir, heroIdx, mirrorRng);

        float3 mirrorXyz = DeterXYZ[heroIdx].xyz * reflectedRadiance;
        float corr = DeterministicCorrection;
        correctedDirect = mirrorXyz * corr;
        return mirrorXyz * corr;
    }

    float3 baseXyz = BaseXyz(prim, primaryDir, hitPoint, pixelHash, sampleIdx);

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
        float lightP; float3 lightDir; float lightDistSq; float lightCos;
        SelectLight(rngLight, hitPoint, hitNormal, lightP, lightDir, lightDistSq, lightCos);

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
        float r1 = (rng >> 16) / 65536.0;
        rng = rng * RNG_MUL + RNG_ADD;
        float r2 = (rng >> 16) / 65536.0;

        float3 sampleDir = CosineHemisphere(hitNormal, r1, r2);
        float3 secOrigin = hitPoint + hitNormal * 1e-3;

        uint secIndex;
        float3 secHitPoint;
        if (TraceClosest(secOrigin, sampleDir, secIndex, secHitPoint))
        {
            PrimitiveInfo secPrim = Primitives[secIndex];
            float3 secHitNormal = FaceNormal(secPrim, sampleDir);
            // Finding §1: scalar hero-λ throughput; CIE applied once at the primary surface (baseXyz).
            float secReflHero = IndirectReflHero(secPrim, sampleDir, secHitPoint, heroIdx);

            float secDirectTerm = 0.0;
            rng = rng * RNG_MUL + RNG_ADD;
            uint lightIdx2 = rng % NumLights;
            secDirectTerm += UniformLightTerm(lightIdx2, secHitPoint, secHitNormal);

            float secIncoming = secReflHero * (AmbientLevel + secDirectTerm);

            float secBounce2Plus = 0.0;
            rng = rng * RNG_MUL + RNG_ADD;
            float r3 = (rng >> 16) / 65536.0;
            rng = rng * RNG_MUL + RNG_ADD;
            float r4 = (rng >> 16) / 65536.0;

            float3 tertDir = CosineHemisphere(secHitNormal, r3, r4);
            float3 tertOrigin = secHitPoint + secHitNormal * 1e-3;

            uint tertIndex;
            float3 tertHitPoint;
            if (TraceClosest(tertOrigin, tertDir, tertIndex, tertHitPoint))
            {
                PrimitiveInfo tertPrim = Primitives[tertIndex];
                float3 tertHitNormal = FaceNormal(tertPrim, tertDir);
                float tertReflHero = IndirectReflHero(tertPrim, tertDir, tertHitPoint, heroIdx);

                float tertDirectTerm = 0.0;
                rng = rng * RNG_MUL + RNG_ADD;
                uint lightIdx3 = rng % NumLights;
                tertDirectTerm += UniformLightTerm(lightIdx3, tertHitPoint, tertHitNormal);

                float tertIncoming = tertReflHero * (AmbientLevel + tertDirectTerm);
                secBounce2Plus = secReflHero * tertIncoming;
            }

            bounce0 = baseXyz * directTerm;
            float3 localBounce1 = baseXyz * secIncoming;
            float3 localBounce2Plus = baseXyz * secBounce2Plus;
            indirect = localBounce1 + localBounce2Plus;
            xyz += indirect;
        }
    }

    // Surface only. The volumetric fog is computed separately in the kernel and
    // composited by the resolve pass (so it can animate without going through the
    // surface's temporal accumulation).
    float correction = DeterministicCorrection;
    correctedDirect = bounce0 * correction;
    correctedIndirect = indirect * correction;
    return xyz * correction;
}

// ── Biome indicator (ceiling category overlay) ────────────────────────
// Tints ceiling tiles by the smoke category of the biome they sit in so the
// maze's fog layout is legible at a glance: clear = untinted, ground smoke =
// green, full fog = amber. A debug overlay only — gated off by BiomeIndicator=0
// so the self-test's CPU cross-check (which has no overlay) still matches.

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

// 0 clear, 1 ground smoke, 2 full fog — matching what GetDensity does for the
// active smoke mode (Biome keys off the per-biome category; Always* are uniform).
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

    // Recolour the ceiling to the category hue, keeping (capped) surface
    // brightness so the tile grid still reads. A plain hue *multiply* would clamp
    // to white where the ceiling is blown out near lights, so we set the colour
    // outright — the low off-hue channels guarantee the tint survives saturation.
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
    float3 corrected = ShadeSample(CamPos, dir, pixelHash, sampleIdx, hit,
                                   correctedDirect, correctedIndirect, gHitPoint, gNormal);

    // Ceiling biome-category overlay (debug). Oriented normal points down (-Y)
    // on a ceiling hit; gated off (BiomeIndicator=0) for the parity self-test.
    if (BiomeIndicator != 0u && hit && gNormal.y < -0.5)
        corrected = ApplyBiomeIndicator(corrected, gHitPoint);

    if (SampleClamp > 0.0)
        corrected = clamp(corrected, float3(0.0, 0.0, 0.0), float3(SampleClamp, SampleClamp, SampleClamp));

    // Running-mean accumulation. Restart on a whole-frame reset or a hit/miss
    // flip; a soft reset caps the effective sample count so recent samples
    // dominate during motion while preserving the current mean.
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

    // G-buffer for the resolve pass. Zeroed on a miss (the resolve gates use on
    // the hit mask, so the miss value is never consumed for reprojection).
    HitPointOut[ix] = float4(gHitPoint, hit ? 1.0 : 0.0);
    NormalOut[ix] = float4(gNormal, 0.0);

    // Volumetric fog for this pixel's camera segment, computed fresh each frame at
    // VolTime and written for the resolve pass to composite onto the *converged*
    // surface. The march is deterministic (no MC noise), so the fog animates
    // cleanly with VolTime while the surface keeps accumulating undisturbed.
    float fogT = 1.0;
    float3 fogInscatter = float3(0.0, 0.0, 0.0);
    if (hit)
        IntegrateVolumetric(CamPos, gHitPoint, dir, VolTime, fogT, fogInscatter);
    FogOut[ix] = float4(fogInscatter * DeterministicCorrection, fogT);
}
