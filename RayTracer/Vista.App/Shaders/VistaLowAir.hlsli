// The valleys' pools in the low air (vista-plan §10 M1.13b), shared by the integrator (VistaIntegrator.hlsl) and the sunlight
// volume's builder (VistaLowAir.hlsl). Every function here is the float twin of Vista.Atmosphere.LowAir, operation for operation, so
// --gpu-selftest can hold them against it.
//
// The pools are a density in three dimensions: each valley's pool on its own floor (the pool map, Vista.Weather.ValleyPools, two
// levels: the world's grid every 64 m and the far field every 256 m), its haze thinning upward to a soft top, and mist from the floor
// up to where its air reaches the dew point; a 3D noise moves the tops into wisps and a 2D one opens patches in the mist. The
// sunlight volume holds, over each level and in layers that follow its floors, the terrain's shadow and the pools' own optical depth
// toward the sun, built once a render.
//
// Needs VistaAtmosphere.hlsli (its constants hold the low air's rows).

// The pool cells of both levels, then the two sunlight volumes (two voxels an element). The volume's builder writes them.
#ifdef LOW_AIR_RW
RWStructuredBuffer<uint4> LowAir : register(u0);
#else
StructuredBuffer<uint4> LowAir : register(t24);
#endif

#define LOW_MODE_NONE  0u
#define LOW_MODE_POOLS 1u

// Whether the pools are on: LowMode, in a shader compiled with LOW_AIR (the renderer's variant for a render with pools); a shader
// compiled without it holds none of their code, so a view without pools keeps M1.13a's registers and speed.
#ifdef LOW_AIR
#define LOW_AIR_ON (LowMode != LOW_MODE_NONE)
#else
#define LOW_AIR_ON false
#endif
#define LowFloorDepth  25.0 // how far under its floor a pool reaches, m (C# reference: LowAir.FloorDepthMetres)

struct PoolSample
{
    float Floor;     // the valley floor, m above sea level
    float Depth;     // the pool's depth above it, m
    float Mist;      // the mist's depth above it, m
    float HazeFloor; // the pool's haze at its floor, in the haze's unit
    float HazeTop;   // and at its top
    float Water;     // the mist's liquid water before the cap, g/m³
    float Cover;     // how far the mist's top stands above the ground, m (small at a deck's edge)
};

// ── The noise (C# reference: LowAir.Noise2, LowAir.Noise3) ───────────────────────────────────────────

uint LowHash(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float LowFade(float t) { return t * t * t * (t * (t * 6.0 - 15.0) + 10.0); }

// The 2D gradient g (0 to 7: 22.5° + 45°·g) dotted with (dx, dy), by arithmetic (threads would read a table one index at a time):
// (cos, sin) or, for 1, 2, 5 and 6, (sin, cos), with x negative for 2 to 5 and y for 4 to 7 (C# reference: LowAir.Gradient2).
float LowGradient2(uint g, float dx, float dy)
{
    bool swap = (((g + 1u) >> 1) & 1u) != 0u;
    float gx = swap ? 0.3826834 : 0.9238795, gy = swap ? 0.9238795 : 0.3826834;
    if (g - 2u < 4u)
        gx = -gx;
    if (g >= 4u)
        gy = -gy;
    return gx * dx + gy * dy;
}

// Perlin's (2002) gradient h (0 to 15: the twelve edges of a cube, four twice) dotted with (x, y, z), by his arithmetic.
float LowGradient3(uint h, float3 d)
{
    float u = h < 8u ? d.x : d.y;
    float v = h < 4u ? d.y : (h == 12u || h == 14u ? d.x : d.z);
    return ((h & 1u) == 0u ? u : -u) + ((h & 2u) == 0u ? v : -v);
}

float LowCorner2(uint seed, int ix, int iy, float dx, float dy)
{
    return LowGradient2(LowHash(seed ^ LowHash(asuint(ix) ^ LowHash(asuint(iy)))) >> 29, dx, dy);
}

// 2D gradient noise in lattice units, roughly in [−1, 1].
float LowNoise2(uint seed, float2 p)
{
    float fx = floor(p.x), fy = floor(p.y);
    int ix = int(fx), iy = int(fy);
    float dx = p.x - fx, dy = p.y - fy;
    float n00 = LowCorner2(seed, ix, iy, dx, dy);
    float n10 = LowCorner2(seed, ix + 1, iy, dx - 1.0, dy);
    float n01 = LowCorner2(seed, ix, iy + 1, dx, dy - 1.0);
    float n11 = LowCorner2(seed, ix + 1, iy + 1, dx - 1.0, dy - 1.0);
    float u = LowFade(dx), v = LowFade(dy);
    float a = n00 + (n10 - n00) * u;
    float b = n01 + (n11 - n01) * u;
    return (a + (b - a) * v) * 1.4142135;
}

float LowCorner3(uint seed, int ix, int iy, int iz, float3 d)
{
    return LowGradient3(LowHash(seed ^ LowHash(asuint(ix) ^ LowHash(asuint(iy) ^ LowHash(asuint(iz))))) >> 28, d);
}

// 3D gradient noise in lattice units, clamped to [−1, 1].
float LowNoise3(uint seed, float3 p)
{
    float3 f = floor(p);
    int ix = int(f.x), iy = int(f.y), iz = int(f.z);
    float3 d = p - f;
    float n000 = LowCorner3(seed, ix, iy, iz, d);
    float n100 = LowCorner3(seed, ix + 1, iy, iz, d - float3(1.0, 0.0, 0.0));
    float n010 = LowCorner3(seed, ix, iy + 1, iz, d - float3(0.0, 1.0, 0.0));
    float n110 = LowCorner3(seed, ix + 1, iy + 1, iz, d - float3(1.0, 1.0, 0.0));
    float n001 = LowCorner3(seed, ix, iy, iz + 1, d - float3(0.0, 0.0, 1.0));
    float n101 = LowCorner3(seed, ix + 1, iy, iz + 1, d - float3(1.0, 0.0, 1.0));
    float n011 = LowCorner3(seed, ix, iy + 1, iz + 1, d - float3(0.0, 1.0, 1.0));
    float n111 = LowCorner3(seed, ix + 1, iy + 1, iz + 1, d - float3(1.0, 1.0, 1.0));
    float u = LowFade(d.x), v = LowFade(d.y), w = LowFade(d.z);
    float a0 = n000 + (n100 - n000) * u, a1 = n010 + (n110 - n010) * u;
    float b0 = n001 + (n101 - n001) * u, b1 = n011 + (n111 - n011) * u;
    float a = a0 + (a1 - a0) * v, b = b0 + (b1 - b0) * v;
    return clamp(a + (b - a) * w, -1.0, 1.0);
}

// ── The pool map ──────────────────────────────────────────────────────────────────────────────────

float4 LowLevelGrid(uint level) { return level == 0u ? LowLevel0 : LowLevel1; }

PoolSample UnpackPool(uint4 c)
{
    PoolSample s;
    s.Floor = asfloat(c.x);
    s.Depth = f16tof32(c.y & 0xFFFFu);
    s.Mist = f16tof32(c.y >> 16);
    s.HazeFloor = f16tof32(c.z & 0xFFFFu);
    s.HazeTop = f16tof32(c.z >> 16);
    s.Water = f16tof32(c.w & 0xFFFFu);
    s.Cover = f16tof32(c.w >> 16);
    return s;
}

PoolSample LerpPool(PoolSample a, PoolSample b, float t)
{
    PoolSample s;
    s.Floor = a.Floor + (b.Floor - a.Floor) * t;
    s.Depth = a.Depth + (b.Depth - a.Depth) * t;
    s.Mist = a.Mist + (b.Mist - a.Mist) * t;
    s.HazeFloor = a.HazeFloor + (b.HazeFloor - a.HazeFloor) * t;
    s.HazeTop = a.HazeTop + (b.HazeTop - a.HazeTop) * t;
    s.Water = a.Water + (b.Water - a.Water) * t;
    s.Cover = a.Cover + (b.Cover - a.Cover) * t;
    return s;
}

// Where `world` lies on a level: its grid coordinates, and whether it lies on it.
bool OnLowLevel(uint level, float2 world, out float2 uv)
{
    float4 g = LowLevelGrid(level);
    uv = (world - g.xy) / g.z;
    return all(uv >= 0.0) && all(uv <= g.w - 1.0);
}

// A level's pool at grid coordinates `uv` (on the level), bilinear between its four cells.
PoolSample LowLevelSample(uint level, float2 uv)
{
    uint size = uint(LowLevelGrid(level).w);
    uint offset = level == 0u ? 0u : LowLevel1Offset;
    uint2 c = min(uint2(uv), uint2(size - 2u, size - 2u));
    float2 f = uv - float2(c);
    uint k = offset + c.y * size + c.x;
    PoolSample bottom = LerpPool(UnpackPool(LowAir[k]), UnpackPool(LowAir[k + 1u]), f.x);
    PoolSample top = LerpPool(UnpackPool(LowAir[k + size]), UnpackPool(LowAir[k + size + 1u]), f.x);
    return LerpPool(bottom, top, f.y);
}

// The pool over `world`: the world's level, blending into the far field's over the world's outer LowBlendMetres (as the terrain
// does); the far field's beyond it; none outside both (`any` false, and a pool of nothing at sea level).
PoolSample PoolAt(float2 world, out bool any)
{
    float2 uv0, uv1;
    bool on0 = OnLowLevel(0u, world, uv0);
    bool on1 = LowLevels > 1u && OnLowLevel(1u, world, uv1);
    any = on0 || on1;
    PoolSample none = (PoolSample)0;
    if (!any)
        return none;
    if (!on0)
        return LowLevelSample(1u, uv1);
    PoolSample fine = LowLevelSample(0u, uv0);
    if (!on1)
        return fine;
    float2 edge = min(uv0, LowLevel0.w - 1.0 - uv0) * LowLevel0.z; // metres to the world's nearest edge
    float t = saturate(min(edge.x, edge.y) / LowBlendMetres);
    if (t >= 1.0)
        return fine;
    return LerpPool(LowLevelSample(1u, uv1), fine, t);
}

// ── The ceiling map ───────────────────────────────────────────────────────────────────────────────
//
// Every LowCeilingBlock-th cell of each level a block (1 km, 4 km): the highest any pool reaches over it and one block around it,
// four to an element after the volumes. Above it the march strides; a step of up to a block from a point stays under its bound.

float LowCeilingBlockValue(uint level, float2 uv)
{
    uint blocks = (uint(LowLevelGrid(level).w) - 1u) / LowCeilingBlock + 1u;
    uint offset = level == 0u ? 0u : ((uint(LowLevel0.w) - 1u) / LowCeilingBlock + 1u) * ((uint(LowLevel0.w) - 1u) / LowCeilingBlock + 1u);
    uint2 b = min(uint2(uv / float(LowCeilingBlock)), uint2(blocks - 1u, blocks - 1u));
    uint index = offset + b.y * blocks + b.x;
    return asfloat(LowAir[LowCeilingOffset + index / 4u][index % 4u]);
}

// The highest any pool reaches over `world` (C# reference: LowAir.CeilingAt): its block's on the world's level, the higher of both
// levels' where the world's blends into the far field's, the far field's beyond; far below everything outside both. `block`: how
// far a step may go from `world` and stay under that bound, m (the level's block; the far field's outside both).
float CeilingAt(float2 world, out float block)
{
    float2 uv0, uv1;
    bool on0 = OnLowLevel(0u, world, uv0);
    bool on1 = LowLevels > 1u && OnLowLevel(1u, world, uv1);
    block = (on0 ? LowLevel0.z : LowLevels > 1u ? LowLevel1.z : LowLevel0.z) * float(LowCeilingBlock);
    if (!on0)
        return on1 ? LowCeilingBlockValue(1u, uv1) : -1.0e9;
    float fine = LowCeilingBlockValue(0u, uv0);
    if (!on1)
        return fine;
    float2 edge = min(uv0, LowLevel0.w - 1.0 - uv0) * LowLevel0.z;
    return min(edge.x, edge.y) >= LowBlendMetres ? fine : max(fine, LowCeilingBlockValue(1u, uv1));
}

// ── The density ───────────────────────────────────────────────────────────────────────────────────

// The mist's extinction at liquid water `lwc`, g/m³: Kunkel's (1984) fit, capped at the mist's limit.
float MistExtinction(float lwc) { return lwc > 0.0 ? 0.1447 * pow(min(lwc, MistWaterLimit), 0.88) : 0.0; }

// How much of the wisps and patches a mist takes (C# reference: LowAir.Fray): the larger of its edge's share (1 where the ground
// rises to its top, 0 LowFray metres under it) and its weakness (1 less its water over the cap). A strong deck's top over a valley's
// middle lies flat and sharp; its edges, where it laps the slopes, fray (the Sunnegga webcam, vista-plan §10).
float MistFray(PoolSample s)
{
    float edge = 1.0 - saturate(s.Cover / LowFray);
    float weak = 1.0 - saturate(s.Water / MistWaterLimit);
    return max(edge, weak);
}

// The pools' haze (in the haze's unit) and mist (extinction per metre) at `altitude` m above sea level over `world`, given the
// pool there (`s`, from PoolAt). The mist is uniform under a logistic top LowMistSoftness soft (a fog layer is mixed by its own
// turbulence). The 3D noise moves both tops by up to LowWispAmplitude, the 2D one the mist's by a share of its depth, never above
// its pool's.
void PoolDensity(PoolSample s, float2 world, float altitude, out float haze, out float mist)
{
    haze = 0.0;
    mist = 0.0;
    if (!(s.Depth > 0.0))
        return;
    float u = altitude - s.Floor;
    // Nothing in the rock under the floor (the march never looks there; the reference, which ignores the terrain in the air, would
    // find a pool there hundreds of metres deep).
    if (u > s.Depth + LowWispAmplitude + 8.0 * LowHazeSoftness || u < -LowFloorDepth)
        return;
    // Deep in a pool of haze alone the wisps would move a top 8 softnesses away: its logistic stands at 1 within 3·10⁻⁴.
    float wisp = s.Mist > 0.0 || u > s.Depth - LowWispAmplitude - 8.0 * LowHazeSoftness
        ? LowNoise3(LowNoiseSeed, float3(world.x / LowWispScale, altitude / LowWispVertical, world.y / LowWispScale))
        : 0.0;
    float top = s.Depth + LowWispAmplitude * wisp;
    float profile = s.HazeFloor + (s.HazeTop - s.HazeFloor) * saturate(u / s.Depth);
    haze = profile / (1.0 + exp((u - top) / LowHazeSoftness));
    if (s.Mist > 0.0)
    {
        float patch = LowNoise2(LowHash(LowNoiseSeed ^ 0x9E3779B9u), world / LowPatchScale);
        float fray = MistFray(s);
        float mistTop = min(s.Mist * (1.0 + LowPatchStrength * fray * patch), s.Depth) + LowWispAmplitude * fray * wisp; // never above its pool
        if (u < mistTop + 8.0 * LowMistSoftness)
            mist = MistExtinction(s.Water) / (1.0 + exp((u - mistTop) / LowMistSoftness));
    }
}

// The same at a point, the pool looked up.
void LowDensityAt(float2 world, float altitude, out float haze, out float mist)
{
    bool any;
    PoolSample s = PoolAt(world, any);
    PoolDensity(s, world, altitude, haze, mist);
}

// The haze column of the pool's linear profile from `altitude` up to its top, m of the haze's unit (the noise left out): what a
// sky ray from inside a pool crosses, straight up.
float PoolHazeAbove(PoolSample s, float altitude)
{
    if (!(s.Depth > 0.0))
        return 0.0;
    float u = clamp(altitude - s.Floor, 0.0, s.Depth);
    float at = s.HazeFloor + (s.HazeTop - s.HazeFloor) * (u / s.Depth);
    return 0.5 * (at + s.HazeTop) * (s.Depth - u);
}

// ── The sunlight volume ───────────────────────────────────────────────────────────────────────────
//
// Over each level, every LowVolumeRatio-th cell (128 m and 512 m), LowVolumeLayers layers: LowVolumePoolLayers from the pool's floor
// up to its top plus LowVolumeMargin, the rest from there to the pools' highest reach (LowSlabTop: the low air a ridge may shade),
// two voxels to an element: (the terrain's shadow 0 to 1, the pools' haze column toward the sun less the tables'
// layer's, m of the haze's unit) and (the mist's optical depth toward the sun, its optical depth straight up), half precision.

uint LowVolumeSize(uint level) { return (uint(LowLevelGrid(level).w) - 1u) / LowVolumeRatio + 1u; }

uint LowVolumeOffset(uint level) { return level == 0u ? LowVolume0Offset : LowVolume1Offset; }

// The span of a pool's volume column above its floor, m.
float LowVolumeSpan(PoolSample s) { return max(s.Depth, s.Mist) + LowVolumeMargin; }

// The top of a volume column above its floor: the pools' highest reach, at least just above its pool's span.
float LowLayerTop(PoolSample s) { return max(LowSlabTop - s.Floor, LowVolumeSpan(s) + 1.0); }

// The height above its floor of a volume column's layer `l` (C# reference: LowAir.LayerHeight): the first LowVolumePoolLayers over
// the pool, the rest from there to LowLayerTop.
float LowLayerHeight(PoolSample s, float l)
{
    float pool = LowVolumeSpan(s), np = float(LowVolumePoolLayers - 1u);
    return l <= np ? pool * l / np : pool + (LowLayerTop(s) - pool) * (l - np) / float(LowVolumeLayers - LowVolumePoolLayers);
}

// Its inverse: the fractional layer at `u` m above the floor, clamped to the column.
float LowLayerOf(PoolSample s, float u)
{
    float pool = LowVolumeSpan(s), np = float(LowVolumePoolLayers - 1u);
    float l = u <= pool ? np * u / pool : np + float(LowVolumeLayers - LowVolumePoolLayers) * (u - pool) / (LowLayerTop(s) - pool);
    return clamp(l, 0.0, float(LowVolumeLayers - 1u));
}

float4 UnpackVoxel(uint2 v) { return float4(f16tof32(v.x & 0xFFFFu), f16tof32(v.x >> 16), f16tof32(v.y & 0xFFFFu), f16tof32(v.y >> 16)); }

float4 LowVoxel(uint level, uint i, uint j, uint layer)
{
    uint size = LowVolumeSize(level);
    uint voxel = (layer * size + j) * size + i;
    uint4 e = LowAir[LowVolumeOffset(level) + voxel / 2u];
    return UnpackVoxel((voxel & 1u) != 0u ? e.zw : e.xy);
}

// The sunlight at `altitude` over `world` in pool `s`: (shadow, haze column delta, mist depth toward the sun, mist depth up),
// trilinear in the level's columns and in their layers (LowLayerOf: the layers follow the floors). The finest level holding
// the point; outside both, or with no volume built, full light and no pool.
float4 SunlightAt(PoolSample s, float2 world, float altitude)
{
    if (LowVolumeLayers == 0u)
        return float4(1.0, 0.0, 0.0, 0.0);
    float2 uv;
    uint level = 0u;
    if (!OnLowLevel(0u, world, uv))
    {
        if (LowLevels < 2u || !OnLowLevel(1u, world, uv))
            return float4(1.0, 0.0, 0.0, 0.0);
        level = 1u;
    }
    uint size = LowVolumeSize(level);
    float2 vuv = uv / float(LowVolumeRatio);
    uint2 c = min(uint2(vuv), uint2(size - 2u, size - 2u));
    float2 f = vuv - float2(c);
    float w = LowLayerOf(s, altitude - s.Floor);
    uint l0 = min(uint(w), LowVolumeLayers - 2u);
    float fl = w - float(l0);
    float4 sum = 0.0;
    [unroll] for (uint k = 0u; k < 8u; k++)
    {
        uint di = k & 1u, dj = (k >> 1u) & 1u, dl = (k >> 2u) & 1u;
        float weight = (di != 0u ? f.x : 1.0 - f.x) * (dj != 0u ? f.y : 1.0 - f.y) * (dl != 0u ? fl : 1.0 - fl);
        sum += weight * LowVoxel(level, c.x + di, c.y + dj, l0 + dl);
    }
    return sum;
}
