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
#define SURFACE_THINFILM     3u  // SurfaceKind.ThinFilm
#define SURFACE_EMISSIVE     6u  // SurfaceKind.Emissive (§5.3): self-emitting; shaded diffuse + MaterialEmission
#define SURFACE_BUBBLE       7u  // SurfaceKind.Bubble
#define BUBBLE_REFLECT_BOOST 45.0 // §2.6: boosts a bubble's Fresnel so thin-film rings read across it
// §A: effective glass path (world units) a shadow ray accrues per dielectric interface, so coloured
// glass casts a Beer–Lambert-tinted shadow. Must equal Optics.GlassShadowInterfaceThickness (CPU).
#define GLASS_SHADOW_INTERFACE_THICKNESS 0.03
#define BUBBLE_FIREFLY_CLAMP 1.5  // §2.6: per-sample luminance cap on a bubble (tames the reflect/transmit-split fireflies)
#define MAX_SPECULAR_BOUNCES 8u  // JobSystem.MaxSpecularBounces
#define MOVER_SPECULAR_BOUNCES 1u // §4.1/§4.4: a moving part's specular chain (the chrome ball reflects
                                  // the static scene once). The iterative loop below counts the primary
                                  // hit as depth 1, so the loop cap passed for a mover is this + 1 (the
                                  // recursive CPU/reference form uses this value directly at the first bounce).

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
    uint  Dynamic;        // 1 = moving part → cheap mover shading tier (§4.1); 0 = static
    float Pad0; float Pad1; float Pad2; // 112-byte stride, matches GpuPrimitive
};

RaytracingAccelerationStructure Scene              : register(t0);
StructuredBuffer<PrimitiveInfo>  Primitives        : register(t1);
StructuredBuffer<float4>         DeterXYZ           : register(t2); // white-balanced CIE; xyz used, .w = nm
StructuredBuffer<float>          MaterialReflectance: register(t3); // [material*50 + index]
StructuredBuffer<float>          MaterialEmission   : register(t10);// [material*50 + index] (§5.3 emissive)
StructuredBuffer<float4>         Lights             : register(t4); // xyz = world position, w = radius (§C1)
StructuredBuffer<float4>         LightColors        : register(t5); // xyz = colour (inscatter)
StructuredBuffer<float4>         LightDirs          : register(t9); // §C2: xyz = sun travel direction, w = 1 if directional
StructuredBuffer<float4>         RgbBasis           : register(t6); // [i].xyz = linearRGB→reflectance basis row i (plan §3.1)
StructuredBuffer<float4>         DecalPixels        : register(t7); // linear RGBA atlas, [layer*S*S + y*S + x]

// Analytic spheres (§0.4), carried as procedural-AABB geometry. Only read when the BLAS has
// sphere geometry (the shader gets CANDIDATE_PROCEDURAL_PRIMITIVE); a dummy buffer otherwise.
struct SphereInfo
{
    float CX; float CY; float CZ; float Radius;
    uint  MatRow; uint Surface; float Ior; float CauchyB;
    float P0; float P1; float P2; uint Dynamic; // Dynamic: 1 = the moving chrome ball (§4.1)
};
StructuredBuffer<SphereInfo> Spheres : register(t8);

// Caustic photon map (shadows-and-caustics-plan §B4), flattened into a GPU-uploadable uniform grid by
// PhotonMap.BuildGrid and gathered by TraceCausticEstimate (the port of CausticReference.EstimateXyz).
// A 1-element dummy binds when caustics are off (CausticEnabled == 0), so the gather is never entered.
struct CausticPhoton { float3 Pos; float3 Normal; float Power; uint WlIndex; }; // WlIndex → DeterXYZ row

// §B4 full GPU caustic pipeline. The forward photon *trace* (CausticCS) and the *binning* both run on
// the GPU, with no CPU readback: photons are deposited to PhotonsOut with an atomic PhotonCounter,
// their cell bounding box is accumulated by atomic min/max into Bbox, a 1-thread pass turns that into
// GridParams (origin cell + dims), and a link pass threads each photon onto its cell's singly-linked
// list (CellHead → PhotonNext). The gather (TraceCausticEstimate) walks those lists, reading the grid
// origin/dims from GridParams — so the whole map lives on the GPU and moving casters cost no stall.
// One EmitJob per caster (matching the CPU PhotonTracer's per-caster EmitInto call).
struct EmitJob
{
    float LX, LY, LZ; uint PhotonBase;            // light position + first global photon index
    float BMinX, BMinY, BMinZ; uint PhotonCount;  // caster bounds min + photons in this job
    float BMaxX, BMaxY, BMaxZ; uint Seed;         // caster bounds max + RNG seed
    float DirX, DirY, DirZ; uint Mode;            // Mode 1 = collimated beam along Dir from a point in [BMin,BMax]
};
StructuredBuffer<EmitJob>         EmitJobs      : register(t11);
RWStructuredBuffer<CausticPhoton> PhotonsOut    : register(u13); // deposited caustic photons (unsorted)
RWStructuredBuffer<uint>          PhotonCounter : register(u14); // atomic append counter (element 0)
RWStructuredBuffer<int>           PhotonNext    : register(u15); // per-photon linked-list next (-1 = tail)
RWStructuredBuffer<int>           CellHead      : register(u16); // per linear cell: head photon index (-1 = empty)
RWStructuredBuffer<int>           GridParams    : register(u17); // [minCX,minCY,minCZ, dimX,dimY,dimZ, photonCount, cellCount]
RWStructuredBuffer<int>           Bbox          : register(u18); // [minCX,minCY,minCZ, maxCX,maxCY,maxCZ] (atomic min/max)

cbuffer CausticEmitConstants : register(b1)
{
    uint  EmitJobCount; uint PhotonCapacity; uint EmitMaxBounces; float EmitCellSize;
    uint  CellHeadCapacity; float EmitBubbleBoost; uint EmitPhysicalEnergy; uint _emitPad3; // §12 boost, §8 physical energy
};

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
    float  RatSize;              uint  ShowRat; uint  RatLayer; uint VolShadowTransmittance; // §A: fog self-shadow opt-in
    // Caustics (§B4): the flattened photon-grid params, matching PhotonMap.BuildGrid / CausticReference.
    uint   CausticEnabled;       int   CausticMinCellX; int CausticMinCellY; int CausticMinCellZ;
    uint   CausticDimX;          uint  CausticDimY; uint CausticDimZ; float CausticCellSize;
    float  CausticRadius;        float CausticStrength; uint ShadowTransmitTriangles; float VolFarFadeStart; // §O: 0 = no fade
    // §7-13 realism knobs (mirror TraceConstants). Each defaults to the historical behaviour.
    uint   ViewDependentAlbedo;  uint  SpecularIndirect; uint AreaLightSolidAngleSampling; uint FogSpectralLighting;
    float  BubbleReflectBoost;   float SingleScatterAlbedo; uint PhaseAllLights; uint NestedDielectricStack;
    uint   IndirectFogShadows;   // §9 fog on indirect-bounce shadow rays
    uint   CausticSpectralAlbedo; uint _rpad1; uint _rpad2; // §8 per-photon-wavelength receiver reflectance
    // §4 lens (mirrors TraceConstants). Zoom needs no field: the CPU folds the lens's effective Fov into
    // TanHalfFov/AspectTanHalfFov above. LensApertureRadius 0 = pinhole ⇒ no aperture sampling, no RNG draw.
    float  LensApertureRadius;   float LensFocusDistance; float LensBladeRotation; float LensDistortK1;
    float  LensDistortK2;        float LensVignetteStrength; uint LensBlades; uint LensCatEye;
};

// §O3 procedural sky (outdoor-area-plan.md) — a separate cbuffer so the Constants layout above is
// untouched (roofed scenes stay bit-identical). SkyEnabled 0 → a ray that escapes returns black.
cbuffer SkyConstants : register(b2)
{
    float3 SkySunDir;    float SkyEnabled;      // sun travel direction; 1 = sky on
    float  SkyCosSunRadius; float SkySunRadiance; float SkyStrength; float SkyRayleighDepth;
    float  SkySunTempK;  float _skyPad0; float _skyPad1; float _skyPad2;
};

#define SKY_REF_NM      550.0
#define SKY_AIRMASS_FLR 0.15
#define SKY_C2_NM_K     1.438777e7   // second radiation constant hc/k (nm·K)

// Planck black-body shape at `wl` for temperature `tempK`, normalised to 1 at SKY_REF_NM (a colour shape
// factor, not absolute radiance). tempK <= 0 → 1 (flat white). Mirrors BlackbodySpectrum.Relative (§O9);
// used for the sky's sun and for per-light emission spectra.
float BlackbodyShape(float wl, float tempK)
{
    if (tempK <= 0.0) return 1.0;
    float r = SKY_REF_NM / wl;
    float denom    = exp(SKY_C2_NM_K / (wl * tempK)) - 1.0;
    float denomRef = exp(SKY_C2_NM_K / (SKY_REF_NM * tempK)) - 1.0;
    return r * r * r * r * r * (denomRef / denom); // (550/λ)^5 · (denomRef/denom)
}

// Luma-normalization for a blackbody emitter (finding §6): 1 / (CIE-Y-weighted mean of the shape over
// the hero set), so colour temperature changes hue only, not brightness. Matches the CPU
// WavelengthLookup.BlackbodyLumaNorm (same white-balanced DeterXYZ). tempK <= 0 → 1 (flat white).
float BlackbodyLumaNorm(float tempK)
{
    if (tempK <= 0.0) return 1.0;
    float weightSum = 0.0;
    float lumaSum = 0.0;
    [loop] for (uint i = 0u; i < DETERMINISTIC_COUNT; i++)
    {
        float y = DeterXYZ[i].y;
        weightSum += y;
        lumaSum += y * BlackbodyShape(DeterXYZ[i].w, tempK);
    }
    float mean = weightSum > 1e-9 ? lumaSum / weightSum : 1.0;
    return mean > 1e-9 ? 1.0 / mean : 1.0;
}

// The sky's solar shape is the black-body shape at the sun's temperature, luma-normalized (§6) so a
// warmer/cooler sun shifts the daylight hue without changing the dome's overall brightness.
float SkySolarShape(float wl) { return BlackbodyShape(wl, SkySunTempK) * BlackbodyLumaNorm(SkySunTempK); }

// Spectral sky radiance toward `dir` at `wavelengthNm` (port of Sky.Radiance): a bright sun disk toward
// −SkySunDir, else a single-scattering Rayleigh dome — 1/λ⁴ scattering saturated through 1−e^(−τ) so it
// reads blue (not violet) at the zenith and pales toward the horizon (aerial perspective).
// Rayleigh dome without the sun disk (realism finding §5): the sky radiance an indirect/bounce lookup
// must use, because a diffuse surface's sun illumination is already added by NEE on the directional sun.
float SkyDomeRadiance(float3 dir, float wavelengthNm)
{
    float3 d = normalize(dir);
    float3 toSun = normalize(-SkySunDir);
    float cosSun = dot(d, toSun);

    float cosZenith = saturate(d.y);
    float airmass = 1.0 / (cosZenith + SKY_AIRMASS_FLR);
    float lr = SKY_REF_NM / max(wavelengthNm, 1.0);
    float betaRel = lr * lr * lr * lr;          // (550/λ)⁴
    float tau = SkyRayleighDepth * betaRel * airmass;
    float inscatter = 1.0 - exp(-tau);
    float phase = 0.75 * (1.0 + cosSun * cosSun); // Rayleigh phase, mean 1 over the sphere
    return SkyStrength * SkySolarShape(wavelengthNm) * phase * inscatter;
}

float SkyRadiance(float3 dir, float wavelengthNm)
{
    float3 d = normalize(dir);
    float3 toSun = normalize(-SkySunDir);
    float cosSun = dot(d, toSun);
    if (cosSun >= SkyCosSunRadius)
        return SkySunRadiance; // camera/primary miss + specular reflections see the sun disk
    return SkyDomeRadiance(d, wavelengthNm);
}

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
    // §11: the 0.4 + 0.6·cosθ view-dependent face attenuation is a reciprocity-violating shading hack
    // that reads as baked-in ambient occlusion. ViewDependentAlbedo (default on, the established look)
    // keeps it; off gives a constant, view-independent base albedo so real lighting does the shading.
    float faceAtten = (ViewDependentAlbedo != 0u) ? (0.4 + 0.6 * cosTheta) : 1.0;
    if (prim.Pattern == 1u) // brick
    {
        bool mortar = IsMortar(u, w, prim.P0, prim.P1, prim.P2, prim.P3);
        atten = faceAtten;
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
        atten = faceAtten * BevelFactor(u, w, prim.P0, prim.P1, prim.P2); // §11
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

// Two-beam thin-film interference reflectance in [0,1] at one wavelength (§2.2), a port of
// Optics.ThinFilmReflectance. Bright where 2·n·d·cosθ_t is an odd multiple of λ/2.
float ThinFilmReflectance(float cosThetaI, float wavelengthNm, float filmThicknessNm, float filmIor)
{
    if (filmThicknessNm <= 0.0) return 1.0;
    cosThetaI = clamp(abs(cosThetaI), 0.0, 1.0);
    float sinI2 = 1.0 - cosThetaI * cosThetaI;
    float cosThetaT = sqrt(max(0.0, 1.0 - sinI2 / (filmIor * filmIor)));
    float halfPhase = 2.0 * 3.14159265358979 * filmIor * filmThicknessNm * cosThetaT / wavelengthNm;
    float s = sin(halfPhase);
    return s * s;
}

// Smooth world-position swirl (port of Optics.FilmSwirl) for the oil-slick thickness variation.
float FilmSwirl(float x, float z)
{
    return 0.55 * sin(4.1 * x + 3.3 * z)
         + 0.30 * sin(2.7 * x - 5.2 * z + 1.3)
         + 0.15 * sin(6.9 * x + 1.7 * z + 2.9);
}

// Thin-film interference factor for a hit at hero index `idx` (or 1 for non-thin-film),
// mirroring Phase1Reference.ThinFilmFactor: cosθ from the ray and the unit geometric normal;
// base thickness in prim.CauchyB with per-position swirl amplitude in prim.P0 (oil slick),
// film IOR in prim.Ior; wavelength (nm) rides DeterXYZ.w.
float ThinFilmFactor(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, uint idx)
{
    if (prim.Surface != SURFACE_THINFILM) return 1.0;
    float cosTheta = abs(dot(normalize(rayDir), float3(prim.NX, prim.NY, prim.NZ)));
    float thickness = max(1.0, prim.CauchyB + prim.P0 * FilmSwirl(hitPoint.x, hitPoint.z));
    float raw = ThinFilmReflectance(cosTheta, DeterXYZ[idx].w, thickness, prim.Ior);
    return saturate(0.5 + prim.P1 * (raw - 0.5)); // contrast (§2.2)
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
        float refl = MaterialReflectance[reflBase + idx] * atten * ThinFilmFactor(prim, rayDir, hitPoint, idx);
        xyz += DeterXYZ[idx].xyz * refl;
    }
    return xyz / float(COMPANION_COUNT);
}

// §4: BaseXyz weighted per companion wavelength by the chosen light's blackbody emission spectrum
// (× the constant §6 luma norm), so a coloured light's tint correlates with each wavelength's
// reflectance. Identical to BaseXyz for a flat-white light (lightTempK ≤ 0 → shape ≡ 1, norm ≡ 1),
// so temp-0 scenes are bit-identical. Mirrors the CPU PathTracer.LightEmission fold.
float3 DirectBaseXyz(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, uint pixelHash, uint sampleIdx, float lightTempK)
{
    float norm = BlackbodyLumaNorm(lightTempK);
    uint heroIdx = ((pixelHash % DETERMINISTIC_COUNT) + (sampleIdx % DETERMINISTIC_COUNT)) % DETERMINISTIC_COUNT;
    uint stride = DETERMINISTIC_COUNT / COMPANION_COUNT;

    if (prim.Pattern == 3u) // decal
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
            dxyz += DeterXYZ[idx].xyz * refl * BlackbodyShape(DeterXYZ[idx].w, lightTempK);
        }
        return dxyz * norm / float(COMPANION_COUNT);
    }

    uint row; float atten;
    PatternShade(prim, rayDir, hitPoint, row, atten);
    uint reflBase = row * DETERMINISTIC_COUNT;

    float3 xyz = float3(0.0, 0.0, 0.0);
    [unroll]
    for (uint k = 0u; k < COMPANION_COUNT; k++)
    {
        uint idx = (heroIdx + k * stride) % DETERMINISTIC_COUNT;
        float refl = MaterialReflectance[reflBase + idx] * atten * ThinFilmFactor(prim, rayDir, hitPoint, idx);
        xyz += DeterXYZ[idx].xyz * refl * BlackbodyShape(DeterXYZ[idx].w, lightTempK);
    }
    return xyz * norm / float(COMPANION_COUNT);
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
    float refl = MaterialReflectance[row * DETERMINISTIC_COUNT + heroIdx] * atten * ThinFilmFactor(prim, rayDir, hitPoint, heroIdx);
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

// Synthesise a PrimitiveInfo for a sphere hit (§0.4): the analytic outward normal in N, and
// the sphere's material row + optical params, so the whole quad-shading pipeline (PatternShade
// on Plain, HeroReflectance, ThinFilmFactor, dielectric, absorption) runs on it unchanged.
PrimitiveInfo SphereToPrim(SphereInfo s, float3 hitPoint)
{
    PrimitiveInfo p = (PrimitiveInfo)0;
    float3 n = normalize(hitPoint - float3(s.CX, s.CY, s.CZ));
    p.NX = n.x; p.NY = n.y; p.NZ = n.z;
    p.Pattern = 0u;              // Plain — no brick/decal patterning
    p.MatPrimary = s.MatRow;
    p.MatSecondary = s.MatRow;
    p.Surface = s.Surface;
    p.Ior = s.Ior;
    p.CauchyB = s.CauchyB;
    p.P0 = s.P0; p.P1 = s.P1; p.P2 = s.P2;
    p.Dynamic = s.Dynamic;
    return p;
}

// Nearest ray/sphere root ≥ tMin (half-b form), for the procedural candidate.
bool IntersectSphere(SphereInfo s, float3 origin, float3 dir, float tMin, out float t)
{
    float3 oc = origin - float3(s.CX, s.CY, s.CZ);
    float a = dot(dir, dir);
    float halfB = dot(oc, dir);
    float c = dot(oc, oc) - s.Radius * s.Radius;
    float disc = halfB * halfB - a * c;
    if (disc < 0.0) { t = 0.0; return false; }
    float sq = sqrt(disc);
    t = (-halfB - sq) / a;
    if (t < tMin) t = (-halfB + sq) / a;
    return t >= tMin;
}

bool TraceClosest(float3 origin, float3 dir, out PrimitiveInfo prim, out float3 hitPoint)
{
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = 1e-4;
    ray.TMax = 1e4;

    // Build-in reveal (§9.3): while RevealHeight is below the wall top, force non-opaque
    // traversal and reject hits above the rising line, so walls appear to grow from the
    // floor (the floor at y=0 always passes; the ceiling appears when the reveal completes).
    // Once RevealHeight >= WALL_HEIGHT the fast opaque path is used. The loop also intersects
    // any procedural sphere candidates (§0.4) — a sphere-free BLAS surfaces none, so no-op.
    uint rayFlags = (RevealHeight < WALL_HEIGHT) ? RAY_FLAG_FORCE_NON_OPAQUE : RAY_FLAG_NONE;
    RayQuery<RAY_FLAG_NONE> q;
    q.TraceRayInline(Scene, rayFlags, 0xFF, ray);
    while (q.Proceed())
    {
        uint ct = q.CandidateType();
        if (ct == CANDIDATE_PROCEDURAL_PRIMITIVE)
        {
            float t;
            if (IntersectSphere(Spheres[q.CandidatePrimitiveIndex()], origin, dir, q.RayTMin(), t))
                q.CommitProceduralPrimitiveHit(t);
        }
        else if (ct == CANDIDATE_NON_OPAQUE_TRIANGLE)
        {
            float tc = q.CandidateTriangleRayT();
            if ((origin.y + dir.y * tc) <= RevealHeight)
                q.CommitNonOpaqueTriangleHit(); // below the reveal line → solid
        }
    }

    uint status = q.CommittedStatus();
    if (status == COMMITTED_TRIANGLE_HIT)
    {
        prim = Primitives[q.CommittedPrimitiveIndex() >> 1];
        hitPoint = origin + dir * q.CommittedRayT();
        return true;
    }
    if (status == COMMITTED_PROCEDURAL_PRIMITIVE_HIT)
    {
        hitPoint = origin + dir * q.CommittedRayT();
        prim = SphereToPrim(Spheres[q.CommittedPrimitiveIndex()], hitPoint);
        return true;
    }
    prim = (PrimitiveInfo)0;
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
    while (q.Proceed())
    {
        if (q.CandidateType() == CANDIDATE_PROCEDURAL_PRIMITIVE)
        {
            SphereInfo s = Spheres[q.CandidatePrimitiveIndex()];
            float t;
            // Bubbles (§2.6) are see-through thin shells, so they must not cast solid shadows — a
            // stream of them would stack into muddy dark blobs on the walls. Skip them for shadow
            // rays; other spheres (diffuse/dielectric) still occlude.
            if (s.Surface != SURFACE_BUBBLE && IntersectSphere(s, origin, dir, q.RayTMin(), t))
                q.CommitProceduralPrimitiveHit(t); // any hit ends the search (shadow ray)
        }
    }
    return q.CommittedStatus() != COMMITTED_NOTHING; // triangle or sphere occludes
}

// Exact unpolarized Fresnel reflectance (port of Optics.FresnelDielectric). Defined here (ahead of
// the specular chain that also uses it) because the transmittance shadow ray below needs it.
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

// Beer–Lambert absorption σ at a wavelength, interpolated from red/green/blue anchors
// (port of Optics.AbsorptionAt, §2.3). For a dielectric the anchors ride in P0/P1/P2. Defined
// here (ahead of the specular chain that also uses it) because the transmittance shadow ray below
// tints coloured-glass shadows with it.
float AbsorptionAt(float sigmaR, float sigmaG, float sigmaB, float wavelengthNm)
{
    if (wavelengthNm <= 465.0) return sigmaB;
    if (wavelengthNm <= 532.0) return lerp(sigmaB, sigmaG, (wavelengthNm - 465.0) / (532.0 - 465.0));
    if (wavelengthNm <= 630.0) return lerp(sigmaG, sigmaR, (wavelengthNm - 532.0) / (630.0 - 532.0));
    return sigmaR;
}

// Transmittance-aware shadow ray (shadows-and-caustics-plan §A1): the fraction of light in [0,1]
// that survives to the light, the GPU twin of CPU BVH.Transmittance. Opaque geometry returns 0 (a
// hard shadow, bit-identical to TraceOccluded), but thin dielectric / bubble geometry attenuates
// instead of fully blocking — so bubbles cast a soft, thin-film-tinted shadow (closing the old
// divergence where TraceOccluded skipped bubbles entirely, giving them no shadow) and glass spheres
// a faint Fresnel-dimmed one.
//
// Dielectric *triangles* (glass windows §1.2, glass jewels) attenuate too — but only when the scene
// actually contains them (ShadowTransmitTriangles), so window-free scenes keep the pure hardware
// accept-first-hit fast path (opaque triangles auto-commit, ending the ray) and stay byte-identical
// to TraceOccluded — the selftest/regress mazes carry no glass and pay no candidate cost. When glass
// triangles are present we force them non-opaque so they surface as candidates we can multiply
// through (1 − Fresnel, dispersion via Cauchy to match the specular branch) and continue; every
// other triangle stays an opaque blocker and, with accept-first-hit, ends the ray on its first
// commit. Procedural spheres always run the accumulate loop. Order-independent: any opaque commit
// short-circuits to 0.
float TraceTransmittance(float3 origin, float3 dir, float maxDist, uint heroIdx)
{
    if (maxDist <= 1e-4)
        return 1.0;

    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = 1e-4;
    ray.TMax = maxDist;

    // Accept-first-hit ends the ray on the first committed (opaque) hit — a hard shadow. Only when
    // the scene has glass triangles do we also force triangles non-opaque, so they become candidates
    // we can attenuate-and-continue through instead of blocking; otherwise the fast path is exact.
    uint rayFlags = RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH;
    if (ShadowTransmitTriangles != 0u)
        rayFlags |= RAY_FLAG_FORCE_NON_OPAQUE;

    RayQuery<RAY_FLAG_NONE> q;
    q.TraceRayInline(Scene, rayFlags, 0xFF, ray);

    float transmittance = 1.0;
    float3 ndir = normalize(dir);
    while (q.Proceed())
    {
        uint ct = q.CandidateType();
        if (ct == CANDIDATE_PROCEDURAL_PRIMITIVE)
        {
            SphereInfo s = Spheres[q.CandidatePrimitiveIndex()];
            float t;
            if (!IntersectSphere(s, origin, dir, q.RayTMin(), t))
                continue;

            float3 outward = normalize((origin + dir * t) - float3(s.CX, s.CY, s.CZ));
            float cosv = abs(dot(ndir, outward));
            if (s.Surface == SURFACE_BUBBLE)
            {
                // The reflect probability is exactly the specular bubble branch's rReflect:
                // boosted Fresnel × base reflectance × thin-film tint (gravity-graded thickness).
                float grav = lerp(1.4, 0.6, (outward.y + 1.0) * 0.5);
                float d = s.CauchyB * grav;
                float base = MaterialReflectance[s.MatRow * DETERMINISTIC_COUNT + heroIdx];
                float film = ThinFilmReflectance(cosv, DeterXYZ[heroIdx].w, d, s.Ior);
                float rReflect = saturate(BubbleReflectBoost * FresnelDielectric(cosv, 1.0, s.Ior)) * base * film; // §12
                transmittance *= 1.0 - rReflect;
            }
            else if (s.Surface == SURFACE_DIELECTRIC)
            {
                float um = DeterXYZ[heroIdx].w * 1e-3;
                float n = s.Ior + s.CauchyB / (um * um);
                float sigma = AbsorptionAt(s.P0, s.P1, s.P2, DeterXYZ[heroIdx].w);
                transmittance *= (1.0 - FresnelDielectric(cosv, 1.0, n))
                               * exp(-sigma * GLASS_SHADOW_INTERFACE_THICKNESS);
            }
            else
            {
                q.CommitProceduralPrimitiveHit(t); // opaque sphere → hard shadow (ends the search)
            }
            // Transmissive spheres are not committed, so the ray keeps travelling toward the light.
        }
        else if (ct == CANDIDATE_NON_OPAQUE_TRIANGLE)
        {
            // Reached only when ShadowTransmitTriangles forced triangles non-opaque. A glass
            // window/jewel quad attenuates by its Fresnel-transmitted fraction × Beer–Lambert
            // absorption (the GPU twin of BVH.Transmittance's dielectric case; n = Ior + CauchyB/λ²
            // and σ from the P0/P1/P2 anchors, as in the specular branch) and lets the ray continue;
            // every other triangle is an opaque blocker that commits and, with accept-first-hit, ends
            // the search. A pane is two coincident quads, so a clear window keeps (1 − Fresnel)² and a
            // stained one is additionally tinted toward its transmitted hue — a faint, coloured shadow.
            PrimitiveInfo p = Primitives[q.CandidatePrimitiveIndex() >> 1];
            if (p.Surface == SURFACE_DIELECTRIC)
            {
                float cosv = abs(dot(ndir, float3(p.NX, p.NY, p.NZ)));
                float um = DeterXYZ[heroIdx].w * 1e-3;
                float n = p.Ior + p.CauchyB / (um * um);
                float sigma = AbsorptionAt(p.P0, p.P1, p.P2, DeterXYZ[heroIdx].w);
                transmittance *= (1.0 - FresnelDielectric(cosv, 1.0, n))
                               * exp(-sigma * GLASS_SHADOW_INTERFACE_THICKNESS);
            }
            else
            {
                q.CommitNonOpaqueTriangleHit(); // opaque triangle → hard shadow (ends the search)
            }
        }
    }

    // A committed hit is an opaque triangle or an opaque sphere → fully shadowed.
    if (q.CommittedStatus() != COMMITTED_NOTHING)
        return 0.0;

    return transmittance;
}

// ── Lighting (mirrors Phase2Reference) ────────────────────────────────

// §C1 area-light sampling: the shadow-ray target on a light — its centre for a point light (radius in
// Lights[i].w == 0; draws NO RNG, so point-light scenes are bit-identical to before) or a uniform
// point on the light's sphere for an area light, so the accumulated NEE visibility softens into a
// penumbra. Mirrors Light.SamplePoint (CPU) / Phase2Reference.SampleLightPoint.
float3 SampleLightPoint(uint lightIdx, float3 shadingPoint, inout uint rng)
{
    float3 pos = Lights[lightIdx].xyz;
    float radius = Lights[lightIdx].w;
    if (radius <= 0.0)
        return pos;

    // §10: cone-sample the visible cap (PBRT uniform-cone) instead of the whole sphere, so only the
    // physically-illuminating front of the light contributes and the near-contact penumbra tightens.
    // Off, or the shading point inside the sphere → whole-sphere (historical). Mirrors Light.SamplePoint.
    if (AreaLightSolidAngleSampling != 0u)
    {
        float3 toCenter = pos - shadingPoint;
        float dc2 = dot(toCenter, toCenter);
        if (dc2 > radius * radius)
        {
            float dc = sqrt(dc2);
            float cosMax = sqrt(max(0.0, 1.0 - radius * radius / dc2));
            rng = rng * RNG_MUL + RNG_ADD; float u1 = float(rng) / 4294967296.0;
            rng = rng * RNG_MUL + RNG_ADD; float u2 = float(rng) / 4294967296.0;
            float cosT = 1.0 - u1 * (1.0 - cosMax);
            float sinT = sqrt(max(0.0, 1.0 - cosT * cosT));
            float phiC = 2.0 * PI * u2;
            float3 w = toCenter / dc;
            float3 t = abs(w.x) > 0.1 ? normalize(float3(w.y, -w.x, 0.0)) : normalize(float3(0.0, w.z, -w.y));
            float3 b = cross(w, t);
            float3 dir = sinT * cos(phiC) * t + sinT * sin(phiC) * b + cosT * w;
            float ds = dc * cosT - sqrt(max(0.0, radius * radius - dc2 * sinT * sinT));
            return shadingPoint + ds * dir;
        }
    }

    rng = rng * RNG_MUL + RNG_ADD;
    float z = 2.0 * (float(rng) / 4294967296.0) - 1.0;
    rng = rng * RNG_MUL + RNG_ADD;
    float phi = 2.0 * PI * (float(rng) / 4294967296.0);
    float s = sqrt(max(0.0, 1.0 - z * z));
    return pos + radius * float3(s * cos(phi), s * sin(phi), z);
}

// §C1/§C2 light geometry (mirrors Light.SelectionWeight / SampleShadowRay). A light is a point light
// (LightDirs[i].w == 0: position Lights[i].xyz, sphere radius Lights[i].w) or a directional sun
// (LightDirs[i].w == 1: travel direction LightDirs[i].xyz, and Lights[i].w reused as angular radius).

// Importance weight to pick a light in NEE: a point light's 1/d²·cos, a sun's plain cos (no falloff).
float LightSelectionWeight(uint i, float3 samplePoint, float3 normal)
{
    if (LightDirs[i].w > 0.5)
    {
        float c = dot(normal, normalize(-LightDirs[i].xyz));
        return c > 0.0 ? c : 0.0;
    }
    float3 toLight = Lights[i].xyz - samplePoint;
    float dsq = dot(toLight, toLight);
    float cosv = dot(normal, toLight / sqrt(dsq));
    return cosv > 0.0 ? 1.0 / max(dsq, 1e-6) : 0.0;
}

// A direction within the sun's angular-radius cone (Lights[i].w radians); the axis exactly (no RNG)
// when the angular radius is 0. Mirrors Light.SampleCone.
float3 SampleSunDir(uint i, float3 axis, inout uint rng)
{
    float radius = Lights[i].w;
    if (radius <= 0.0)
        return axis;
    rng = rng * RNG_MUL + RNG_ADD; float u1 = float(rng) / 4294967296.0;
    rng = rng * RNG_MUL + RNG_ADD; float u2 = float(rng) / 4294967296.0;
    float cosMax = cos(radius);
    float cosT = 1.0 - u1 * (1.0 - cosMax);
    float sinT = sqrt(max(0.0, 1.0 - cosT * cosT));
    float phi = 2.0 * PI * u2;
    float3 t = abs(axis.x) > 0.1 ? normalize(float3(axis.y, -axis.x, 0.0)) : normalize(float3(0.0, axis.z, -axis.y));
    float3 b = cross(axis, t);
    return normalize(sinT * cos(phi) * t + sinT * sin(phi) * b + cosT * axis);
}

// Samples one shadow ray toward light i; writes dir/dist and returns the geometry factor (cos/d² for
// a point light, plain cos for a sun — no falloff). Draws RNG only for a soft light (a radius-0 light
// draws none and reproduces the old hard shadow byte-for-byte).
float SampleLightShadowRay(uint i, float3 samplePoint, float3 normal, inout uint rng, out float3 dir, out float dist)
{
    if (LightDirs[i].w > 0.5)
    {
        float3 toSun = normalize(-LightDirs[i].xyz);
        dist = 1e4; // effectively infinite — any occluder in the scene blocks the sun
        if (toSun.y <= 0.0) { dir = toSun; return 0.0; } // §O8: sun below the horizon (night) casts no light
        dir = SampleSunDir(i, toSun, rng);
        float c = dot(normal, dir);
        return c > 0.0 ? c : 0.0;
    }
    float3 toLight = SampleLightPoint(i, samplePoint, rng) - samplePoint;
    float distSq = max(dot(toLight, toLight), 1e-6);
    dist = sqrt(distSq);
    dir = toLight / dist;
    float cosv = dot(normal, dir);
    return cosv > 0.0 ? cosv / distSq : 0.0;
}

uint SelectLight(inout uint rng, float3 samplePoint, float3 normal, out float outP)
{
    float totalW = 0.0;
    for (uint li = 0u; li < NumLights; li++)
        totalW += LightSelectionWeight(li, samplePoint, normal);

    rng = rng * RNG_MUL + RNG_ADD;

    if (totalW > 0.0)
    {
        float u = float(rng) / 4294967296.0 * totalW;
        float acc = 0.0;
        for (uint li = 0u; li < NumLights; li++)
        {
            float w = LightSelectionWeight(li, samplePoint, normal);
            acc += w;
            if (u <= acc)
            {
                outP = w / totalW;
                return li;
            }
        }
        uint last = NumLights - 1u;
        outP = LightSelectionWeight(last, samplePoint, normal) / totalW;
        return last;
    }

    uint idx = rng % NumLights;
    outP = 1.0 / float(NumLights);
    return idx;
}

float UniformLightTerm(uint lightIdx, float3 hitPoint, float3 hitNormal, uint heroIdx, inout uint rng)
{
    float3 lightDir; float dist;
    float geom = SampleLightShadowRay(lightIdx, hitPoint, hitNormal, rng, lightDir, dist);
    if (geom <= 0.0)
        return 0.0;

    float3 shadowOrigin = hitPoint + hitNormal * 1e-3;
    float vis = TraceTransmittance(shadowOrigin, lightDir, dist - 2e-3, heroIdx);
    if (vis <= 0.0)
        return 0.0;

    // §O9: modulate by the light's blackbody emission at this hero wavelength (LightColors[i].w = temp K),
    // luma-normalized (§6) so temperature is chromatic-only.
    float emission = BlackbodyShape(DeterXYZ[heroIdx].w, LightColors[lightIdx].w) * BlackbodyLumaNorm(LightColors[lightIdx].w);
    return vis * (geom * LightIntensity * float(NumLights)) * emission;
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
    // §O: this profile keeps a 0.70 floor that otherwise fills all Y. Indoors the roof caps it, but over
    // the open garden it billows up into the sky like fire smoke. Fade it out between the wall top and a
    // low ceiling so outdoor fog reads as a ground/garden bank under a clear sky. The fade starts at
    // WALL_HEIGHT, so the roofed indoor volume (y <= WALL_HEIGHT) multiplies by exactly 1.0 — the goldens
    // (all roofed scenes) stay bit-exact.
    float heightCeil = 1.0 - smoothstep(WALL_HEIGHT, WALL_HEIGHT + 2.0, p.y);
    return 0.50 * thickCoverage * heightWeight * heightCeil;
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

// Fog self-shadow (shadows-and-caustics-plan §A2): the medium's transmittance in [0,1] from an
// in-scatter sample toward a light, marched over the density field (advected by time) so thick smoke
// nearer the light dims the in-scatter — real volumetric shafts that fall off with depth. A
// line-for-line port of Phase4Reference.InscatterShadowTransmittance (a coarser quarter-step march
// than the camera one). Off unless the VolShadowTransmittance opt-in is set (High/Ultra).
float InscatterShadowTransmittance(float3 from, float3 lightDir, float dist, float time)
{
    float marchLength = min(dist, max(VolMaxMarchDistance, 0.0));
    if (marchLength <= 1e-5)
        return 1.0;

    uint steps = max(2u, VolMarchSteps / 4u);
    float stepLength = marchLength / float(steps);
    float sigmaScale = (VolSmokeMode == 3u) ? VolSigmaScaleGround : VolSigmaScaleFog;
    float transmittance = 1.0;

    for (uint i = 0u; i < steps; i++)
    {
        float distance = (float(i) + 0.5) * stepLength;
        float3 p = from + lightDir * distance;
        float density = GetDensity(p, VolSmokeMode, time);
        if (density <= 0.0)
            continue;

        float sigmaT = density * sigmaScale;
        if (sigmaT <= 0.0)
            continue;

        transmittance *= exp(-sigmaT * stepLength);
        if (transmittance < VolEarlyOutTransmittance)
            return transmittance;
    }

    return transmittance;
}

// §9: fog transmittance along an indirect-bounce shadow ray toward a point light, so thick smoke dims a
// bounce's direct lighting (the GPU otherwise has no fog on shadow rays). Off / directional / no fog → 1.
// Marched toward the light centre (the fog term tolerates that approximation), reusing the self-shadow march.
float IndirectFogShadowFactor(float3 from, uint lightIdx)
{
    if (IndirectFogShadows == 0u || LightDirs[lightIdx].w > 0.5)
        return 1.0;
    float3 toL = Lights[lightIdx].xyz - from;
    float dL = max(length(toL), 1e-4);
    return InscatterShadowTransmittance(from, toL / dL, dL, VolTime);
}

float3 EstimateInscatterLight(float3 samplePoint, float3 viewDirection, bool traceShadow)
{
    if (NumLights == 0u)
        return SMOKE_TINT;

    float3 lighting = float3(0.0, 0.0, 0.0);
    for (uint i = 0u; i < NumLights; i++)
    {
        float3 lightDir; float dist; float lightWeight;
        if (LightDirs[i].w > 0.5) // §C2/§O8: directional sun — parallel (no falloff), dark below the horizon
        {
            float3 toSun = normalize(-LightDirs[i].xyz);
            if (toSun.y <= 0.0) continue; // night: a below-horizon sun lights no fog (was a bogus point at origin)
            lightDir = toSun;
            dist = 1e4;
            lightWeight = LightIntensity; // parallel rays: full strength, no inverse-square (god-rays through fog)
        }
        else
        {
            float3 toLight = Lights[i].xyz - samplePoint;
            float distSq = max(dot(toLight, toLight), 1e-6);
            dist = sqrt(distSq);
            lightDir = toLight / dist;
            lightWeight = LightIntensity / distSq;
        }

        if (traceShadow)
        {
            float3 shadowOrigin = samplePoint + lightDir * 1e-3;
            if (TraceOccluded(shadowOrigin, lightDir, dist - 2e-3))
                continue;
        }
        // Dim the in-scatter by the fog optical depth toward this light (§A2), tied to the same
        // shadow-step cadence (traceShadow) as the geometry occlusion above.
        if (VolShadowTransmittance != 0u && traceShadow)
            lightWeight *= InscatterShadowTransmittance(samplePoint, lightDir, dist, VolTime);

        // §9: with PhaseAllLights, weight each light by the phase toward *it* (normalised by the isotropic
        // phase, the neutral outer factor); no anisotropy → 1, so a no-op. Mirrors the CPU integrator.
        float phaseFactor = 1.0;
        if (PhaseAllLights != 0u)
        {
            float g = clamp(VolAnisotropyG, -0.95, 0.95);
            if (abs(g) > 1e-4)
            {
                float cosTheta = clamp(dot(lightDir, -viewDirection), -1.0, 1.0);
                float denom = 1.0 + g * g - 2.0 * g * cosTheta;
                phaseFactor = (1.0 - g * g) / (4.0 * PI * pow(denom, 1.5)) / ISOTROPIC_PHASE;
            }
        }

        lighting += LightColors[i].xyz * (lightWeight * phaseFactor);
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

    // Full march-step count whether the camera is moving or still. Halving the steps while moving (a perf
    // trick) changed the fog integral, so the fog visibly shifted the instant the camera started/stopped —
    // a flicker. The frame is unpipelined and fog is a single march per pixel, so full steps while moving
    // is cheap enough on the target GPU; consistency matters more here. (Goldens render still, so bit-exact.)
    uint steps = max(1u, VolMarchSteps);

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
        // §O: fade the fog to nothing over the far end of the march so the camera-relative cutoff doesn't
        // read as a hard "fog sphere" in the open garden. Off (VolFarFadeStart <= 0) → bit-exact indoors.
        if (VolFarFadeStart > 0.0)
            density *= 1.0 - smoothstep(VolFarFadeStart, max(VolMaxMarchDistance, VolFarFadeStart + 1e-3), distance);
        if (density <= 0.0)
            continue;

        float sigmaT = density * sigmaScale;
        if (sigmaT <= 0.0)
            continue;

        float opticalDepth = sigmaT * stepLength;
        float stepTransmittance = exp(-opticalDepth);
        float scatterWeight = transmittance * (1.0 - stepTransmittance);
        // §9: PhaseAllLights folds the per-light phase into EstimateInscatterLight → neutral outer factor.
        float phase = (PhaseAllLights != 0u) ? ISOTROPIC_PHASE : VolEvaluatePhase(dir, p);

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

        // §9: single-scattering albedo scales the in-scatter (absorption keeps the rest); 1 = historical.
        inscatter += localLight * (scatterWeight * inscatterScale * phase * SingleScatterAlbedo);
        transmittance *= stepTransmittance;

        if (transmittance < VolEarlyOutTransmittance)
            break;
    }
}

// ── Mirror specular reflection (spectral-effects-plan.md §1.1) ─────────
// Line-for-line port of Phase2Reference.MirrorDiffuseScalar / TraceMirrorRadiance
// (and JobSystem's mirror branch). HLSL has no recursion, so the mirror chain is
// an iterative loop accumulating a reflectance product.

// MirrorDiffuseScalar is defined below HeroReflectance (which it now calls for the §7 indirect bounce),
// since HLSL requires definition-before-use.

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
    return MaterialReflectance[row * DETERMINISTIC_COUNT + heroIdx] * atten * ThinFilmFactor(prim, dir, hitPoint, heroIdx);
}

// Self-emitted radiance at the hero wavelength for a hit (§5.3): the emissive twin of HeroReflectance,
// read raw from the MaterialEmission table by the PatternShade row (no pattern atten — emission is a
// source, not a reflection). 0 for any non-emitting material, so the no-emission path is unchanged.
// Mirrors the emission lookup in Phase2Reference.ShadeSample / TraceSpecularRadiance.
float HeroEmission(PrimitiveInfo prim, float3 dir, float3 hitPoint, uint heroIdx)
{
    uint row; float atten;
    PatternShade(prim, dir, hitPoint, row, atten);
    return MaterialEmission[row * DETERMINISTIC_COUNT + heroIdx];
}

// §7: one cosine-weighted indirect bounce leaving a diffuse surface reached through a specular chain,
// so a mirror's / glass's world is not conspicuously flatter and darker than the directly-viewed one.
// Mirrors JobSystem.SpecularIndirectTerm — the same uniform-light one-bounce the primary path uses.
float SpecularIndirectTerm(float3 pt, float3 normal, inout uint rng, uint heroIdx)
{
    rng = rng * RNG_MUL + RNG_ADD; float r1 = (rng >> 16) / 65536.0;
    rng = rng * RNG_MUL + RNG_ADD; float r2 = (rng >> 16) / 65536.0;
    float3 sampleDir = CosineHemisphere(normal, r1, r2);
    float3 secOrigin = pt + normal * 1e-3;

    PrimitiveInfo secPrim; float3 secHitPoint;
    if (TraceClosest(secOrigin, sampleDir, secPrim, secHitPoint))
    {
        float3 secHitNormal = FaceNormal(secPrim, sampleDir);
        float secRefl = HeroReflectance(secPrim, sampleDir, secHitPoint, heroIdx);
        rng = rng * RNG_MUL + RNG_ADD;
        uint lightIdx2 = rng % NumLights;
        float secDirect = UniformLightTerm(lightIdx2, secHitPoint, secHitNormal, heroIdx, rng)
            * IndirectFogShadowFactor(secHitPoint, lightIdx2); // §9
        return secRefl * (AmbientLevel + secDirect);
    }
    if (SkyEnabled != 0.0)
        return SkyDomeRadiance(sampleDir, DeterXYZ[heroIdx].w); // skylight (no sun disk, §5)
    return 0.0;
}

// ── Mirror specular reflection (spectral-effects-plan.md §1.1) ─────────
// The diffuse terminal of the specular chain: refl × (ambient + direct [+ §7 indirect]). Mirrors
// Phase2Reference.MirrorDiffuseScalar (which has no §7, so the parity self-test — §7 off — matches).
float MirrorDiffuseScalar(float refl, float3 pt, float3 normal, inout uint rng, uint heroIdx)
{
    float ambient = 1.0;
    float direct = 0.0;
    float indirect = 0.0;
    if (LightingMode != 0u && NumLights > 0u)
    {
        ambient = AmbientLevel;
        float p;
        uint chosen = SelectLight(rng, pt, normal, p);
        float3 ldir; float dist;
        float geom = SampleLightShadowRay(chosen, pt, normal, rng, ldir, dist);
        if (geom > 0.0)
        {
            float vis = 1.0;
            if (LightingMode == 2u) // NEE
            {
                float3 shadowOrigin = pt + normal * 1e-3;
                vis = TraceTransmittance(shadowOrigin, ldir, dist - 2e-3, heroIdx);
            }
            if (vis > 0.0)
                // §O9 (finding §3): tint by the chosen light's blackbody emission at the hero wavelength,
                // so a surface seen in a mirror is warmed/cooled by the light exactly as the primary
                // direct term (and the CPU DirectTermAt) already are. Flat-white lights (temp 0) → ×1.
                direct = vis * (geom * LightIntensity / max(p, 1e-9))
                    * BlackbodyShape(DeterXYZ[heroIdx].w, LightColors[chosen].w)
                    * BlackbodyLumaNorm(LightColors[chosen].w);
        }
        // §7: add one indirect bounce (off by default). Draws no RNG when off, so the specular chain's
        // random stream — and every parity self-test / golden — is byte-identical to before.
        if (SpecularIndirect != 0u)
            indirect = SpecularIndirectTerm(pt, normal, rng, heroIdx);
    }
    return refl * (ambient + direct + indirect);
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
// so smoke shows in reflections/refractions, and Beer–Lambert absorption applied along
// every segment travelled inside a coloured glass (§2.3). Iterative (HLSL has no
// recursion), tracing from the camera; the camera→primary segment (depth 1) is fogged by
// FogOut/resolve, so it is skipped here to avoid double-counting.
// Mirrors JobSystem.TraceSpecularRadiance.
float3 TraceSpecularRadiance(float3 origin, float3 dir, uint heroIdx, inout uint rng, uint maxBounces)
{
    float weight = 1.0;             // Π (transmittance × reflectance) so far
    float3 accum = float3(0.0, 0.0, 0.0);
    bool inGlass = false;
    float mediumSigma = 0.0;        // Beer–Lambert σ of the medium the ray is inside (0 = clear air)
    // §13 medium stack (NestedDielectricStack): the IOR + absorption σ of each nested dielectric the ray
    // is inside, so glass-in-glass gets the right IOR pairing (not the inGlass boolean's air pairing) and a
    // bubble keeps the enclosing glass's σ. Enter/exit is decided by IOR-matching (a hit whose IOR equals
    // the current medium's = exiting it), which reproduces the boolean exactly for a single object. Only
    // used when the knob is on; the off path keeps inGlass/mediumSigma byte-identical.
    float sIor[4]; float sSig[4]; int sTop = 0;
    [loop]
    for (uint depth = 1u; depth <= MAX_SPECULAR_BOUNCES; depth++)
    {
        PrimitiveInfo prim; float3 hitPoint;
        bool sceneHit = TraceClosest(origin, dir, prim, hitPoint);
        float sceneT = sceneHit ? length(hitPoint - origin) : 1e30;

        // The rat is a real object for reflected rays (depth > 1); the primary ray (depth 1) tests
        // it in ShadeSample instead, so it is skipped here to avoid testing the billboard twice.
        float3 ratHitPoint; float ratRefl;
        bool ratHit = depth > 1u && IntersectRat(origin, dir, sceneT, heroIdx, ratHitPoint, ratRefl);

        if (!sceneHit && !ratHit)
        {
            // Ray escapes. Through an open roof (§O2/§O3) it sees the sky, not black — so mirrors reflect
            // and glass/crystal refract the sky instead of returning black. Weighted by the specular
            // throughput so far; DeterministicCorrection is applied by the caller. Sky off → adds nothing
            // (roofed / golden scenes unchanged). Emissive, so it keeps the fog already accumulated.
            if (SkyEnabled != 0.0)
                accum += weight * DeterXYZ[heroIdx].xyz * SkyRadiance(dir, DeterXYZ[heroIdx].w);
            return accum;
        }

        float3 segEnd = ratHit ? ratHitPoint : hitPoint;

        // Beer–Lambert absorption for the segment just travelled inside a coloured glass (§2.3).
        if (mediumSigma > 0.0)
            weight *= exp(-mediumSigma * length(segEnd - origin));

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

        float3 hitNormal = FaceNormal(prim, dir);

        if (prim.Surface == SURFACE_MIRROR && depth < maxBounces)
        {
            weight *= HeroReflectance(prim, dir, hitPoint, heroIdx);
            dir = reflect(dir, hitNormal);
            origin = hitPoint + hitNormal * 1e-3;
            continue;
        }

        if (prim.Surface == SURFACE_DIELECTRIC && depth < maxBounces)
        {
            // Dispersion (§2.1): the IOR varies with the hero wavelength (Cauchy n = A + B/λ²),
            // so each accumulated wavelength refracts by a different angle. The hero wavelength
            // (nm) rides in DeterXYZ[heroIdx].w; CauchyB == 0 leaves n == prim.Ior (non-dispersive).
            float um = DeterXYZ[heroIdx].w * 1e-3;   // hero wavelength in µm
            float n = prim.Ior + prim.CauchyB / (um * um);
            float hitSig = AbsorptionAt(prim.P0, prim.P1, prim.P2, DeterXYZ[heroIdx].w);

            // §13: pick the from/to IOR either from the medium stack (nested-correct) or the inGlass
            // boolean (legacy, single-level). exiting = leaving the current medium on transmit.
            float iorFrom, iorTo; bool exiting;
            if (NestedDielectricStack != 0u)
            {
                float curIor = (sTop > 0) ? sIor[sTop - 1] : 1.0;
                exiting = (sTop > 0) && abs(curIor - n) < 1e-3;
                iorFrom = curIor;
                iorTo = exiting ? ((sTop > 1) ? sIor[sTop - 2] : 1.0) : n;
            }
            else
            {
                exiting = inGlass;
                iorFrom = inGlass ? n : 1.0;
                iorTo   = inGlass ? 1.0 : n;
            }

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
                if (NestedDielectricStack != 0u)
                {
                    if (exiting) { if (sTop > 0) sTop--; }
                    else if (sTop < 4) { sIor[sTop] = n; sSig[sTop] = hitSig; sTop++; }
                    mediumSigma = (sTop > 0) ? sSig[sTop - 1] : 0.0; // top of the stack's absorption
                }
                else
                {
                    inGlass = !inGlass;
                    // Enter this glass's absorption, or exit to clear air (§2.3).
                    mediumSigma = inGlass ? hitSig : 0.0;
                }
            }
            continue;
        }

        if (prim.Surface == SURFACE_BUBBLE && depth < maxBounces)
        {
            // Thin-shell soap bubble (§2.6): the film is microns thin, so the background passes
            // nearly straight through (mostly see-through — real bubbles don't warp like a solid
            // lens) with a vivid thin-film reflection. The film reflectance IS the reflect
            // probability, so the colour is which wavelengths reflect most; a gravity thickness
            // gradient (thin at the top, thick at the bottom, keyed on the sphere's outward normal
            // prim.NY) gives the multiple swirling colour bands, and the Fresnel term (boosted,
            // clamped) makes the grazing rim bright. prim.Ior = film index, prim.CauchyB = base nm.
            float filmIor = prim.Ior;
            float cosI = abs(dot(normalize(dir), hitNormal));
            float grav = lerp(1.4, 0.6, (prim.NY + 1.0) * 0.5); // bottom thicker → top thinner: shifts the ring colour
            float d = prim.CauchyB * grav;
            float base = MaterialReflectance[prim.MatPrimary * DETERMINISTIC_COUNT + heroIdx];
            float film = ThinFilmReflectance(cosI, DeterXYZ[heroIdx].w, d, filmIor);
            float rReflect = saturate(BubbleReflectBoost * FresnelDielectric(cosI, 1.0, filmIor)) * base * film; // §12
            rng = rng * RNG_MUL + RNG_ADD;
            float u = float(rng) / 4294967296.0;
            if (u < rReflect)
            {
                dir = reflect(dir, hitNormal);
                origin = hitPoint + hitNormal * 1e-3;
            }
            else
            {
                origin = hitPoint + dir * 1e-3;  // transmit straight through the thin shell
            }
            continue;
        }

        // Terminal diffuse hit (+ any self-emission: a neon insert seen in the chrome ball / glass dome).
        // MirrorDiffuseScalar consumes rng first (order preserved); HeroEmission draws none. Matches the
        // Phase2Reference.TraceSpecularRadiance terminal; +0 for a non-emitting surface (§5.3).
        float termScalar = MirrorDiffuseScalar(HeroReflectance(prim, dir, hitPoint, heroIdx), hitPoint, hitNormal, rng, heroIdx);
        termScalar += HeroEmission(prim, dir, hitPoint, heroIdx);
        accum += weight * DeterXYZ[heroIdx].xyz * termScalar;
        return accum;
    }
    return accum;
}

// Caustic density estimate at a diffuse receiver (shadows-and-caustics-plan §B4): the same
// constant-kernel gather CausticReference.EstimateXyz / PhotonMap.EstimateXyz compute — the
// ±ceil(r/cell) cell scan (clamped to the grid), normal rejection, and Σ power·XYZ(λ)/(π r²) — so the
// result matches the CPU map the CI parity test pins. The storage differs: the full-GPU pipeline keeps
// each cell's photons on a singly-linked list (CellHead → PhotonNext) rather than a sorted range, and
// the grid origin/dims come from the GPU-built GridParams buffer (no CPU readback). Photons carry
// their wavelength as a DeterXYZ row, so XYZ(λ) is DeterXYZ[WlIndex]. Returns 0 when caustics are off.
// §8 SpectralAlbedo: when CausticSpectralAlbedo is set, each photon is weighted by the receiver's
// reflectance at the photon's OWN wavelength (HeroReflectance evaluated at ph.WlIndex) instead of the
// composite multiplying the whole estimate by the hero-wavelength albedo — so a caustic on a red floor
// keeps the floor's spectral tint per bundle. `prim`/`rayDir` describe the receiver for that lookup.
float3 TraceCausticEstimate(float3 pos, float3 normal, PrimitiveInfo prim, float3 rayDir)
{
    if (CausticEnabled == 0u)
        return float3(0.0, 0.0, 0.0);
    if (GridParams[6] <= 0)   // no photons deposited
        return float3(0.0, 0.0, 0.0);

    int minCX = GridParams[0], minCY = GridParams[1], minCZ = GridParams[2];
    int dimX = GridParams[3], dimY = GridParams[4], dimZ = GridParams[5];

    float cellSize = CausticCellSize;
    float radius = CausticRadius;
    float radiusSq = radius * radius;
    int span = max(1, (int)ceil(radius / cellSize));
    int cx = (int)floor(pos.x / cellSize);
    int cy = (int)floor(pos.y / cellSize);
    int cz = (int)floor(pos.z / cellSize);

    float3 sum = float3(0.0, 0.0, 0.0);
    for (int dz = -span; dz <= span; dz++)
    {
        int gz = cz + dz - minCZ;
        if (gz < 0 || gz >= dimZ) continue;
        for (int dy = -span; dy <= span; dy++)
        {
            int gy = cy + dy - minCY;
            if (gy < 0 || gy >= dimY) continue;
            for (int dx = -span; dx <= span; dx++)
            {
                int gx = cx + dx - minCX;
                if (gx < 0 || gx >= dimX) continue;

                int cell = gx + dimX * (gy + dimY * gz);
                int idx = CellHead[cell];
                [loop] for (int guard = 0; idx >= 0 && guard < 1000000; guard++)
                {
                    CausticPhoton ph = PhotonsOut[idx];
                    if (dot(ph.Normal, normal) >= 0.5)
                    {
                        float3 d = ph.Pos - pos;
                        if (dot(d, d) <= radiusSq)
                        {
                            float albedo = (CausticSpectralAlbedo != 0u)
                                ? HeroReflectance(prim, rayDir, pos, ph.WlIndex)
                                : 1.0;
                            sum += DeterXYZ[ph.WlIndex].xyz * (ph.Power * albedo);
                        }
                    }
                    idx = PhotonNext[idx];
                }
            }
        }
    }

    return sum / (PI * radiusSq);
}

// Full per-sample corrected XYZ; also returns direct/indirect components, the
// primary-hit mask, the primary hit point + oriented normal for the G-buffer, and
// (Phase 5) the primary hit's base reflectance luminance for the Albedo view.
float3 ShadeSample(float3 camPos, float3 primaryDir, uint pixelHash, uint sampleIdx,
                   out bool primaryHit, out bool primaryRatHit, out bool primaryBubbleHit,
                   out float3 correctedDirect, out float3 correctedIndirect,
                   out float3 primaryHitPoint, out float3 primaryNormal, out float primaryAlbedo,
                   out uint primaryDynamic)
{
    correctedDirect = float3(0.0, 0.0, 0.0);
    correctedIndirect = float3(0.0, 0.0, 0.0);
    primaryHitPoint = float3(0.0, 0.0, 0.0);
    primaryNormal = float3(0.0, 0.0, 0.0);
    primaryAlbedo = 0.0;
    primaryRatHit = false;
    primaryBubbleHit = false;
    primaryDynamic = 0u;

    uint heroIdx = ((pixelHash % DETERMINISTIC_COUNT) + (sampleIdx % DETERMINISTIC_COUNT)) % DETERMINISTIC_COUNT;

    PrimitiveInfo prim;
    float3 hitPoint;
    primaryHit = TraceClosest(camPos, primaryDir, prim, hitPoint);

    // The rat is a real traced object for the primary ray too (plan §8): test its view-facing
    // billboard, occluded by any nearer scene geometry. A hit is a fullbright sprite terminal
    // written to the G-buffer, so it fogs (FogOut) and reprojects like any primary surface — no
    // screen-space compositing. Reflections keep testing it inside TraceSpecularRadiance.
    float sceneT = primaryHit ? length(hitPoint - camPos) : 1e30;
    float3 ratHitPoint; float ratRefl;
    if (IntersectRat(camPos, primaryDir, sceneT, heroIdx, ratHitPoint, ratRefl))
    {
        primaryHit = true;
        primaryRatHit = true;
        primaryHitPoint = ratHitPoint;
        primaryNormal = -primaryDir;          // billboard faces the camera
        primaryAlbedo = ratRefl;
        float3 ratXyz = DeterXYZ[heroIdx].xyz * ratRefl * DeterministicCorrection;
        correctedDirect = ratXyz;             // fullbright → all "direct" for the debug view
        return ratXyz;
    }

    if (!primaryHit)
    {
        // §O2/§O3: escaped through the open roof → the sky (emissive), else black. DeterXYZ is the
        // white-balanced CIE table, so the flat dome integrates to a neutral blue, not a magenta cast.
        if (SkyEnabled == 0.0)
            return float3(0.0, 0.0, 0.0);
        float3 skyXyz = DeterXYZ[heroIdx].xyz * SkyRadiance(primaryDir, DeterXYZ[heroIdx].w) * DeterministicCorrection;
        correctedDirect = skyXyz;
        return skyXyz;
    }

    // A moving bubble is tagged so its pixels self-reset (like the rat), so a live stream does not
    // need to force whole-frame motion mode — the static scene keeps converging around it.
    primaryBubbleHit = (prim.Surface == SURFACE_BUBBLE);
    // pinball-plan §4.1: a primary hit on a moving part takes the cheap mover tier (capped specular /
    // no spectral indirect / no caustics) and marks the pixel a mover for the MotionSampleCap soft-cap.
    bool dyn = (prim.Dynamic != 0u);
    primaryDynamic = dyn ? 1u : 0u;

    float3 hitNormal = FaceNormal(prim, primaryDir);
    primaryHitPoint = hitPoint;
    primaryNormal = hitNormal;

    // Specular surface — mirror (§1.1), dielectric (§1.2), or bubble (§2.6). Hero-only;
    // accumulation builds the spectrum. G-buffer keeps the specular surface so TAA reprojects
    // on it. Mirrors JobSystem.TraceCore.
    if (prim.Surface == SURFACE_MIRROR || prim.Surface == SURFACE_DIELECTRIC || prim.Surface == SURFACE_BUBBLE)
    {
        primaryAlbedo = HeroReflectance(prim, primaryDir, hitPoint, heroIdx);

        uint specRng = pixelHash + sampleIdx * 2654435761u + 1013904223u;
        // Already CIE-weighted XYZ, with smoke integrated along the reflected segments. A mover caps the
        // chain to one reflection into the converged static scene (§4.4); the loop counts the primary hit
        // as depth 1, so the cap is MOVER_SPECULAR_BOUNCES + 1.
        uint specCap = dyn ? (MOVER_SPECULAR_BOUNCES + 1u) : MAX_SPECULAR_BOUNCES;
        float3 specXyz = TraceSpecularRadiance(camPos, primaryDir, heroIdx, specRng, specCap) * DeterministicCorrection;

        // Soap-bubble firefly clamp (§2.6): the thin-shell reflect/transmit split is high-variance (a
        // rare boosted reflection over a mostly see-through background), so cap each sample's luminance
        // — scaling XYZ so the hue (the iridescent colour) is preserved, only the brightness outliers
        // are tamed. The running mean still integrates the film colour over the wavelength cycle.
        if (prim.Surface == SURFACE_BUBBLE)
        {
            float lum = max(specXyz.y, 1e-4);
            if (lum > BUBBLE_FIREFLY_CLAMP)
                specXyz *= BUBBLE_FIREFLY_CLAMP / lum;
        }
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
        float lightP;
        uint chosenLight = SelectLight(rngLight, hitPoint, shadeNormal, lightP);
        // §C1/§C2: sample the shadow ray toward the chosen light — geom is cos/d² (point) or cos (sun).
        float3 lightDir; float chosenDist;
        float geom = SampleLightShadowRay(chosenLight, hitPoint, shadeNormal, rngLight, lightDir, chosenDist);

        if (geom > 0.0)
        {
            float vis = 1.0;
            if (LightingMode == 2u) // NEE
            {
                float3 shadowOrigin = hitPoint + hitNormal * 1e-3;
                vis = TraceTransmittance(shadowOrigin, lightDir, chosenDist - 2e-3, heroIdx);
            }
            if (vis > 0.0)
                directTerm += vis * (geom * LightIntensity / max(lightP, 1e-9)); // §4: geometry × visibility only
        }

        // §4: base reflectance × the chosen light's per-companion emission spectrum (§O9 blackbody × §6
        // luma norm), so a coloured light's tint correlates with each wavelength's reflectance rather than
        // being factored out at the hero λ and smeared over the companion mean. Equals baseXyz for a
        // flat-white light (temp 0), so temp-0 scenes (all parity self-tests) stay bit-identical.
        float3 directBaseXyz = DirectBaseXyz(prim, primaryDir, hitPoint, pixelHash, sampleIdx, LightColors[chosenLight].w);

        xyz = baseXyz * ambientTerm + directBaseXyz * directTerm;

        // Cheap mover tier (§4.1): a moving diffuse part takes direct + ambient only — skip the spectral
        // one-bounce indirect, the sky-dome bounce and (below) the caustic gather. Static (!dyn) unchanged.
        if (!dyn)
        {
        uint rng = pixelHash + sampleIdx * RNG_MUL + RNG_ADD;
        rng = rng * RNG_MUL + RNG_ADD;
        float r1 = (rng >> 16) / 65536.0;
        rng = rng * RNG_MUL + RNG_ADD;
        float r2 = (rng >> 16) / 65536.0;

        float3 sampleDir = CosineHemisphere(hitNormal, r1, r2);
        float3 secOrigin = hitPoint + hitNormal * 1e-3;

        PrimitiveInfo secPrim;
        float3 secHitPoint;
        if (TraceClosest(secOrigin, sampleDir, secPrim, secHitPoint))
        {
            float3 secHitNormal = FaceNormal(secPrim, sampleDir);
            // Finding §1: scalar hero-λ throughput; CIE applied once at the primary surface (baseXyz).
            // HeroReflectance is the decal-aware scalar refl, i.e. IndirectBaseXyz / DeterXYZ(hero).
            float secReflHero = HeroReflectance(secPrim, sampleDir, secHitPoint, heroIdx);

            float secDirectTerm = 0.0;
            rng = rng * RNG_MUL + RNG_ADD;
            uint lightIdx2 = rng % NumLights;
            secDirectTerm += UniformLightTerm(lightIdx2, secHitPoint, secHitNormal, heroIdx, rng)
                * IndirectFogShadowFactor(secHitPoint, lightIdx2); // §9

            float secIncoming = secReflHero * (AmbientLevel + secDirectTerm);

            float secBounce2Plus = 0.0;
            rng = rng * RNG_MUL + RNG_ADD;
            float r3 = (rng >> 16) / 65536.0;
            rng = rng * RNG_MUL + RNG_ADD;
            float r4 = (rng >> 16) / 65536.0;

            float3 tertDir = CosineHemisphere(secHitNormal, r3, r4);
            float3 tertOrigin = secHitPoint + secHitNormal * 1e-3;

            PrimitiveInfo tertPrim;
            float3 tertHitPoint;
            if (TraceClosest(tertOrigin, tertDir, tertPrim, tertHitPoint))
            {
                float3 tertHitNormal = FaceNormal(tertPrim, tertDir);
                float tertReflHero = HeroReflectance(tertPrim, tertDir, tertHitPoint, heroIdx);

                float tertDirectTerm = 0.0;
                rng = rng * RNG_MUL + RNG_ADD;
                uint lightIdx3 = rng % NumLights;
                tertDirectTerm += UniformLightTerm(lightIdx3, tertHitPoint, tertHitNormal, heroIdx, rng)
                    * IndirectFogShadowFactor(tertHitPoint, lightIdx3); // §9

                float tertIncoming = tertReflHero * (AmbientLevel + tertDirectTerm);
                secBounce2Plus = secReflHero * tertIncoming;
            }
            else if (SkyEnabled != 0.0)
            {
                // §O7: the second bounce escaped to the open sky — skylight (scalar spectral radiance at
                // the hero wavelength) is its incoming radiance; the CIE weight rides on baseXyz (§1).
                // DomeRadiance omits the sun disk (finding §5) — a bounce's sun is NEE's job, not the sky's.
                secBounce2Plus = secReflHero * SkyDomeRadiance(tertDir, DeterXYZ[heroIdx].w);
            }

            bounce0 = directBaseXyz * directTerm;
            float3 localBounce1 = baseXyz * secIncoming;
            float3 localBounce2Plus = baseXyz * secBounce2Plus;
            indirect = localBounce1 + localBounce2Plus;
            xyz += indirect;
        }
        else if (SkyEnabled != 0.0)
        {
            // §O7: the diffuse bounce escaped to the open sky → the sky lights this surface (ambient blue
            // fill on outdoor cells; daylight bleeding into a corridor just inside a boundary opening).
            // One-bounce skylight; scalar spectral radiance at the hero wavelength, CIE on baseXyz (§1).
            // DomeRadiance omits the sun disk (finding §5) — the sun is added by NEE, not this bounce.
            bounce0 = directBaseXyz * directTerm;
            indirect = baseXyz * SkyDomeRadiance(sampleDir, DeterXYZ[heroIdx].w);
            xyz += indirect;
        }
        } // end if(!dyn)
        else
        {
            // Mover: book the direct term as bounce-0; no indirect (mirrors TraceCore's mover branch).
            bounce0 = directBaseXyz * directTerm;
        }
    }

    // Caustics (§B4): add the forward photon map's density estimate at this diffuse receiver — the
    // light→specular→diffuse paths NEE cannot connect. Scaled by the hero albedo/π and strength, added
    // raw (like `indirect`) so the shared correction below and the resolve's fog composite treat it
    // identically. Specular hits already returned above; CausticEnabled == 0 → early-out → byte-identical.
    if (CausticEnabled != 0u && !dyn) // movers skip the caustic gather (§4.1)
    {
        // §8: with spectral albedo the per-photon receiver reflectance is folded inside the gather,
        // so the composite multiplier drops the hero albedo (else it would be applied twice).
        float causticAlbedo = (CausticSpectralAlbedo != 0u)
            ? 1.0
            : HeroReflectance(prim, primaryDir, hitPoint, heroIdx);
        float3 caustic = TraceCausticEstimate(hitPoint, hitNormal, prim, primaryDir) * (causticAlbedo * CausticStrength * (1.0 / PI));
        xyz += caustic;
        indirect += caustic;
    }

    // Emissive surface (§5.3): add its own emitted radiance at the hero wavelength, folded into the total
    // before the deterministic correction so it scales like the reflectance terms. A specular primary hit
    // already returned above; a non-emitting surface adds 0, so the no-emission path is byte-identical.
    // Mirrors the primary-hit emission block in Phase2Reference.ShadeSample / JobSystem.TraceCore.
    xyz += DeterXYZ[heroIdx].xyz * HeroEmission(prim, primaryDir, hitPoint, heroIdx);

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

// ── §4 Lens ───────────────────────────────────────────────────────────
// A line-for-line port of RayTracer.Core/Pipeline/LensSampler.cs (the CI-testable contract; CI has no GPU).
// Keep these in lockstep with that file — working convention 1.

// Brown–Conrady radial distortion: r' = r·(1 + k1·r² + k2·r⁴). Negative k1 = barrel, positive = pincushion.
void LensDistort(inout float px, inout float py, float k1, float k2)
{
    float r2 = px * px + py * py;
    float scale = 1.0 + k1 * r2 + k2 * r2 * r2;
    px *= scale;
    py *= scale;
}

// Samples the aperture: a perfect disc below 3 blades, otherwise the inscribed regular polygon via a
// triangle fan. Always draws exactly two randoms, so the stream length is independent of blade count.
void LensSampleAperture(float radius, uint blades, float bladeRotation, inout uint rng,
                        out float lx, out float ly)
{
    rng = rng * RNG_MUL + RNG_ADD; float u1 = float(rng) / 4294967296.0;
    rng = rng * RNG_MUL + RNG_ADD; float u2 = float(rng) / 4294967296.0;

    if (blades < 3u)
    {
        float r = radius * sqrt(u1);
        float theta = 2.0 * PI * u2;
        lx = r * cos(theta);
        ly = r * sin(theta);
        return;
    }

    float fan = u1 * float(blades);
    uint tri = min(uint(fan), blades - 1u);
    float a = fan - float(tri);
    float b = u2;
    if (a + b > 1.0) { a = 1.0 - a; b = 1.0 - b; }

    float step = 2.0 * PI / float(blades);
    float t0 = bladeRotation + float(tri) * step;
    float t1 = t0 + step;
    float v0x = radius * cos(t0), v0y = radius * sin(t0);
    float v1x = radius * cos(t1), v1y = radius * sin(t1);
    lx = a * v0x + b * v1x;
    ly = a * v0y + b * v1y;
}

// Squashes an off-axis aperture sample along the radial direction into the barrel's "cat's eye" (§4.4).
// Deterministic (no rejection), so the RNG stream stays pixel-independent and portable.
void LensApplyCatEye(inout float lx, inout float ly, float fieldX, float fieldY, float fieldRadius)
{
    float fieldLen = sqrt(fieldX * fieldX + fieldY * fieldY);
    if (fieldLen <= 1e-6 || fieldRadius <= 1e-6)
        return;

    float ux = fieldX / fieldLen;
    float uy = fieldY / fieldLen;
    float squash = max(0.25, 1.0 - 0.75 * min(fieldRadius, 1.0));

    float radial = lx * ux + ly * uy;
    float tanX = lx - radial * ux;
    float tanY = ly - radial * uy;
    radial *= squash;
    lx = tanX + radial * ux;
    ly = tanY + radial * uy;
}

// cos⁴θ natural falloff raised to `strength`: 0 = off (exactly 1), 1 = physical, >1 = exaggerated.
float LensVignetteFactor(float px, float py, float strength)
{
    if (strength <= 0.0)
        return 1.0;
    float z2 = ImgPlaneZ * ImgPlaneZ;
    float cos2 = z2 / (px * px + py * py + z2);
    float cos4 = cos2 * cos2;
    return (abs(strength - 1.0) < 1e-6) ? cos4 : pow(cos4, strength);
}

// The primary ray through the lens. Mirrors LensSampler.GenerateLocalRay + the reference's world-space
// composition. LensApertureRadius 0 with no distortion ⇒ the exact historical pinhole, no randoms drawn.
void GenerateLensRay(float px, float py, uint pixelHash, uint sampleIdx,
                     out float3 origin, out float3 dir, out float vignette)
{
    float dpx = px;
    float dpy = py;
    if (LensDistortK1 != 0.0 || LensDistortK2 != 0.0)
        LensDistort(dpx, dpy, LensDistortK1, LensDistortK2);

    float3 localDir = float3(dpx, dpy, ImgPlaneZ);
    vignette = LensVignetteFactor(px, py, LensVignetteStrength);

    if (LensApertureRadius <= 0.0)
    {
        origin = CamPos;
        dir = normalize(RotateByQuaternion(CamRot, localDir));
        return;
    }

    // The lens's own RNG stream (LensSampler.SeedFor), so the aperture draw cannot perturb the
    // wavelength/specular/shadow streams.
    uint rng = pixelHash + sampleIdx * 2246822519u + 3266489917u;
    float lx, ly;
    LensSampleAperture(LensApertureRadius, LensBlades, LensBladeRotation, rng, lx, ly);

    if (LensCatEye != 0u)
    {
        float fieldRadius = sqrt(dpx * dpx + dpy * dpy) / max(abs(ImgPlaneZ), 1e-6);
        LensApplyCatEye(lx, ly, dpx, dpy, fieldRadius);
    }

    // Thin lens: every point of the aperture images the same focus-plane point, which is what makes that
    // plane sharp and everything else not.
    float ft = LensFocusDistance / max(abs(ImgPlaneZ), 1e-6);
    float3 focusPoint = localDir * ft;
    float3 localOrigin = float3(lx, ly, 0.0);

    origin = CamPos + RotateByQuaternion(CamRot, localOrigin);
    dir = normalize(RotateByQuaternion(CamRot, focusPoint - localOrigin));
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

    uint pixelHash = Hash2D(x, y);

    // §4.1: generate the primary ray through the lens (a port of Phase1Reference.PrimaryRayThroughLens).
    // With LensApertureRadius 0 and no distortion this is exactly the historical
    // `normalize(RotateByQuaternion(CamRot, float3(px, py, ImgPlaneZ)))` from CamPos, and draws no randoms.
    float3 rayOrigin;
    float3 dir;
    float vignette;
    GenerateLensRay(px, py, pixelHash, sampleIdx, rayOrigin, dir, vignette);

    bool hit, ratHit, bubbleHit;
    float3 correctedDirect, correctedIndirect, gHitPoint, gNormal;
    float gAlbedo;
    uint gDynamic;
    float3 corrected = ShadeSample(rayOrigin, dir, pixelHash, sampleIdx, hit, ratHit, bubbleHit,
                                   correctedDirect, correctedIndirect, gHitPoint, gNormal, gAlbedo, gDynamic);

    // §4.1: the lens's natural falloff toward the corners. Exactly 1.0 when the lens does not vignette.
    if (LensVignetteStrength > 0.0)
    {
        corrected *= vignette;
        correctedDirect *= vignette;
        correctedIndirect *= vignette;
    }

    // Ceiling biome-category overlay (debug). Oriented normal points down (-Y)
    // on a ceiling hit; gated off (BiomeIndicator=0) for the parity self-test.
    if (BiomeIndicator != 0u && hit && gNormal.y < -0.5)
        corrected = ApplyBiomeIndicator(corrected, gHitPoint);

    // Per-sample firefly clamp; also record how much was clamped (mirrors the
    // clampDelta L1 sum in PathTracer.cs) for the Phase 5.2 clamp heatmap + stat.
    // Skipped on a primary miss: that sample is the emissive sky (§O2/§O3) — smooth and low-variance,
    // not a firefly — and a per-channel clamp would chop its high-luminance (green) wavelengths first,
    // biasing the blue dome toward pink ("evening sky") at low clamp values. Clamping it buys no noise
    // reduction, only colour bias, so the sky renders true at any clamp (including a roofed scene's black
    // miss, where clamp is a no-op anyway — goldens unchanged).
    float clampDelta = 0.0;
    if (SampleClamp > 0.0 && hit)
    {
        float3 unclamped = corrected;
        corrected = clamp(corrected, float3(0.0, 0.0, 0.0), float3(SampleClamp, SampleClamp, SampleClamp));
        clampDelta = abs(unclamped.x - corrected.x) + abs(unclamped.y - corrected.y) + abs(unclamped.z - corrected.z);
    }

    // Hit/miss mask: 1u = any surface hit (static geometry AND the moving rat/bubbles alike), 0u = miss.
    // The rat and bubbles are no longer given a hard per-pixel reset (which made them 1-sample noisy);
    // instead a mover's pixels are soft-capped at MotionSampleCap — the same budget the whole frame uses
    // while the camera moves. So a mover accumulates ~cap samples: smooth (no fireflies), with a short
    // bounded temporal smear (and, once it drifts on, the freed pixel reconverges from ~cap, so any
    // wall ghost fades in a fraction of a second). The animations are slowed to keep that smear legible.
    uint hitMask = hit ? 1u : 0u;
    // Only the rat genuinely animates each frame, so cap its pixels to bound its temporal smear. Bubbles
    // are FROZEN (snapshotted at build/regenerate, §2.6) — capping them as "movers" pinned their spectral
    // reflect/transmit estimate at ~MotionSampleCap samples, so a hero bubble never converged no matter how
    // long it baked. Treat frozen bubbles as static geometry so they accumulate the full sample budget;
    // during camera motion SoftResetFlag still caps every pixel (bubble included), so no specular ghosting.
    // A moving part (the chrome ball / a flipper) soft-caps at MotionSampleCap like the rat, so the static
    // scene keeps converging around it and its bounded temporal smear stays short (§4.1/§4.3).
    bool isMover = ratHit || (gDynamic != 0u);
    bool restart = reset || (LastHit[ix] != hitMask);
    uint prevCount = restart ? 0u : SampleCount[ix];
    if ((SoftResetFlag != 0u || isMover) && !restart)
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
    LastHit[ix] = hitMask;

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
    // the resolve pass onto the converged surface. Fog fills the volume, so a ray that
    // misses (escapes through the open roof to the sky, §O2/§O3) still crosses the fog and
    // must in-scatter + attenuate the sky behind it — otherwise the fog reads as a wash
    // "painted" only where rays land. On a miss there is no surface, so march a full fog
    // column toward a far point along the ray (IntegrateVolumetric caps the march at
    // VolMaxMarchDistance, so an endpoint just past it covers the whole column). Roofed
    // scenes never miss, so their fog — and the goldens — are unchanged.
    // §4.1: marched from the ray's actual origin — the sampled aperture point, not the camera centre.
    // Identical to CamPos on the pinhole path.
    float3 fogEnd = hit ? gHitPoint : rayOrigin + dir * (max(VolMaxMarchDistance, 0.0) + 1.0);
    float fogT = 1.0;
    float3 fogInscatter = float3(0.0, 0.0, 0.0);
    IntegrateVolumetric(rayOrigin, fogEnd, dir, VolTime, fogT, fogInscatter);
    FogOut[ix] = float4(fogInscatter * DeterministicCorrection, fogT);
}

// ── §B4: GPU forward photon trace ─────────────────────────────────────
// Closest hit for the photon pass: like TraceClosest but with no build-in reveal (the maze is always
// complete during a caustic build), so opaque triangles keep the hardware fast path and only spheres
// run the candidate loop. Decouples the pass from the render's RevealHeight.
bool TraceClosestNoReveal(float3 origin, float3 dir, out PrimitiveInfo prim, out float3 hitPoint)
{
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = 1e-4;
    ray.TMax = 1e4;

    RayQuery<RAY_FLAG_NONE> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, ray);
    while (q.Proceed())
    {
        if (q.CandidateType() == CANDIDATE_PROCEDURAL_PRIMITIVE)
        {
            float t;
            if (IntersectSphere(Spheres[q.CandidatePrimitiveIndex()], origin, dir, q.RayTMin(), t))
                q.CommitProceduralPrimitiveHit(t);
        }
    }

    uint status = q.CommittedStatus();
    if (status == COMMITTED_TRIANGLE_HIT)
    {
        prim = Primitives[q.CommittedPrimitiveIndex() >> 1];
        hitPoint = origin + dir * q.CommittedRayT();
        return true;
    }
    if (status == COMMITTED_PROCEDURAL_PRIMITIVE_HIT)
    {
        hitPoint = origin + dir * q.CommittedRayT();
        prim = SphereToPrim(Spheres[q.CommittedPrimitiveIndex()], hitPoint);
        return true;
    }
    prim = (PrimitiveInfo)0;
    hitPoint = float3(0.0, 0.0, 0.0);
    return false;
}

// The GPU twin of PhotonTracer.TracePhoton (shadows-and-caustics-plan §B4): one thread emits one
// photon toward its caster's bounds and forward-traces it through the specular surfaces (mirror /
// bubble thin-shell / dielectric with dispersion + Beer–Lambert), depositing on the first diffuse
// receiver reached after ≥1 specular bounce. The refraction / Fresnel / absorption maths reuse the
// same helpers as the eye-side chain — only the direction of travel differs. Per-thread RNG is
// independent of the CPU's serial stream (accumulation / TAA average the stochastic noise); the map
// stays deterministic per build because the seed is fixed.
[numthreads(64, 1, 1)]
void CausticCS(uint3 tid : SV_DispatchThreadID)
{
    uint gid = tid.x;

    // Which caster (emit job) does this global photon index belong to? Few jobs → linear scan.
    EmitJob job = (EmitJob)0;
    bool found = false;
    [loop] for (uint j = 0; j < EmitJobCount; j++)
    {
        EmitJob e = EmitJobs[j];
        if (gid >= e.PhotonBase && gid < e.PhotonBase + e.PhotonCount) { job = e; found = true; break; }
    }
    if (!found)
        return;

    uint photonIndex = gid - job.PhotonBase;

    // Per-thread deterministic RNG seed (hash of the job seed and global index), then the same LCG
    // the CPU tracer uses for the emission jitter and Russian-roulette draws.
    uint rng = job.Seed ^ (gid * 2654435761u);
    rng ^= rng >> 15; rng *= 2246822519u; rng ^= rng >> 13; rng *= 3266489917u; rng ^= rng >> 16;

    rng = rng * 747796405u + 2891336453u; float rx = rng / 4294967296.0;
    rng = rng * 747796405u + 2891336453u; float ry = rng / 4294967296.0;
    rng = rng * 747796405u + 2891336453u; float rz = rng / 4294967296.0;

    float3 bmin = float3(job.BMinX, job.BMinY, job.BMinZ);
    float3 bmax = float3(job.BMaxX, job.BMaxY, job.BMaxZ);
    float3 spot = bmin + (bmax - bmin) * float3(rx, ry, rz);

    float3 origin, dir;
    if (job.Mode == 1u)
    {
        // Collimated beam: parallel rays (fixed Dir) over the cross-section box [BMin,BMax] — a real
        // beam, so a prism disperses it into a clean wavelength-ordered band (no point-source fan).
        origin = spot;
        dir = normalize(float3(job.DirX, job.DirY, job.DirZ));
    }
    else
    {
        // Point emission: from the light toward a random point in the caster bounds (diverging cone).
        float3 lightPos = float3(job.LX, job.LY, job.LZ);
        float3 d = spot - lightPos;
        float len = length(d);
        if (len < 1e-6)
            return;
        origin = lightPos;
        dir = d / len;
    }

    uint row = photonIndex % DETERMINISTIC_COUNT;   // matches WavelengthLookup.GetDeterministicWavelength(p)
    float wavelength = DeterXYZ[row].w;
    // §8: physical photon power — intensity × the caster's subtended solid angle, split across the photons
    // — so a closer/brighter light changes the caustic physically instead of only through Strength. Off
    // (EmitPhysicalEnergy == 0) keeps the historical flat 1/N. Point emission only (a collimated beam has
    // no point source); the white-light equivalent of the CPU PhotonTracer (these jobs carry no temperature).
    float power = 1.0 / max(job.PhotonCount, 1u);
    if (EmitPhysicalEnergy != 0u && job.Mode != 1u)
    {
        float3 center = (bmin + bmax) * 0.5;
        float r = 0.5 * length(bmax - bmin);
        float d = max(length(center - float3(job.LX, job.LY, job.LZ)), r + 1e-3);
        float omega = min(3.14159265 * r * r / (d * d), 6.28318530); // π r²/d², capped at 2π
        power *= LightIntensity * omega;
    }

    bool inGlass = false;
    float mediumSigma = 0.0;
    uint specularBounces = 0u;

    [loop] for (uint depth = 0u; depth < EmitMaxBounces; depth++)
    {
        PrimitiveInfo prim; float3 hitPoint;
        if (!TraceClosestNoReveal(origin, dir, prim, hitPoint))
            return; // escaped the scene

        // Beer–Lambert absorption for the segment just travelled inside a coloured medium.
        if (mediumSigma > 0.0)
            power *= exp(-mediumSigma * distance(origin, hitPoint));

        float3 n = FaceNormal(prim, dir);

        if (prim.Surface == SURFACE_MIRROR)
        {
            power *= HeroReflectance(prim, dir, hitPoint, row);
            dir = reflect(dir, n);
            origin = hitPoint + n * 1e-3;
            specularBounces++;
            continue;
        }

        if (prim.Surface == SURFACE_BUBBLE)
        {
            // Thin-shell reflect probability = the specular branch's rReflect (boosted Fresnel ×
            // base reflectance × thin-film tint, gravity-graded thickness via the sphere normal Y).
            float cosv = abs(dot(normalize(dir), n));
            float grav = lerp(1.4, 0.6, (prim.NY + 1.0) * 0.5);
            float d = prim.CauchyB * grav;
            float baseR = MaterialReflectance[prim.MatPrimary * DETERMINISTIC_COUNT + row];
            float film = ThinFilmReflectance(cosv, wavelength, d, prim.Ior);
            float reflectProb = saturate(EmitBubbleBoost * FresnelDielectric(cosv, 1.0, prim.Ior)) * baseR * film; // §12
            rng = rng * 747796405u + 2891336453u;
            if (rng / 4294967296.0 < reflectProb)
            {
                dir = reflect(dir, n);
                origin = hitPoint + n * 1e-3;
            }
            else
            {
                origin = hitPoint + dir * 1e-3; // transmit straight through the thin shell
            }
            specularBounces++;
            continue;
        }

        if (prim.Surface == SURFACE_DIELECTRIC)
        {
            float um = wavelength * 1e-3;
            float nIor = prim.Ior + prim.CauchyB / (um * um);
            float iorFrom = inGlass ? nIor : 1.0;
            float iorTo   = inGlass ? 1.0 : nIor;
            float cosv = abs(dot(normalize(dir), n));
            float fresnel = FresnelDielectric(cosv, iorFrom, iorTo);
            float3 refr;
            bool transmits = Refract(dir, n, iorFrom, iorTo, refr);
            rng = rng * 747796405u + 2891336453u;
            float u = rng / 4294967296.0;
            if (!transmits || u < fresnel)
            {
                dir = reflect(dir, n);
                origin = hitPoint + n * 1e-3;
            }
            else
            {
                dir = refr;
                origin = hitPoint - n * 1e-3;
                inGlass = !inGlass;
                mediumSigma = inGlass ? AbsorptionAt(prim.P0, prim.P1, prim.P2, wavelength) : 0.0;
            }
            specularBounces++;
            continue;
        }

        // Diffuse / opaque receiver: deposit only after ≥1 specular bounce (else it is ordinary
        // direct light NEE already accounts for). Append via an atomic bump on the shared counter.
        if (specularBounces > 0u)
        {
            uint idx;
            InterlockedAdd(PhotonCounter[0], 1u, idx);
            if (idx < PhotonCapacity)
            {
                CausticPhoton ph;
                ph.Pos = hitPoint;
                ph.Normal = n;
                ph.Power = power;
                ph.WlIndex = row;
                PhotonsOut[idx] = ph;

                // Grow the photon-cell bounding box (atomic min/max) so CausticParamsCS can size the
                // grid — the full-GPU replacement for CPU BuildGrid's bbox scan. Same cell key as the
                // link pass + gather: floor(pos / cellSize).
                int ccx = (int)floor(hitPoint.x / EmitCellSize);
                int ccy = (int)floor(hitPoint.y / EmitCellSize);
                int ccz = (int)floor(hitPoint.z / EmitCellSize);
                InterlockedMin(Bbox[0], ccx); InterlockedMin(Bbox[1], ccy); InterlockedMin(Bbox[2], ccz);
                InterlockedMax(Bbox[3], ccx); InterlockedMax(Bbox[4], ccy); InterlockedMax(Bbox[5], ccz);
            }
        }
        return;
    }
}

// §B4 full-GPU binning, step 2: turn the atomic photon-cell bounding box (Bbox) + deposited count into
// the grid origin/dims (GridParams). One thread. The CPU twin is the head of PhotonMap.BuildGrid.
[numthreads(1, 1, 1)]
void CausticParamsCS(uint3 tid : SV_DispatchThreadID)
{
    int count = min((int)PhotonCounter[0], (int)PhotonCapacity);
    GridParams[6] = count;
    if (count <= 0)
    {
        GridParams[0] = 0; GridParams[1] = 0; GridParams[2] = 0;
        GridParams[3] = 0; GridParams[4] = 0; GridParams[5] = 0;
        GridParams[7] = 0;
        return;
    }

    int minX = Bbox[0], minY = Bbox[1], minZ = Bbox[2];
    int dimX = Bbox[3] - minX + 1, dimY = Bbox[4] - minY + 1, dimZ = Bbox[5] - minZ + 1;
    int cellCount = dimX * dimY * dimZ;
    // If the grid would exceed the cell-head capacity, disable the map rather than index out of range
    // (should not happen with a sane capacity; a loud, safe fallback).
    if (cellCount <= 0 || cellCount > (int)CellHeadCapacity)
    {
        GridParams[6] = 0;
        return;
    }
    GridParams[0] = minX; GridParams[1] = minY; GridParams[2] = minZ;
    GridParams[3] = dimX; GridParams[4] = dimY; GridParams[5] = dimZ;
    GridParams[7] = cellCount;
}

// §B4 full-GPU binning, step 3: reset every cell's linked-list head to empty (-1). Dispatched over the
// full fixed CellHead capacity (independent of the dynamic grid), so it needs no prior pass.
[numthreads(64, 1, 1)]
void CausticClearHeadCS(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x < CellHeadCapacity)
        CellHead[tid.x] = -1;
}

// §B4 full-GPU binning, step 4: thread each deposited photon onto its cell's singly-linked list via an
// atomic exchange on the head (order-independent: the gather sums the whole list). The CPU twin is the
// counting-sort scatter in PhotonMap.BuildGrid; the linked list avoids a GPU prefix-sum entirely.
[numthreads(64, 1, 1)]
void CausticLinkCS(uint3 tid : SV_DispatchThreadID)
{
    int i = (int)tid.x;
    if (i >= GridParams[6])
        return;

    CausticPhoton ph = PhotonsOut[i];
    int minX = GridParams[0], minY = GridParams[1], minZ = GridParams[2];
    int dimX = GridParams[3], dimY = GridParams[4], dimZ = GridParams[5];

    int gx = (int)floor(ph.Pos.x / EmitCellSize) - minX;
    int gy = (int)floor(ph.Pos.y / EmitCellSize) - minY;
    int gz = (int)floor(ph.Pos.z / EmitCellSize) - minZ;
    if (gx < 0 || gx >= dimX || gy < 0 || gy >= dimY || gz < 0 || gz >= dimZ)
    {
        PhotonNext[i] = -1;
        return;
    }

    int cell = gx + dimX * (gy + dimY * gz);
    int old;
    InterlockedExchange(CellHead[cell], i, old);
    PhotonNext[i] = old;
}
