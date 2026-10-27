// The sunlight volume over the valleys' pools (vista-plan §10 M1.13b), built once a render, and the low air's parity probe — DXR 1.1
// inline ray tracing, Shader Model 6.5. C# reference: Vista.Atmosphere.LowAir (VoxelColumns: the volume without the terrain).
//
// One thread a voxel of one level's volume: its place (every LowVolumeRatio-th cell of the pool map, LowVolumeLayers layers from the
// pool's floor to its top plus LowVolumeMargin), then
//  - the terrain's shadow: a ray toward the sun against this render's terrain, from the voxel or, when the voxel lies under the
//    ground (a layer near a floor can, where the floor of the pool map is an average), from the ground above it;
//  - the pools toward the sun: the haze column less the tables' flat layer (the march replaces that layer with the pools) and the
//    mist's optical depth, marched in the voxel's own level frame (the sun's height there: the Earth's curve tilts it by s/R);
//  - the mist above the voxel, straight up through its column (what dims the sky's light in the mist).
// Written as halves, two voxels an element (VistaLowAir.hlsli reads them).

#define LOW_AIR_RW 1
#include "VistaAtmosphere.hlsli"
#include "VistaLowAir.hlsli"

RaytracingAccelerationStructure Scene : register(t0);
RWStructuredBuffer<float4> ProbeOut : register(u1);   // the probe's results
StructuredBuffer<float4> ProbeIn : register(t14);     // the probe's places

cbuffer LowAirBuild : register(b0)
{
    float2 CamWorld;   uint  Level;     uint  FirstPair;   // the frame's tangent point in the world; the level; the first voxel pair
    uint   PairCount;  uint  SunSteps;  float SunReach;    uint  UpSteps;
    uint   ProbeCount; uint  BuildPad0; uint  BuildPad1;   uint  BuildPad2;
};

// The frame point of world (x, z) at `height` above the ground sphere (Vista.World.EarthFrame.ToFrame).
float3 FramePoint(float2 world, float height)
{
    float2 d = world - CamWorld;
    float s = length(d);
    if (s <= 0.0)
        return float3(0.0, height, 0.0);
    float r = GroundRadius();
    float theta = s / r;
    float half = sin(0.5 * theta);
    float horizontal = (r + height) * sin(theta);
    return float3(d.x / s * horizontal, height * cos(theta) - 2.0 * r * half * half, d.y / s * horizontal);
}

float3 FrameUp(float3 p) { return normalize(float3(p.x, p.y + GroundRadius(), p.z)); }

// The first terrain triangle along a ray: its distance, and whether it faces the ray's way (the ray started under it).
bool TraceTerrain(float3 origin, float3 dir, out float t, out bool fromBelow)
{
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = 0.0;
    ray.TMax = 1.0e7;
    RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, ray);
    q.Proceed();
    t = 0.0;
    fromBelow = false;
    if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT)
        return false;
    t = q.CommittedRayT();
    // TerrainMesh winds its triangles so that their front faces the sky; a ray that meets one front-first came from above it.
    fromBelow = !q.CommittedTriangleFrontFace();
    return true;
}

float OffsetFor(float3 p) { return 1.0e-2 + 4.0e-6 * max(max(abs(p.x), abs(p.y)), abs(p.z)); }

// The sun's direction at frame point `p` in its own level frame (world x, up, z): the sun's bearing in the world and its height here.
float3 LocalToSun(float3 p)
{
    float muS = dot(AtmToSun, FrameUp(p));
    float2 bearing = length(AtmToSun.xz) > 1.0e-6 ? normalize(AtmToSun.xz) : float2(0.0, 0.0);
    float cosE = sqrt(max(1.0 - muS * muS, 0.0));
    return float3(bearing.x * cosE, muS, bearing.y * cosE);
}

// The pools' columns from `altitude` over `world` in pool `s` (C# reference: LowAir.VoxelColumns): toward the sun (`toSun`, world x,
// up, z) to the pools' reach or SunReach, the haze column less the tables' layer's and the mist's optical depth, at the midpoints of
// steps growing quadratically from the point; straight up through the pool's volume column, the mist's optical depth.
float3 SunColumns(PoolSample s, float2 world, float altitude, float3 toSun)
{
    float hazeDelta = 0.0, mistDepth = 0.0;
    if (toSun.y > 0.0)
    {
        float reach = min(SunReach, max(LowSlabTop - altitude, 0.0) / toSun.y);
        for (uint k = 0u; k < SunSteps; k++)
        {
            float a = reach * (float(k) / float(SunSteps)) * (float(k) / float(SunSteps));
            float b = reach * (float(k + 1u) / float(SunSteps)) * (float(k + 1u) / float(SunSteps));
            float t = 0.5 * (a + b), dt = b - a;
            float py = altitude + toSun.y * t;
            float haze, mist;
            LowDensityAt(world + toSun.xz * t, py, haze, mist);
            hazeDelta += (haze - ValleyHaze(py)) * dt;
            mistDepth += mist * dt;
        }
    }
    float mistUp = 0.0;
    float span = s.Floor + LowVolumeSpan(s) - altitude;
    if (span > 0.0)
    {
        float dz = span / float(UpSteps);
        for (uint k = 0u; k < UpSteps; k++)
        {
            float haze, mist;
            PoolDensity(s, world, altitude + (float(k) + 0.5) * dz, haze, mist);
            mistUp += mist * dz;
        }
    }
    return float3(hazeDelta, mistDepth, mistUp);
}

[numthreads(64, 1, 1)]
void CSSunlightVolume(uint3 tid : SV_DispatchThreadID)
{
    uint pair = FirstPair + tid.x;
    if (tid.x >= PairCount)
        return;
    uint size = LowVolumeSize(Level);
    uint voxels = size * size * LowVolumeLayers;
    float4 grid = LowLevelGrid(Level);
    float cell = grid.z * float(LowVolumeRatio);
    uint4 packed = 0u;
    [unroll] for (uint h = 0u; h < 2u; h++)
    {
        uint voxel = 2u * pair + h;
        if (voxel >= voxels)
            break;
        uint i = voxel % size, j = (voxel / size) % size, layer = voxel / (size * size);
        float2 world = grid.xy + float2(float(i), float(j)) * cell;
        bool any;
        PoolSample s = PoolAt(world, any);
        float altitude = s.Floor + LowLayerHeight(s, float(layer));

        // The sun at the voxel: the frame's direction, and its height in the voxel's own vertical.
        float3 p = FramePoint(world, altitude - GroundAltitude);
        float3 up = FrameUp(p);
        float3 toSun = LocalToSun(p);
        float shadow = 1.0;
        if (toSun.y > 0.0)
        {
            // From under the ground, the ground above it.
            float3 origin = p;
            float tUp;
            bool fromBelow;
            if (TraceTerrain(p, up, tUp, fromBelow) && fromBelow)
                origin = p + up * (tUp + OffsetFor(p));
            float tSun;
            bool unused;
            shadow = TraceTerrain(origin + AtmToSun * OffsetFor(origin), AtmToSun, tSun, unused) ? 0.0 : 1.0;
        }
        float3 columns = SunColumns(s, world, altitude, toSun);
        uint2 v = uint2(f32tof16(shadow) | (f32tof16(columns.x) << 16), f32tof16(columns.y) | (f32tof16(columns.z) << 16));
        if (h == 0u)
            packed.xy = v;
        else
            packed.zw = v;
    }
    LowAir[LowVolumeOffset(Level) + pair] = packed;
}

// The low air at chosen places, for --gpu-selftest's parity with LowAir: probe i reads ProbeIn[i] (x, z: a world place, y: an
// altitude) and writes ProbeOut[4i] = (the pool's floor, depth, mist, haze at the floor), [4i + 1] = (the haze and the mist there, the
// noise's wisp and patch), [4i + 2] = the sunlight there (SunlightAt) and [4i + 3] = the volume's columns from there (SunColumns, the
// sun's direction where the point stands; its height in w).
[numthreads(64, 1, 1)]
void CSLowAirProbe(uint3 tid : SV_DispatchThreadID)
{
    uint item = tid.x;
    if (item >= ProbeCount)
        return;
    float4 q = ProbeIn[item];
    bool any;
    PoolSample s = PoolAt(q.xz, any);
    float haze, mist;
    PoolDensity(s, q.xz, q.y, haze, mist);
    float wisp = LowNoise3(LowNoiseSeed, float3(q.x / LowWispScale, q.y / LowWispVertical, q.z / LowWispScale));
    float patch = LowNoise2(LowHash(LowNoiseSeed ^ 0x9E3779B9u), q.xz / LowPatchScale);
    ProbeOut[4u * item] = float4(s.Floor, s.Depth, s.Mist, s.HazeFloor);
    ProbeOut[4u * item + 1u] = float4(haze, mist, wisp, patch);
    ProbeOut[4u * item + 2u] = SunlightAt(s, q.xz, q.y);
    float3 toSun = LocalToSun(FramePoint(q.xz, q.y - GroundAltitude));
    ProbeOut[4u * item + 3u] = float4(SunColumns(s, q.xz, q.y, toSun), toSun.y);
}
