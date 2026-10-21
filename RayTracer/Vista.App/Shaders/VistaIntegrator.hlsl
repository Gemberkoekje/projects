// Vista offline integrator (vista-plan §5.1, §10 M0, M1.3) — DXR 1.1 inline ray tracing, Shader Model 6.5.
//
// A spectral path tracer for stills: multi-bounce Lambertian terrain, next-event estimation on a real-size sun
// disk, Russian roulette, and hero-wavelength sampling over the engine's 50-wavelength deterministic cycle (a hero
// plus three companions that share its path, so the throughput is a float4). Since M1.3 the light comes from a
// spectral atmosphere on a spherical Earth (VistaAtmosphere.hlsli): the sun reaches a surface through the
// transmittance of the air from that point, the Earth's shadow included, and rays that leave the terrain read the
// sky-view LUT. Since M1.4 every segment that ends on the terrain, camera and bounce, carries the air between its ends
// (aerial perspective: AerialSegment), with the world bent onto the Earth; since M1.9 the mesh itself is bent, in a frame
// around the camera (HeightAbove, LocalUp), and reaches past the horizon. Radiance is in
// W·m⁻²·sr⁻¹·nm⁻¹. The C# references are Vista.Atmosphere.AtmosphereLuts, AerialPerspective and
// Vista.Rendering.IntegratorTables (pinned by Vista.Tests).
//
// Since M1.5 (LightSampling = 1, the default) every surface samples the sun's disk and a path's first surface also samples
// the sky (a distribution over the sky view's luminance, VistaSkyLight.hlsli), and the bounce's own direction still counts
// when it reaches either: multiple importance sampling weighs them by the power heuristic, so golden hour and twilight, lit
// mostly by the sky, converge faster. The bounce's density in those weights includes its Russian-roulette survival, and is
// zero after the last bounce, where the lights carry everything. LightSampling = 0 is M1.4's estimator (the sun alone
// sampled, the sky left to the bounce), kept for comparison; the reference atmosphere always works that way.
//
// ATMOSPHERE_REFERENCE (vista-plan §10 M1.3, M1.4; --atmosphere reference) swaps the LUT paths for the slow, unbiased
// check: the air is tracked as a medium (delta tracking, next-event estimation to the sun disk with ratio-tracked
// transmittance, Rayleigh or Henyey–Greenstein phase sampling, a Lambertian ground sphere) both along the segments
// that end on the terrain and on rays that leave it, and the sun's transmittance to a surface is ratio-tracked too.
// Free paths depend on the wavelength, so a reference path carries the hero wavelength only, weighted by the companion
// count. REFERENCE_TERRAIN_SHADOWS (--atmosphere referenceShadowed) adds the terrain's shadow to the sunlight the air
// scatters once.
// The reference build also holds CSSkyCheck, which compares LUT and reference for chosen directions and segments,
// wavelength by wavelength; the LUT build holds CSAerialProbe, the aerial perspective's parity probe, CSSkyLuminance,
// which measures the sky distribution's cells, CSSkyProbe, its sampling's parity probe, and CSIncidentMeter, the light on
// the exposure's grey card (M1.10a).
//
// Determinism contract (BatchPlanner): every random number of a pixel-sample comes from (pixel, sample index) alone
// (and the render's RandomStream, 0 for every image anyone sees) — never from the dispatch — and a pixel's samples are
// always added in sample-index order, starting from zero at sample 0. So the image is the same however the render is
// split into dispatches.
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
#define T_MAX             1.0e7
#ifndef SKY_LIGHT_BOUNCES
#define SKY_LIGHT_BOUNCES 1u    // the surfaces of a path that sample the sky as a light: the first (vista-plan §10 M1.5, measured)
#endif

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
    uint   LightSampling; uint SkySampling; uint RandomStream; uint RrStartBounce; // 1: MIS of sun, sky and bounce; 1: the sky distribution is bound and lit
    float2 CamWorld;      uint MapCount;   uint _pad3;                            // the mesh's tangent point in the world; normal maps bound (M1.9)
    float2 Map0Origin;    float Map0Cell;  uint Map0Size;                         // the world grid's normal map
    float2 Map1Origin;    float Map1Cell;  uint Map1Size;                         // the 32 m far grid's
};

StructuredBuffer<uint> VertexMapNormals : register(t19); // TerrainMesh.MapNormals: the maps' normal at each vertex, two halves (x, z)
StructuredBuffer<uint> NormalMap0       : register(t20); // NormalMap.Packed of the world grid
StructuredBuffer<uint> NormalMap1       : register(t21); // and of the 32 m far grid

#include "VistaSkyLight.hlsli"

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

// ── The shading the mesh's triangles cannot carry (vista-plan §10 M1.9; C# reference: Vista.World.NormalMap) ───────
//
// A view-dependent triangle many grid samples wide carries the normals of its three corners only: the gullies inside it,
// light on one wall and dark on the other, would vanish from a preview that a final renders. So a hit adds the grid's own
// normal there less the normal the corners would interpolate (their map normals): nothing on a triangle as fine as the grid,
// the grid's shading on a coarse one. The maps' normals are x and z in the flat world; the bent frame turns them by s/R,
// under 0.6° anywhere a map reaches, which a difference of two of them does not notice.

float2 UnpackHalf2(uint packed)
{
    return float2(f16tof32(packed & 0xFFFFu), f16tof32(packed >> 16));
}

// The world x and z of frame point `p`: its bearing from the camera and its distance along the ground (EarthFrame inverted).
float2 WorldXZ(float3 p)
{
    float horizontal = length(p.xz);
    if (horizontal <= 0.0)
        return CamWorld;
    float r = GroundRadius();
    return CamWorld + p.xz * (r * atan2(horizontal, p.y + r) / horizontal);
}

bool OnMap(float2 world, float2 origin, float cell, uint size)
{
    float2 far = origin + cell * float(size - 1u);
    return all(world >= origin) && all(world <= far);
}

// Bilinear between the samples around `world`, the edge's outside the map (NormalMap.Sample).
float2 SampleMap0(float2 world)
{
    float2 uv = clamp((world - Map0Origin) / Map0Cell, 0.0, float(Map0Size - 1u));
    uint2 c = min(uint2(uv), uint2(Map0Size - 2u, Map0Size - 2u));
    float2 f = uv - float2(c);
    uint k = c.y * Map0Size + c.x;
    float2 bottom = lerp(UnpackHalf2(NormalMap0[k]), UnpackHalf2(NormalMap0[k + 1u]), f.x);
    float2 top = lerp(UnpackHalf2(NormalMap0[k + Map0Size]), UnpackHalf2(NormalMap0[k + Map0Size + 1u]), f.x);
    return lerp(bottom, top, f.y);
}

float2 SampleMap1(float2 world)
{
    float2 uv = clamp((world - Map1Origin) / Map1Cell, 0.0, float(Map1Size - 1u));
    uint2 c = min(uint2(uv), uint2(Map1Size - 2u, Map1Size - 2u));
    float2 f = uv - float2(c);
    uint k = c.y * Map1Size + c.x;
    float2 bottom = lerp(UnpackHalf2(NormalMap1[k]), UnpackHalf2(NormalMap1[k + 1u]), f.x);
    float2 top = lerp(UnpackHalf2(NormalMap1[k + Map1Size]), UnpackHalf2(NormalMap1[k + Map1Size + 1u]), f.x);
    return lerp(bottom, top, f.y);
}

// The finest map that holds the point, else the coarsest's edge (TerrainHierarchy.MapNormal).
float2 MapNormal(float2 world)
{
    if (MapCount < 2u || OnMap(world, Map0Origin, Map0Cell, Map0Size))
        return SampleMap0(world);
    return SampleMap1(world);
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
    if (MapCount != 0u)
    {
        float2 corners = UnpackHalf2(VertexMapNormals[i0]) * (1.0 - bary.x - bary.y) + UnpackHalf2(VertexMapNormals[i1]) * bary.x
                       + UnpackHalf2(VertexMapNormals[i2]) * bary.y;
        float2 detail = MapNormal(WorldXZ(hit.Position)) - corners;
        ns = normalize(ns + float3(detail.x, 0.0, detail.y));
    }
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

// The pdf in solid angle of next-event estimation's uniform direction in the sun's disk.
float SunConePdf()
{
    return 1.0 / (2.0 * PI * AtmSunOneMinusCos);
}

float4 MaterialReflectance(uint material, uint4 idx)
{
    uint row = material * HERO_COUNT;
    return float4(Reflectance[row + idx.x], Reflectance[row + idx.y], Reflectance[row + idx.z], Reflectance[row + idx.w]);
}

// ── The world bent onto the Earth (vista-plan §10 M1.4, M1.9) ─────────────────────────────────────
//
// Since M1.9 the mesh itself is bent onto the ground sphere around the camera (Vista.World.EarthFrame): its positions are
// in the frame whose origin is the ground sphere's point below the camera, +Y the camera's vertical, the planet's centre at
// (0, −R, 0); a world point keeps its height above the ground and sinks by the curvature drop 2R·sin²(s/2R) at a horizontal
// distance s from the camera (785 m at 100 km), its vertical tilted by s/R. The camera stands at (0, h, 0). So a point's
// height above the ground sphere and its vertical come straight from its position, and a segment between two points is the
// straight chord between them. (M1.4 traced the terrain flat and bent it only for the air; the far field, out past the
// horizon, needs the geometry bent too.) C# reference: EarthFrame.HeightAbove and LocalUp.

// The height above the ground sphere of frame point `p`: |p − c| − R written so float keeps its metres.
float HeightAbove(float3 p)
{
    float r = GroundRadius();
    float horizontal2 = p.x * p.x + p.z * p.z;
    return (horizontal2 + p.y * (p.y + 2.0 * r)) / (sqrt(horizontal2 + (p.y + r) * (p.y + r)) + r);
}

// The local vertical at frame point `p`: the sun's height there for its transmittance and the Earth's shadow.
float3 LocalUp(float3 p)
{
    return normalize(float3(p.x, p.y + GroundRadius(), p.z));
}

// The air between frame points `a` and `b`: the segment seen from `a` (AerialSegment's input) and its unit direction.
AirSegment SegmentBetween(float3 a, float3 b, out float3 dir)
{
    float3 chord = b - a;
    float len = length(chord);
    float3 up = LocalUp(a);
    dir = len > 0.0 ? chord / len : up;
    AirSegment seg;
    seg.Height = max(HeightAbove(a), 0.0);
    seg.Mu = clamp(dot(dir, up), -1.0, 1.0);
    seg.MuS = clamp(dot(AtmToSun, up), -1.0, 1.0);
    seg.Nu = clamp(dot(dir, AtmToSun), -1.0, 1.0);
    seg.Length = len;
    return seg;
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

// Whether the terrain hides the sun from a path's first event in the air (REFERENCE_TERRAIN_SHADOWS): the planet-centred
// point is the frame point p − (0, R, 0) (to half a metre in float; it lies in open air). Only the first: it lies on a
// segment of open air, between the camera or a surface and the next surface or the sky, while later events of a path that
// ignores the terrain may sit under the mountains or on the ground sphere beneath them, where the terrain would shadow
// light that could never have got there (it darkened even the noon sky by 3%).
bool TerrainShadows(uint events, float3 p, float3 toSun)
{
#ifdef REFERENCE_TERRAIN_SHADOWS
    if (events != 0u)
        return false;
    float3 world = float3(p.x, p.y - GroundRadius(), p.z);
    return Occluded(world + toSun * OriginOffset(world), toSun);
#else
    return false;
#endif
}

// One path through the air, for the wavelength whose coefficients are `k` and whose sun row is `sun`, from the
// planet-centred point `p` (the frame point plus (0, R, 0); its height `h0` above the ground sphere tracked apart, exactly)
// along `dir`: its radiance in W·m⁻²·sr⁻¹·nm⁻¹. A camera ray that never scatters and leaves through the sun's disk sees
// the disk; any other path reaches the sun only through next-event estimation.
//
// A positive `surface` is the length of an air segment that ends on the terrain (vista-plan §10 M1.4): the first
// flight stops there. If it gets there unscattered, `reached` is set and nothing has been added: the caller shades the
// surface. If it scatters first, the rest of the path runs in the air alone, as the LUTs assume. Inside the air the
// terrain is ignored, so the reference checks the LUTs' model — except that REFERENCE_TERRAIN_SHADOWS lets the terrain
// hide the sun from the first event's next-event estimation (see TerrainShadows): the known limit of the LUTs, air in a
// mountain's shadow, measured on the light it scatters once.
float AirPath(float4 k, float4 sun, float3 p, float h0, float3 dir, float surface, bool cameraRay, inout uint rng, out bool reached)
{
    reached = false;
    float groundRadius = GroundRadius();
    float top = SeaRadius + TopAltitude - groundRadius;
    float h = h0;
    float3 d = dir;
    float throughput = 1.0;
    float radiance = 0.0;
    for (uint events = 0u; events < REF_MAX_EVENTS; events++)
    {
        float3 up = normalize(p);
        float mu = dot(d, up);
        bool toSurface = events == 0u && surface > 0.0;
        bool toGround = !toSurface && HitsSphere(groundRadius, h, mu);
        float segment = toSurface ? surface : toGround ? DistanceToSphereBelow(groundRadius, h, mu) : DistanceToShellAbove(groundRadius, h, top, mu);
        float sigmaBar = Majorant(k, h, mu, segment);

        // Delta tracking: tentative collisions at the majorant's rate, real with probability σ_t/σ̄. A chord between
        // two low points may dip under the ground sphere's bulge; the air there is taken at the ground's density.
        float t = 0.0;
        bool collided = false;
        float4 medium = 0.0;
        for (uint n = 0u; n < REF_MAX_STEPS; n++)
        {
            t -= log(1.0 - NextFloat(rng)) / sigmaBar;
            if (t >= segment)
                break;
            medium = MediumAt(k, GroundAltitude + max(AltitudeAlong(groundRadius, h, mu, t), 0.0));
            if (NextFloat(rng) * sigmaBar < medium.y)
            {
                collided = true;
                break;
            }
        }

        if (!collided && toSurface)
        {
            reached = true;
            return 0.0;
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
            if (cosS > 0.0 && !TerrainShadows(events, p, toSun))
                radiance += throughput * sun.z * INV_PI * cosS * sun.y * RatioTrack(k, 0.0, cosS, rng);
            throughput *= sun.z;
            d = CosineHemisphere(n, NextFloat(rng), NextFloat(rng));
        }
        else
        {
            p += t * d;
            h = max(AltitudeAlong(groundRadius, h, mu, t), 0.0);
            float3 upHere = normalize(p);
            throughput *= medium.x / medium.y; // the single-scattering albedo of the mix here
            float muS = dot(toSun, upHere);
            if (!HitsSphere(groundRadius, h, muS) && !TerrainShadows(events, p, toSun))
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
    uint key = pixelHash ^ PcgHash(sampleIndex);
    // Another, independent stream (stream 0 is every image's). The key is hashed before the stream joins it: mixed by XOR
    // alone, sample i of stream s would draw exactly the numbers of sample s of stream i.
    if (RandomStream != 0u)
        key = PcgHash(PcgHash(key) ^ PcgHash(RandomStream));
    uint rng = PcgHash(key);

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
    float bsdfPdf = 0.0; // the density with which the last surface's bounce chose `dir`, its Russian-roulette survival included
    bool skyNee = false; // whether the last surface sampled the sky as a light
    result.Depth = asfloat(0x7F800000u); // +∞ until the camera ray hits something

    for (uint bounce = 0u; ; bounce++)
    {
        Hit hit;
        result.Rays++;
        if (!TraceClosest(origin, dir, hit))
        {
            // The ray leaves the terrain: the sky (and, below the horizon, the ground sphere beyond the mesh), read in the
            // camera's frame — the sky view is built for one vertical, which the bent frame tilts by s/R at a distance s: under
            // 0.3° across the world grid, up to 4° at the far field's reach (vista-plan §10 M1.9).
#ifdef ATMOSPHERE_REFERENCE
            // The reference starts from the ray's own place on the Earth, so its vertical and the sun's height there are exact.
            float h = ClampAltitude(bounce == 0u ? CameraAltitude : GroundAltitude + HeightAbove(origin)) - GroundAltitude;
            bool open;
            radiance.x += throughput.x * AirPath(AtmCoefficients[idx.x], AtmSun[idx.x], float3(origin.x, origin.y + GroundRadius(), origin.z), h, dir, -1.0,
                                                 bounce == 0u, rng, open);
#else
            float altitude = bounce == 0u ? CameraAltitude : ClampAltitude(GroundAltitude + HeightAbove(origin));
            float4 sky = SkyRadiance(bounce == 0u, altitude, dir, idx);
            if (bounce == 0u)
            {
                // A camera ray sees the disk itself.
                if (OnSunDisk(dir))
                    sky += sunDisk * SunTransmittance4(idx, ClampAltitude(CameraAltitude), dir.y);
            }
            else if (LightSampling != 0u)
            {
                // The surface this bounce left also sampled the sky and the sun as lights: the bounce takes its share of
                // each (vista-plan §10 M1.5). The disk is seen through the air from that surface, along its own vertical,
                // as next-event estimation sees it. Under M1.4's estimator the sky is the bounce's alone and the disk
                // next-event estimation's alone.
                if (skyNee)
                    sky *= PowerHeuristic(bsdfPdf, SkyPdf(dir));
                if (SunUp != 0u && OnSunDisk(dir))
                    sky += sunDisk * SunTransmittance4(idx, GroundAltitude + max(HeightAbove(origin), 0.0), dot(dir, LocalUp(origin)))
                         * PowerHeuristic(bsdfPdf, SunConePdf());
            }
            radiance += throughput * sky;
#endif
            break;
        }

        // The air between the ray's origin and the surface (vista-plan §10 M1.4): aerial perspective on every segment,
        // camera and bounce, through the world bent onto the Earth.
        float3 airDir;
        AirSegment air = SegmentBetween(origin, hit.Position, airDir);
#ifdef ATMOSPHERE_REFERENCE
        bool reached;
        float scattered = AirPath(AtmCoefficients[idx.x], AtmSun[idx.x], float3(origin.x, origin.y + GroundRadius(), origin.z), air.Height, airDir,
                                  air.Length, false, rng, reached);
        if (!reached)
        {
            radiance.x += throughput.x * scattered; // it scattered on the way; the rest of its path was in the air alone
            break;
        }
#else
        float4 airT, airS;
#ifdef AERIAL_SKY_DIFFERENCE
        // The measured alternative (vista-plan §10 M1.4): the sky view's difference L(a) − T·L(b) along the ray.
        airT = AerialTransmittance(air, idx);
        float4 skyA = SkyRadiance(bounce == 0u, bounce == 0u ? CameraAltitude : ClampAltitude(GroundAltitude + HeightAbove(origin)), dir, idx);
        float4 skyB = SkyRadiance(false, ClampAltitude(GroundAltitude + HeightAbove(hit.Position)), dir, idx);
        airS = max(skyA - airT * skyB, 0.0) / sunIrradiance;
#else
        AerialSegment(air, idx, airT, airS);
#endif
        radiance += throughput * sunIrradiance * airS;
        throughput *= airT;
#endif

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

        // The bounce that may follow, decided before the lights so their MIS weights see the density it really has: its
        // Russian-roulette survival (its random number is drawn later, as before) times cos/π, and none after the last
        // bounce, where the lights carry everything. bsdfScale · cos(n, ω) is that density toward ω.
        float4 continued = throughput * refl;
#ifdef ATMOSPHERE_REFERENCE
        float survive = bounce >= RrStartBounce ? clamp(continued.x, 0.05, 1.0) : 1.0;
#else
        float survive = bounce >= RrStartBounce ? clamp(max(max(continued.x, continued.y), max(continued.z, continued.w)), 0.05, 1.0) : 1.0;
#endif
        float bsdfScale = bounce < MaxBounces ? survive * INV_PI : 0.0;

        // Next-event estimation on the sun: a uniform direction in its disk; E·T·cos·ρ/π per wavelength, where T is
        // the air's transmittance from this point toward that direction (zero in the Earth's shadow), taken against the
        // point's own vertical in the bent frame, as the air just above it is.
        float u1 = NextFloat(rng);
        float u2 = NextFloat(rng);
        if (SunUp != 0u)
        {
            float3 toSun = SampleCone(AtmToSun, AtmSunOneMinusCos, u1, u2);
            float cosS = dot(hit.ShadingNormal, toSun);
            float height = max(HeightAbove(hit.Position), 0.0);
            float muS = dot(toSun, LocalUp(hit.Position));
            if (cosS > 0.0 && dot(hit.GeometricNormal, toSun) > 0.0 && !HitsSphere(GroundRadius(), height, muS))
            {
                result.Rays++;
                if (!Occluded(next, toSun))
                {
#ifdef ATMOSPHERE_REFERENCE
                    float transmittance = RatioTrack(AtmCoefficients[idx.x], height, muS, rng);
                    radiance.x += throughput.x * refl.x * (INV_PI * cosS) * sunIrradiance.x * transmittance;
#else
                    // The disk is 6.8·10⁻⁵ sr, so its density dwarfs the bounce's and the weight is 1 to float precision.
                    float weight = LightSampling != 0u ? PowerHeuristic(SunConePdf(), bsdfScale * cosS) : 1.0;
                    radiance += throughput * refl * (INV_PI * cosS) * sunIrradiance * SunTransmittance4(idx, GroundAltitude + height, muS) * weight;
#endif
                }
            }
        }

#ifndef ATMOSPHERE_REFERENCE
        // Next-event estimation on the sky (vista-plan §10 M1.5) at a path's first SKY_LIGHT_BOUNCES surfaces: a direction
        // from the sky distribution, a shadow ray against the terrain, and the sky view from this point (as a bounce that
        // escapes here reads it), weighed against the bounce by the power heuristic. Deeper surfaces leave the sky to the
        // bounce: at today's albedo sampling it there too changed no variance and cost 15%. With a bounce after it
        // (MaxBounces ≥ 1) the surface's sky is shared with the bounce and the image has exactly M1.4's orders of light;
        // with no bounces at all (MaxBounces = 0) its sky sample weighs 1 and adds the sky M1.4 left out.
        skyNee = SkySampling != 0u && bounce < SKY_LIGHT_BOUNCES;
        if (skyNee)
        {
            float k1 = NextFloat(rng);
            float k2 = NextFloat(rng);
            float k3 = NextFloat(rng);
            float skyPdf;
            float3 toSky = SampleSky(k1, k2, k3, skyPdf);
            float cosK = dot(hit.ShadingNormal, toSky);
            if (cosK > 0.0 && dot(hit.GeometricNormal, toSky) > 0.0 && skyPdf > 0.0)
            {
                result.Rays++;
                if (!Occluded(next, toSky))
                {
                    float4 sky = SkyRadiance(false, ClampAltitude(GroundAltitude + HeightAbove(next)), toSky, idx);
                    radiance += throughput * refl * sky * (INV_PI * cosK / skyPdf * PowerHeuristic(skyPdf, bsdfScale * cosK));
                }
            }
        }
#endif

        if (bounce >= MaxBounces)
            break;

        // Lambertian bounce, cosine-weighted: f·cos/pdf = ρ.
        throughput = continued;
        if (bounce >= RrStartBounce)
        {
            if (NextFloat(rng) >= survive)
                break;
            throughput /= survive;
        }
        float v1 = NextFloat(rng);
        float v2 = NextFloat(rng);
        dir = CosineHemisphere(hit.ShadingNormal, v1, v2);
        if (dot(dir, hit.GeometricNormal) <= 0.0)
            break; // the smooth normal tilted the lobe under the true surface
        bsdfPdf = bsdfScale * dot(hit.ShadingNormal, dir);
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

// ── Checks: the LUT air against the reference (--atmosphere-check), and the aerial perspective's parity probe ──

cbuffer CheckConstants : register(b2)
{
    uint  CheckDirections;  // directions (or probes) to evaluate
    uint  CheckChunks;      // threads per (direction, wavelength), each summing its own paths
    uint  CheckPaths;       // paths per thread in this dispatch
    uint  CheckFirstPath;   // index of this dispatch's first path within each thread's sequence
};

StructuredBuffer<float4>   CheckDirection : register(t14); // the check: xyz a unit direction from the camera, w a segment length (0: the sky); the probe: see CSAerialProbe
RWStructuredBuffer<float4> CheckSums      : register(u6);  // the check, per (direction, wavelength, chunk): Σ L, Σ L², LUT L, 0

#ifdef ATMOSPHERE_REFERENCE

// For each direction from the camera (slice 0) and each wavelength: the LUT's radiance and CheckPaths reference paths.
// A direction with a segment length compares the light the air scatters toward the camera along that segment, which
// ends on the ground sphere if it gets there first (vista-plan §10 M1.4); without one, the whole sky (M1.3).
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
    float h = ClampAltitude(CameraAltitude) - GroundAltitude;
    float surface = CheckDirection[direction].w;
    if (surface > 0.0 && HitsSphere(GroundRadius(), h, dir.y))
        surface = min(surface, DistanceToSphereBelow(GroundRadius(), h, dir.y));
    uint4 idx = uint4(lambda, lambda, lambda, lambda);

    float4 acc = CheckFirstPath == 0u ? float4(0.0, 0.0, 0.0, 0.0) : CheckSums[item];
    if (CheckFirstPath == 0u)
    {
        if (surface > 0.0)
        {
            AirSegment seg;
            seg.Height = h;
            seg.Mu = dir.y;
            seg.MuS = clamp(AtmToSun.y, -1.0, 1.0);
            seg.Nu = dot(dir, AtmToSun);
            seg.Length = surface;
            float4 airT, airS;
#ifdef AERIAL_SKY_DIFFERENCE
            airT = AerialTransmittance(seg, idx);
            float hEnd = max(AltitudeAlong(GroundRadius(), h, dir.y, surface), 0.0);
            airS = (SkyRadiance(true, CameraAltitude, dir, idx) - airT * SkyRadiance(false, ClampAltitude(GroundAltitude + hEnd), dir, idx)) / AtmSun[lambda].y;
#else
            AerialSegment(seg, idx, airT, airS);
#endif
            acc.z = AtmSun[lambda].y * airS.x;
        }
        else
        {
            acc.z = SkyRadiance(true, CameraAltitude, dir, idx).x;
        }
    }
    for (uint p = 0u; p < CheckPaths; p++)
    {
        uint rng = PcgHash(PcgHash(item) ^ PcgHash(CheckFirstPath + p));
        bool reached;
        float l = AirPath(AtmCoefficients[lambda], AtmSun[lambda], float3(0.0, GroundRadius() + h, 0.0), h, dir, surface, false, rng, reached);
        acc.x += l;
        acc.y += l * l;
    }
    CheckSums[item] = acc;
}

#else

// The aerial perspective of chosen segments, for --gpu-selftest's parity with the C# reference (AerialPerspective):
// probe i reads CheckDirection[2i] (xyz: the start) and [2i + 1] (xyz: the end, w: the wavelength), both frame points
// (the world bent around the camera, as the mesh is), and writes CheckSums[2i] = (T, S per unit solar irradiance, μ,
// length) and [2i + 1] = (height, μ_s, ν, 0).
[numthreads(64, 1, 1)]
void CSAerialProbe(uint3 tid : SV_DispatchThreadID)
{
    uint item = tid.x;
    if (item >= CheckDirections)
        return;
    float4 a = CheckDirection[2u * item];
    float4 b = CheckDirection[2u * item + 1u];
    uint lambda = uint(b.w);
    float3 dir;
    AirSegment seg = SegmentBetween(a.xyz, b.xyz, dir);
    float4 airT, airS;
    AerialSegment(seg, uint4(lambda, lambda, lambda, lambda), airT, airS);
    CheckSums[2u * item] = float4(airT.x, airS.x, seg.Mu, seg.Length);
    CheckSums[2u * item + 1u] = float4(seg.Height, seg.MuS, seg.Nu, 0.0);
}

// ── The sky as a light: its measurement and its parity probe (vista-plan §10 M1.5) ──────────────────

RWStructuredBuffer<float> SkyLuminanceOut : register(u7);

// The sky distribution's measurement: for each (cell, terrain slice) — item = cell · SKY_TERRAIN_SLICES + slice − 1 —
// the mean over the cell's SKY_DIST_SUB² points of the sky view's luminance in that slice, Σ_λ ȳ_λ·E_λ·(smooth + HG·haze),
// in the integrator's Y units. The CPU averages the slices and builds the distribution (SkyDistribution.Build). C#
// reference: SkyDistribution.CellLuminance.
[numthreads(64, 1, 1)]
void CSSkyLuminance(uint3 tid : SV_DispatchThreadID)
{
    uint item = tid.x;
    if (item >= SKY_DIST_BANDS * SKY_DIST_COLUMNS * SKY_TERRAIN_SLICES)
        return;
    uint slice = 1u + item % SKY_TERRAIN_SLICES;
    uint cell = item / SKY_TERRAIN_SLICES;
    uint band = cell / SKY_DIST_COLUMNS;
    uint column = cell % SKY_DIST_COLUMNS;
    float muS = clamp(AtmToSun.y, -1.0, 1.0);
    float sinS = sqrt(max(1.0 - muS * muS, 0.0));
    float sum = 0.0;
    for (uint a = 0u; a < SKY_DIST_SUB; a++)
    {
        for (uint b = 0u; b < SKY_DIST_SUB; b++)
        {
            float2 p = SkyCellPoint(band, column, a, b);
            float sinTheta = sqrt(max(1.0 - p.x * p.x, 0.0));
            float phase = HenyeyGreenstein(sinTheta * cos(p.y) * sinS + p.x * muS, MieG);
            for (uint l = 0u; l < ATM_WAVELENGTHS; l += 4u)
            {
                uint4 lambdas = l + uint4(0u, 1u, 2u, 3u);
                uint4 idx = min(lambdas, ATM_WAVELENGTHS - 1u);
                float4 smooth, mie;
                SampleSkyViewSlice(slice, p.x, p.y, idx, smooth, mie);
                float4 weight = float4(Cie[idx.x].y * AtmSun[idx.x].y, Cie[idx.y].y * AtmSun[idx.y].y,
                                       Cie[idx.z].y * AtmSun[idx.z].y, Cie[idx.w].y * AtmSun[idx.w].y);
                weight *= float4(lambdas < ATM_WAVELENGTHS); // the last group's duplicates count nothing
                sum += dot(weight, smooth + phase * mie);
            }
        }
    }
    SkyLuminanceOut[item] = sum / float(SKY_DIST_SUB * SKY_DIST_SUB);
}

// ── The incident meter (vista-plan §10 M1.10a) ───────────────────────────────────────────────────────
//
// An 18% grey card at the camera's altitude, facing the sun's azimuth and tilted up to the sun's elevation, never below the
// horizontal: lit by the sun through the air (the visible part of its disk over the ground sphere) and by the camera's sky view
// (slice 0: below the horizon, the lit ground sphere it holds) over the card's hemisphere. The terrain is not in its way. Item =
// group · (SKY_DIST_BANDS + 1) + band, for the four wavelengths of the group: a band item sums, over the band's cells, the mean over
// each cell's SKY_DIST_SUB² points of L·cos⁺(card, ω) (the CPU multiplies by the cell's solid angle and adds the bands); the last
// item of a group is the sun's irradiance on the card. Written as four floats per item into SkyLuminanceOut, which no other
// kernel uses at the same time. C# reference: IncidentMeter.BandSky and IncidentMeter.SunOnCard.
#define INCIDENT_GROUPS ((ATM_WAVELENGTHS + 3u) / 4u)

[numthreads(64, 1, 1)]
void CSIncidentMeter(uint3 tid : SV_DispatchThreadID)
{
    uint item = tid.x;
    if (item >= INCIDENT_GROUPS * (SKY_DIST_BANDS + 1u))
        return;
    uint band = item % (SKY_DIST_BANDS + 1u);
    uint4 lambdas = 4u * (item / (SKY_DIST_BANDS + 1u)) + uint4(0u, 1u, 2u, 3u);
    uint4 idx = min(lambdas, ATM_WAVELENGTHS - 1u);
    float4 toa = float4(AtmSun[idx.x].y, AtmSun[idx.y].y, AtmSun[idx.z].y, AtmSun[idx.w].y);
    float muS = clamp(AtmToSun.y, -1.0, 1.0);
    float sinS = sqrt(max(1.0 - muS * muS, 0.0));
    float cardCos = saturate(AtmToSun.y);
    float cardSin = sqrt(max(1.0 - cardCos * cardCos, 0.0));
    float4 light = 0.0;
    if (band == SKY_DIST_BANDS)
    {
        float height = ClampAltitude(CameraAltitude) - GroundAltitude;
        float sunMu;
        float visible = SunVisibility(height, muS, sunMu);
        if (visible > 0.0)
        {
            float3 c = SampleColumns(GroundAltitude + height, sunMu);
            float4 transmittance = exp(-float4(OpticalDepth(AtmCoefficients[idx.x], c), OpticalDepth(AtmCoefficients[idx.y], c),
                                               OpticalDepth(AtmCoefficients[idx.z], c), OpticalDepth(AtmCoefficients[idx.w], c)));
            light = toa * visible * transmittance * max(cardSin * sinS + cardCos * muS, 0.0);
        }
    }
    else
    {
        float4 sum = 0.0;
        for (uint column = 0u; column < SKY_DIST_COLUMNS; column++)
        {
            for (uint a = 0u; a < SKY_DIST_SUB; a++)
            {
                for (uint b = 0u; b < SKY_DIST_SUB; b++)
                {
                    float2 p = SkyCellPoint(band, column, a, b);
                    float sinTheta = sqrt(max(1.0 - p.x * p.x, 0.0));
                    float cosCard = cardSin * sinTheta * cos(p.y) + cardCos * p.x;
                    if (cosCard <= 0.0)
                        continue;
                    float phase = HenyeyGreenstein(sinTheta * cos(p.y) * sinS + p.x * muS, MieG);
                    float4 smooth, mie;
                    SampleSkyViewSlice(0u, p.x, p.y, idx, smooth, mie);
                    sum += (smooth + phase * mie) * cosCard;
                }
            }
        }
        light = toa * sum / float(SKY_DIST_SUB * SKY_DIST_SUB);
    }
    light *= float4(lambdas < ATM_WAVELENGTHS); // the last group's duplicates count nothing
    SkyLuminanceOut[4u * item] = light.x;
    SkyLuminanceOut[4u * item + 1u] = light.y;
    SkyLuminanceOut[4u * item + 2u] = light.z;
    SkyLuminanceOut[4u * item + 3u] = light.w;
}

// The sky distribution's sampling and pdf, for --gpu-selftest's parity with SkyDistribution: probe i reads
// CheckDirection[2i] (xyz: three random numbers) and [2i + 1] (xyz: a unit direction), and writes CheckSums[2i] = (the
// sampled direction, its pdf) and [2i + 1] = (the direction's pdf, its band, its column, 0).
[numthreads(64, 1, 1)]
void CSSkyProbe(uint3 tid : SV_DispatchThreadID)
{
    uint item = tid.x;
    if (item >= CheckDirections)
        return;
    float4 u = CheckDirection[2u * item];
    float3 d = CheckDirection[2u * item + 1u].xyz;
    float pdf;
    float3 dir = SampleSky(u.x, u.y, u.z, pdf);
    uint band, column;
    SkyCellOf(d, band, column);
    CheckSums[2u * item] = float4(dir, pdf);
    CheckSums[2u * item + 1u] = float4(SkyPdf(d), float(band), float(column), 0.0);
}

#endif
