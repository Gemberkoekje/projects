// Vista offline integrator (vista-plan §5.1, §10 M0, M1.3) — DXR 1.1 inline ray tracing, Shader Model 6.5.
//
// A spectral path tracer for stills: multi-bounce Lambertian terrain, next-event estimation on a real-size sun
// disk, Russian roulette, and hero-wavelength sampling over the engine's 50-wavelength deterministic cycle (a hero
// plus three companions that share its path, so the throughput is a float4). Since M1.3 the light comes from a
// spectral atmosphere on a spherical Earth (VistaAtmosphere.hlsli): the sun reaches a surface through the
// transmittance of the air from that point, the Earth's shadow included, and rays that leave the terrain read the
// sky-view LUT. Radiance is in W·m⁻²·sr⁻¹·nm⁻¹. The C# references are Vista.Atmosphere.AtmosphereLuts and
// Vista.Rendering.IntegratorTables (pinned by Vista.Tests).
//
// ATMOSPHERE_REFERENCE (vista-plan §10 M1.3, --atmosphere reference) swaps both LUT paths for the slow, unbiased
// check: rays that leave the terrain are tracked through the air as a medium (delta tracking, next-event estimation
// to the sun disk with ratio-tracked transmittance, Rayleigh or Henyey–Greenstein phase sampling, a Lambertian
// ground sphere), and the sun's transmittance to a surface is ratio-tracked too. Free paths depend on the
// wavelength, so a reference path carries the hero wavelength only, weighted by the companion count. The same file
// then also holds CSSkyCheck, which compares the two for chosen directions, wavelength by wavelength.
//
// Determinism contract (BatchPlanner): every random number of a pixel-sample comes from (pixel, sample index)
// alone — never from the dispatch — and a pixel's samples are always added in sample-index order, starting from
// zero at sample 0. So the image is the same however the render is split into dispatches.
//
// The loupe (vista-plan §7.4, M1.1): a render may cover only a window of the frame. The output buffers are
// window-sized, but the camera ray and the random keys belong to the pixel's place in the full frame, so a window
// is bit for bit the same pixels as the full render.
//
// One thread per pixel of a row band of the window; it traces samples [FirstSample, FirstSample + SampleCount).

#include "VistaAtmosphere.hlsli"

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

// ── Bindings (all root descriptors; the atmosphere's are in VistaAtmosphere.hlsli) ────────────────

RaytracingAccelerationStructure Scene     : register(t0);
StructuredBuffer<float3> Positions        : register(t1); // TerrainMesh.Positions
StructuredBuffer<float3> Normals          : register(t2); // TerrainMesh.Normals (unit, smooth)
StructuredBuffer<uint>   Indices          : register(t3); // TerrainMesh.Indices, 3 per triangle
StructuredBuffer<float4> Cie              : register(t4); // xyz = white-balanced CIE, w = wavelength nm
StructuredBuffer<float>  Reflectance      : register(t5); // [material * HERO_COUNT + entry]
StructuredBuffer<float4> MaterialAlbedo   : register(t6); // linear-sRGB albedo per material

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
    float3 CamForward;  float _pad0;
    uint   SunUp;         uint MaxBounces; uint AovMask;    uint TerrainMaterial; // SunUp: the disk can light some point of the scene
    uint   Width;         uint Height;     uint RowStart;   uint RowCount;        // the rendered window; rows within it
    uint   FirstSample;   uint SampleCount; uint _pad1;     uint _pad2;
    uint   WindowX;       uint WindowY;    uint FrameWidth; uint FrameHeight;     // the window's place in the full frame
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

bool OnSunDisk(float3 dir)
{
    return 1.0 - dot(normalize(dir), AtmToSun) <= AtmSunOneMinusCos;
}

float4 MaterialReflectance(uint material, uint4 idx)
{
    uint row = material * HERO_COUNT;
    return float4(Reflectance[row + idx.x], Reflectance[row + idx.y], Reflectance[row + idx.z], Reflectance[row + idx.w]);
}

// ── The reference atmosphere (--atmosphere reference) ─────────────────────────────────────────────

#ifdef ATMOSPHERE_REFERENCE

#define REF_MAX_EVENTS    256u
#define REF_MAX_STEPS     65536u
#define REF_RR_START      4u

// A bound on the extinction along a segment from `h0` above the ground at cos = mu, `segment` long: the lowest
// altitude on it (the closest approach to the Earth's centre, clamped to the segment) for molecules and haze,
// and the ozone peak for ozone. The 0.1% margin covers float rounding of the densities.
float Majorant(float4 k, float h0, float mu, float segment)
{
    float groundRadius = GroundRadius();
    float tClosest = clamp(-(groundRadius + h0) * mu, 0.0, segment);
    float lowest = GroundAltitude + max(AltitudeAlong(groundRadius, h0, mu, tClosest), 0.0);
    return 1.001 * (k.x * RayleighDensity(lowest) + k.z * AerosolDensity(lowest) + k.w);
}

// Transmittance to the top of the atmosphere by ratio tracking (unbiased); the ray must miss the ground.
float RatioTrack(float4 k, float h0, float mu, inout uint rng)
{
    float groundRadius = GroundRadius();
    float top = SeaRadius + TopAltitude - groundRadius;
    float segment = DistanceToShellAbove(groundRadius, h0, top, mu);
    float sigmaBar = Majorant(k, h0, mu, segment);
    float transmittance = 1.0;
    float t = 0.0;
    for (uint n = 0u; n < REF_MAX_STEPS; n++)
    {
        t -= log(1.0 - NextFloat(rng)) / sigmaBar;
        if (t >= segment)
            break;
        float altitude = GroundAltitude + AltitudeAlong(groundRadius, h0, mu, t);
        transmittance *= saturate(1.0 - MediumAt(k, altitude).y / sigmaBar);
    }
    return transmittance;
}

float SampleRayleighCos(float u)
{
    // Inverts the CDF of 3(1 + μ²)/8: μ³ + 3μ = 8u − 4, solved by Cardano (its two cube roots multiply to −1).
    float q = 8.0 * u - 4.0;
    float s = 0.5 * q + sqrt(0.25 * q * q + 1.0);
    float c = sign(s) * pow(abs(s), 1.0 / 3.0);
    return clamp(c - 1.0 / c, -1.0, 1.0);
}

float SampleHenyeyGreensteinCos(float u, float g)
{
    if (abs(g) < 1.0e-3)
        return 1.0 - 2.0 * u;
    float s = (1.0 - g * g) / (1.0 - g + 2.0 * g * u);
    return clamp((1.0 + g * g - s * s) / (2.0 * g), -1.0, 1.0);
}

float3 Scatter(float3 d, float cosTheta, float phi)
{
    float3 t, b;
    Basis(d, t, b);
    float sinTheta = sqrt(max(0.0, 1.0 - cosTheta * cosTheta));
    return normalize(t * (sinTheta * cos(phi)) + b * (sinTheta * sin(phi)) + d * cosTheta);
}

// One path through the air from `h0` above the ground sphere along `dir`, for the wavelength whose coefficients
// are `k` and whose sun row is `sun`: its radiance in W·m⁻²·sr⁻¹·nm⁻¹. A camera ray that never scatters and leaves
// through the sun's disk sees the disk; any other path reaches the sun only through next-event estimation.
float AtmosphereReference(float4 k, float4 sun, float h0, float3 dir, bool cameraRay, inout uint rng)
{
    float groundRadius = GroundRadius();
    float top = SeaRadius + TopAltitude - groundRadius;
    float3 p = float3(0.0, groundRadius + h0, 0.0); // planet-centred; the height is tracked apart, exactly
    float h = h0;
    float3 d = dir;
    float throughput = 1.0;
    float radiance = 0.0;
    for (uint events = 0u; events < REF_MAX_EVENTS; events++)
    {
        float3 up = normalize(p);
        float mu = dot(d, up);
        bool toGround = HitsSphere(groundRadius, h, mu);
        float segment = toGround ? DistanceToSphereBelow(groundRadius, h, mu) : DistanceToShellAbove(groundRadius, h, top, mu);
        float sigmaBar = Majorant(k, h, mu, segment);

        // Delta tracking: tentative collisions at the majorant's rate, real with probability σ_t/σ̄.
        float t = 0.0;
        bool collided = false;
        float4 medium = 0.0;
        for (uint n = 0u; n < REF_MAX_STEPS; n++)
        {
            t -= log(1.0 - NextFloat(rng)) / sigmaBar;
            if (t >= segment)
                break;
            medium = MediumAt(k, GroundAltitude + AltitudeAlong(groundRadius, h, mu, t));
            if (NextFloat(rng) * sigmaBar < medium.y)
            {
                collided = true;
                break;
            }
        }

        if (!collided && !toGround)
        {
            if (cameraRay && events == 0u && OnSunDisk(d))
                radiance += throughput * sun.x;
            break; // into space
        }

        float3 toSun = SampleCone(AtmToSun, AtmSunOneMinusCos, NextFloat(rng), NextFloat(rng));
        if (!collided)
        {
            // The ground sphere, Lambertian with the ground albedo.
            p += segment * d;
            h = 0.0;
            float3 n = normalize(p);
            float cosS = dot(n, toSun);
            if (cosS > 0.0)
                radiance += throughput * sun.z * INV_PI * cosS * sun.y * RatioTrack(k, 0.0, cosS, rng);
            throughput *= sun.z;
            d = CosineHemisphere(n, NextFloat(rng), NextFloat(rng));
        }
        else
        {
            p += t * d;
            h = AltitudeAlong(groundRadius, h, mu, t);
            float3 upHere = normalize(p);
            throughput *= medium.x / medium.y; // the single-scattering albedo of the mix here
            float muS = dot(toSun, upHere);
            if (!HitsSphere(groundRadius, h, muS))
            {
                float cosA = dot(d, toSun);
                float phase = (medium.z * RayleighPhase(cosA) + medium.w * HenyeyGreenstein(cosA, MieG)) / medium.x;
                radiance += throughput * phase * sun.y * RatioTrack(k, h, muS, rng);
            }
#ifdef SINGLE_SCATTERING_ONLY
            break;
#endif
            float pick = NextFloat(rng);
            float u = NextFloat(rng);
            float cosTheta = pick * medium.x < medium.z ? SampleRayleighCos(u) : SampleHenyeyGreensteinCos(u, MieG);
            d = Scatter(d, cosTheta, 2.0 * PI * NextFloat(rng));
        }

        if (events >= REF_RR_START)
        {
            float survive = clamp(throughput, 0.05, 1.0);
            if (NextFloat(rng) >= survive)
                break;
            throughput /= survive;
        }
    }
    return radiance;
}

#endif

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

// (x, y) are full-frame pixel coordinates.
SampleResult TraceSample(uint x, uint y, uint pixelHash, uint sampleIndex)
{
    SampleResult result = (SampleResult)0;
    uint rng = PcgHash(pixelHash ^ PcgHash(sampleIndex));

    uint hero = (pixelHash + sampleIndex) % HERO_COUNT;
    uint4 idx = (hero + uint4(0u, 1u, 2u, 3u) * COMPANION_STRIDE) % HERO_COUNT;
    float4 sunDisk = float4(AtmSun[idx.x].x, AtmSun[idx.y].x, AtmSun[idx.z].x, AtmSun[idx.w].x);
    float4 sunIrradiance = float4(AtmSun[idx.x].y, AtmSun[idx.y].y, AtmSun[idx.z].y, AtmSun[idx.w].y);

    // Box-filtered camera ray through a random point of the pixel.
    float jx = NextFloat(rng);
    float jy = NextFloat(rng);
    float px = (2.0 * ((float(x) + jx) / float(FrameWidth)) - 1.0) * TanHalfFovY * Aspect;
    float py = (1.0 - 2.0 * ((float(y) + jy) / float(FrameHeight))) * TanHalfFovY;
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
            // The ray leaves the terrain: the sky (and, below the horizon, the land beyond the tile).
#ifdef ATMOSPHERE_REFERENCE
            float h = ClampAltitude(bounce == 0u ? CameraAltitude : GroundAltitude + origin.y) - GroundAltitude;
            radiance.x += throughput.x * AtmosphereReference(AtmCoefficients[idx.x], AtmSun[idx.x], h, dir, bounce == 0u, rng);
#else
            float altitude = bounce == 0u ? CameraAltitude : ClampAltitude(GroundAltitude + origin.y);
            float4 sky = SkyRadiance(bounce == 0u, altitude, dir, idx);
            // Only camera rays see the disk; a bounce's sun is next-event estimation's job (no double count).
            if (bounce == 0u && OnSunDisk(dir))
                sky += sunDisk * SunTransmittance4(idx, ClampAltitude(CameraAltitude), dir.y);
            radiance += throughput * sky;
#endif
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

        // Next-event estimation on the sun: a uniform direction in its disk; E·T·cos·ρ/π per wavelength, where T is
        // the air's transmittance from this point toward that direction (zero in the Earth's shadow).
        float u1 = NextFloat(rng);
        float u2 = NextFloat(rng);
        if (SunUp != 0u)
        {
            float3 toSun = SampleCone(AtmToSun, AtmSunOneMinusCos, u1, u2);
            float cosS = dot(hit.ShadingNormal, toSun);
            float altitude = GroundAltitude + hit.Position.y;
            if (cosS > 0.0 && dot(hit.GeometricNormal, toSun) > 0.0 && !HitsSphere(GroundRadius(), altitude - GroundAltitude, toSun.y))
            {
                result.Rays++;
                if (!Occluded(next, toSun))
                {
#ifdef ATMOSPHERE_REFERENCE
                    float transmittance = RatioTrack(AtmCoefficients[idx.x], max(altitude - GroundAltitude, 0.0), toSun.y, rng);
                    radiance.x += throughput.x * refl.x * (INV_PI * cosS) * sunIrradiance.x * transmittance;
#else
                    radiance += throughput * refl * (INV_PI * cosS) * sunIrradiance * SunTransmittance4(idx, altitude, toSun.y);
#endif
                }
            }
        }

        if (bounce >= MaxBounces)
            break;

        // Lambertian bounce, cosine-weighted: f·cos/pdf = ρ.
        throughput *= refl;
        if (bounce >= RR_START_BOUNCE)
        {
#ifdef ATMOSPHERE_REFERENCE
            float survive = clamp(throughput.x, 0.05, 1.0);
#else
            float survive = clamp(max(max(throughput.x, throughput.y), max(throughput.z, throughput.w)), 0.05, 1.0);
#endif
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

#ifdef ATMOSPHERE_REFERENCE
    // The hero carries the path alone; weighting it by the companion count keeps the estimator's mean.
    radiance = float4(radiance.x * float(COMPANIONS), 0.0, 0.0, 0.0);
#endif
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
        uint pixel = y * Width + x;                     // index into this render's (window-sized) buffers
        uint fx = WindowX + x;                          // the same pixel in the full frame
        uint fy = WindowY + y;
        uint pixelHash = PcgHash(fy * FrameWidth + fx); // keyed by the frame pixel, so a loupe is exactly a crop
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
            SampleResult r = TraceSample(fx, fy, pixelHash, s);
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

// ── The LUT sky against the reference, wavelength by wavelength (--atmosphere-check) ─────────────

#ifdef ATMOSPHERE_REFERENCE

cbuffer CheckConstants : register(b2)
{
    uint  CheckDirections;  // directions to compare
    uint  CheckChunks;      // threads per (direction, wavelength), each summing its own paths
    uint  CheckPaths;       // paths per thread in this dispatch
    uint  CheckFirstPath;   // index of this dispatch's first path within each thread's sequence
};

StructuredBuffer<float4>   CheckDirection : register(t14); // xyz: unit direction from the camera (slice 0)
RWStructuredBuffer<float4> CheckSums      : register(u6);  // per (direction, wavelength, chunk): Σ L, Σ L², LUT L, 0

[numthreads(64, 1, 1)]
void CSSkyCheck(uint3 tid : SV_DispatchThreadID)
{
    uint item = tid.x;
    if (item >= CheckDirections * HERO_COUNT * CheckChunks)
        return;
    uint chunk = item % CheckChunks;
    uint lambda = (item / CheckChunks) % HERO_COUNT;
    uint direction = item / (CheckChunks * HERO_COUNT);
    float3 dir = normalize(CheckDirection[direction].xyz);

    float4 acc = CheckFirstPath == 0u ? float4(0.0, 0.0, 0.0, 0.0) : CheckSums[item];
    if (CheckFirstPath == 0u)
        acc.z = SkyRadiance(true, CameraAltitude, dir, uint4(lambda, lambda, lambda, lambda)).x;
    float h = ClampAltitude(CameraAltitude) - GroundAltitude;
    for (uint p = 0u; p < CheckPaths; p++)
    {
        uint rng = PcgHash(PcgHash(item) ^ PcgHash(CheckFirstPath + p));
        float l = AtmosphereReference(AtmCoefficients[lambda], AtmSun[lambda], h, dir, false, rng);
        acc.x += l;
        acc.y += l * l;
    }
    CheckSums[item] = acc;
}

#endif
