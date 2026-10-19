// Vista offline integrator v0 (vista-plan §5.1, §10 M0) — DXR 1.1 inline ray tracing, Shader Model 6.5.
//
// A spectral path tracer for stills: multi-bounce Lambertian terrain, next-event estimation on the sun,
// the engine's single-scattering Rayleigh sky dome (a port of Sky.DomeRadiance), Russian roulette, and
// hero-wavelength sampling over the engine's 50-wavelength deterministic cycle (a hero plus three companions
// that share its path, so the throughput is a float4). The C# reference of the light model is
// Vista.Rendering.SkyLight / IntegratorTables (pinned by Vista.Tests).
//
// Determinism contract (BatchPlanner): every random number of a pixel-sample comes from (pixel, sample
// index) alone — never from the dispatch — and a pixel's samples are always added in sample-index order,
// starting from zero at sample 0. So the image is the same however the render is split into dispatches.
//
// One thread per pixel of a row band; it traces samples [FirstSample, FirstSample + SampleCount).

#define HERO_COUNT        50u
#define COMPANIONS        4u
#define COMPANION_STRIDE  12u   // HERO_COUNT / COMPANIONS
#define PI                3.14159265358979
#define INV_PI            0.318309886183791
#define RR_START_BOUNCE   3u
#define T_MAX             1.0e7

#define AOV_ALBEDO        1u
#define AOV_NORMAL        2u
#define AOV_FIRST_HIT     4u
#define AOV_VARIANCE      8u

// ── Bindings (all root descriptors) ───────────────────────────────────────────────────────────────

RaytracingAccelerationStructure Scene     : register(t0);
StructuredBuffer<float3> Positions        : register(t1); // TerrainMesh.Positions
StructuredBuffer<float3> Normals          : register(t2); // TerrainMesh.Normals (unit, smooth)
StructuredBuffer<uint>   Indices          : register(t3); // TerrainMesh.Indices, 3 per triangle
StructuredBuffer<float4> Cie              : register(t4); // xyz = white-balanced CIE, w = wavelength nm
StructuredBuffer<float4> LightTable       : register(t5); // x solar shape, y (550/λ)⁴, z sun irradiance, w sun disk radiance
StructuredBuffer<float>  Reflectance      : register(t6); // [material * HERO_COUNT + entry]
StructuredBuffer<float4> MaterialAlbedo   : register(t7); // linear-sRGB albedo per material

RWStructuredBuffer<float4> XyzSum         : register(u0); // Σ XYZ over samples
RWStructuredBuffer<float2> LumaStats      : register(u1); // Welford (mean, M2) of the samples' luminance
RWStructuredBuffer<float4> AlbedoSum      : register(u2); // Σ first-hit albedo
RWStructuredBuffer<float4> NormalSum      : register(u3); // Σ first-hit normal
RWStructuredBuffer<float2> FirstHit       : register(u4); // sample 0: (depth, material id)
RWStructuredBuffer<uint>   RayCounter     : register(u5); // [0] low, [1] high word of a 64-bit count

cbuffer Constants : register(b0)
{
    float3 CamPos;      float TanHalfFovY;
    float3 CamRight;    float Aspect;
    float3 CamUp;       float DeterministicCorrection;
    float3 CamForward;  float SkyStrength;
    float3 ToSun;       float SunOneMinusCosMax;   // 1 − cos(sun angular radius), computed in double on the CPU
    float  RayleighDepth; uint SunUp;      uint MaxBounces; uint AovMask;
    uint   Width;         uint Height;     uint RowStart;   uint RowCount;
    uint   FirstSample;   uint SampleCount; uint TerrainMaterial; uint _pad0;
};

// ── Random numbers: a stream per (pixel, sample index) ────────────────────────────────────────────

uint PcgHash(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float NextFloat(inout uint s)
{
    s = PcgHash(s);
    return float(s >> 8) * (1.0 / 16777216.0); // [0, 1)
}

// ── Geometry helpers ───────────────────────────────────────────────────────────────────────────────

// Orthonormal basis around a unit vector (Duff et al. 2017).
void Basis(float3 n, out float3 t, out float3 b)
{
    float s = n.z >= 0.0 ? 1.0 : -1.0;
    float a = -1.0 / (s + n.z);
    float c = n.x * n.y * a;
    t = float3(1.0 + s * n.x * n.x * a, s * c, -s * n.x);
    b = float3(c, s + n.y * n.y * a, -n.y);
}

float3 CosineHemisphere(float3 n, float u1, float u2)
{
    float3 t, b;
    Basis(n, t, b);
    float r = sqrt(u1);
    float phi = 2.0 * PI * u2;
    return normalize(t * (r * cos(phi)) + b * (r * sin(phi)) + n * sqrt(max(0.0, 1.0 - u1)));
}

// Uniform direction in the cone of half-angle θ around `axis`, given 1 − cos θ.
float3 SampleCone(float3 axis, float oneMinusCosMax, float u1, float u2)
{
    float3 t, b;
    Basis(axis, t, b);
    float oneMinusCos = u1 * oneMinusCosMax;
    float cosT = 1.0 - oneMinusCos;
    float sinT = sqrt(max(0.0, oneMinusCos * (2.0 - oneMinusCos)));
    float phi = 2.0 * PI * u2;
    return normalize(t * (sinT * cos(phi)) + b * (sinT * sin(phi)) + axis * cosT);
}

// Distance to push a new ray's origin off the surface: float32 spacing grows with the coordinate size,
// and Vista worlds are kilometres across (≈ 3 cm at 8 km).
float OriginOffset(float3 p)
{
    float m = max(max(abs(p.x), abs(p.y)), abs(p.z));
    return 1.0e-3 + 4.0e-6 * m;
}

struct Hit
{
    float  T;
    float3 Position;
    float3 GeometricNormal; // faces the incoming ray
    float3 ShadingNormal;   // interpolated vertex normal, same hemisphere as GeometricNormal
};

bool TraceClosest(float3 origin, float3 dir, out Hit hit)
{
    hit = (Hit)0;
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = 0.0;
    ray.TMax = T_MAX;

    RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, ray);
    q.Proceed();
    if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT)
        return false;

    uint tri = q.CommittedPrimitiveIndex();
    float2 bary = q.CommittedTriangleBarycentrics();
    uint i0 = Indices[tri * 3u];
    uint i1 = Indices[tri * 3u + 1u];
    uint i2 = Indices[tri * 3u + 2u];
    float3 p0 = Positions[i0];
    float3 e1 = Positions[i1] - p0;
    float3 e2 = Positions[i2] - p0;

    hit.T = q.CommittedRayT();
    hit.Position = p0 + bary.x * e1 + bary.y * e2; // on the triangle, not origin + t·dir
    // TerrainMesh winding: the component cross (v1 − v0) × (v2 − v0) points up (front faces face the sky).
    float3 ng = normalize(cross(e1, e2));
    float3 ns = normalize(Normals[i0] * (1.0 - bary.x - bary.y) + Normals[i1] * bary.x + Normals[i2] * bary.y);
    if (dot(ng, dir) > 0.0) // two-sided: face the incoming ray
    {
        ng = -ng;
        ns = -ns;
    }
    if (dot(ns, ng) <= 0.0)
        ns = ng;
    hit.GeometricNormal = ng;
    hit.ShadingNormal = ns;
    return true;
}

bool Occluded(float3 origin, float3 dir)
{
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = 0.0;
    ray.TMax = T_MAX;

    RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, ray);
    q.Proceed();
    return q.CommittedStatus() != COMMITTED_NOTHING;
}

// ── Light (C# reference: SkyLight) ────────────────────────────────────────────────────────────────

// The Rayleigh dome without the sun disk, per companion wavelength (Sky.DomeRadiance).
float4 DomeRadiance(float3 dir, float4 solarShape, float4 rayleigh)
{
    float3 d = normalize(dir);
    float cosSun = dot(d, ToSun);
    float airmass = 1.0 / (saturate(d.y) + 0.15);
    float phase = 0.75 * (1.0 + cosSun * cosSun);
    float4 inscatter = 1.0 - exp(-RayleighDepth * rayleigh * airmass);
    return SkyStrength * solarShape * phase * inscatter;
}

bool OnSunDisk(float3 dir)
{
    return SunUp != 0u && 1.0 - dot(normalize(dir), ToSun) <= SunOneMinusCosMax;
}

float4 MaterialReflectance(uint material, uint4 idx)
{
    uint row = material * HERO_COUNT;
    return float4(Reflectance[row + idx.x], Reflectance[row + idx.y], Reflectance[row + idx.z], Reflectance[row + idx.w]);
}

// ── One pixel-sample ──────────────────────────────────────────────────────────────────────────────

struct SampleResult
{
    float3 Xyz;
    bool   HitSurface;
    float  Depth;
    float3 Albedo;
    float3 Normal;
    uint   Rays;
};

SampleResult TraceSample(uint x, uint y, uint pixelHash, uint sampleIndex)
{
    SampleResult result = (SampleResult)0;
    uint rng = PcgHash(pixelHash ^ PcgHash(sampleIndex));

    uint hero = (pixelHash + sampleIndex) % HERO_COUNT;
    uint4 idx = (hero + uint4(0u, 1u, 2u, 3u) * COMPANION_STRIDE) % HERO_COUNT;
    float4 l0 = LightTable[idx.x];
    float4 l1 = LightTable[idx.y];
    float4 l2 = LightTable[idx.z];
    float4 l3 = LightTable[idx.w];
    float4 solarShape = float4(l0.x, l1.x, l2.x, l3.x);
    float4 rayleigh = float4(l0.y, l1.y, l2.y, l3.y);
    float4 sunIrradiance = float4(l0.z, l1.z, l2.z, l3.z);
    float4 sunDisk = float4(l0.w, l1.w, l2.w, l3.w);

    // Box-filtered camera ray through a random point of the pixel.
    float jx = NextFloat(rng);
    float jy = NextFloat(rng);
    float px = (2.0 * ((float(x) + jx) / float(Width)) - 1.0) * TanHalfFovY * Aspect;
    float py = (1.0 - 2.0 * ((float(y) + jy) / float(Height))) * TanHalfFovY;
    float3 dir = normalize(CamForward + px * CamRight + py * CamUp);
    float3 origin = CamPos;

    float4 radiance = 0.0;
    float4 throughput = 1.0;
    result.Depth = asfloat(0x7F800000u); // +∞ until the camera ray hits something

    for (uint bounce = 0u; ; bounce++)
    {
        Hit hit;
        result.Rays++;
        if (!TraceClosest(origin, dir, hit))
        {
            float4 sky = DomeRadiance(dir, solarShape, rayleigh);
            // Only camera rays see the disk; a bounce's sun is next-event estimation's job (no double count).
            if (bounce == 0u && OnSunDisk(dir))
                sky += sunDisk;
            radiance += throughput * sky;
            break;
        }

        uint material = TerrainMaterial;
        float4 refl = MaterialReflectance(material, idx);
        if (bounce == 0u)
        {
            result.HitSurface = true;
            result.Depth = hit.T;
            result.Albedo = MaterialAlbedo[material].xyz;
            result.Normal = hit.ShadingNormal;
        }

        float3 next = hit.Position + hit.GeometricNormal * OriginOffset(hit.Position);

        // Next-event estimation on the sun: a uniform direction in its disk; E·cos·ρ/π per wavelength.
        float u1 = NextFloat(rng);
        float u2 = NextFloat(rng);
        if (SunUp != 0u)
        {
            float3 toSun = SampleCone(ToSun, SunOneMinusCosMax, u1, u2);
            float cosS = dot(hit.ShadingNormal, toSun);
            if (cosS > 0.0 && dot(hit.GeometricNormal, toSun) > 0.0)
            {
                result.Rays++;
                if (!Occluded(next, toSun))
                    radiance += throughput * refl * (INV_PI * cosS) * sunIrradiance;
            }
        }

        if (bounce >= MaxBounces)
            break;

        // Lambertian bounce, cosine-weighted: f·cos/pdf = ρ.
        throughput *= refl;
        if (bounce >= RR_START_BOUNCE)
        {
            float survive = clamp(max(max(throughput.x, throughput.y), max(throughput.z, throughput.w)), 0.05, 1.0);
            if (NextFloat(rng) >= survive)
                break;
            throughput /= survive;
        }
        float v1 = NextFloat(rng);
        float v2 = NextFloat(rng);
        dir = CosineHemisphere(hit.ShadingNormal, v1, v2);
        if (dot(dir, hit.GeometricNormal) <= 0.0)
            break; // the smooth normal tilted the lobe under the true surface
        origin = next;
    }

    float3 xyz = (Cie[idx.x].xyz * radiance.x + Cie[idx.y].xyz * radiance.y
                + Cie[idx.z].xyz * radiance.z + Cie[idx.w].xyz * radiance.w) * (DeterministicCorrection / float(COMPANIONS));
    result.Xyz = all(isfinite(xyz)) ? xyz : float3(0.0, 0.0, 0.0);
    return result;
}

// ── Kernel ────────────────────────────────────────────────────────────────────────────────────────

[numthreads(8, 8, 1)]
void CSMain(uint3 tid : SV_DispatchThreadID)
{
    uint rays = 0u;
    if (tid.x < Width && tid.y < RowCount)
    {
        uint x = tid.x;
        uint y = RowStart + tid.y;
        uint pixel = y * Width + x;
        uint pixelHash = PcgHash(pixel);
        bool first = FirstSample == 0u;

        float4 xyzSum = first ? float4(0.0, 0.0, 0.0, 0.0) : XyzSum[pixel];
        float2 luma = 0.0;
        float4 albedoSum = 0.0;
        float4 normalSum = 0.0;
        if (!first && (AovMask & AOV_VARIANCE) != 0u)
            luma = LumaStats[pixel];
        if (!first && (AovMask & AOV_ALBEDO) != 0u)
            albedoSum = AlbedoSum[pixel];
        if (!first && (AovMask & AOV_NORMAL) != 0u)
            normalSum = NormalSum[pixel];

        for (uint s = FirstSample; s < FirstSample + SampleCount; s++)
        {
            SampleResult r = TraceSample(x, y, pixelHash, s);
            rays += r.Rays;
            xyzSum.xyz += r.Xyz;

            float n = float(s + 1u); // samples arrive in order from 0, so this is the running count
            float delta = r.Xyz.y - luma.x;
            luma.x += delta / n;
            luma.y += delta * (r.Xyz.y - luma.x);

            if (r.HitSurface)
            {
                albedoSum.xyz += r.Albedo;
                normalSum.xyz += r.Normal;
            }
            if (s == 0u && (AovMask & AOV_FIRST_HIT) != 0u)
                FirstHit[pixel] = float2(r.Depth, r.HitSurface ? float(TerrainMaterial) : -1.0);
        }

        XyzSum[pixel] = xyzSum;
        if ((AovMask & AOV_VARIANCE) != 0u)
            LumaStats[pixel] = luma;
        if ((AovMask & AOV_ALBEDO) != 0u)
            AlbedoSum[pixel] = albedoSum;
        if ((AovMask & AOV_NORMAL) != 0u)
            NormalSum[pixel] = normalSum;
    }

    // Ray counter: one atomic per wave, with a carry into the high word, so 64 bits on SM 6.5.
    uint waveRays = WaveActiveSum(rays);
    if (WaveIsFirstLane() && waveRays != 0u)
    {
        uint before;
        InterlockedAdd(RayCounter[0], waveRays, before);
        if (before > 0xFFFFFFFFu - waveRays)
            InterlockedAdd(RayCounter[1], 1u);
    }
}
