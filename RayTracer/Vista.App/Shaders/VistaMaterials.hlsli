// The ground's materials (vista-plan §4.3, §10 M1.12), included by VistaIntegrator.hlsl after its helpers (PcgHash, the normal
// maps, WorldXZ, HeightAbove, LocalUp) and the surface tables. Per hit, the rules of Vista.World.MaterialRules put rock, scree,
// soil and grass where the slope, the altitude, the aspect, the erosion's maps and the hardness of the bed the surface cuts say,
// and blend their spectra; the C# twins, operation for operation, are MaterialNoise, MaterialField.Inputs and
// MaterialRules.Evaluate (--gpu-selftest's parity check reads CSMaterialProbe). The constants come from C# once
// (MaterialConstants), so the two sides read one definition.
//
// Rock takes its colour from its bed (M1.11d's strata): the bed table's hardness at the hit's height mixes the soft rock's spectrum
// into the hard rock's, the bed's own two tints brighten or darken it and warm or cool it, and its laminae (about a metre thick)
// are each a little lighter or darker. Soil turns toward scree's colour where it is stony; wet ground is darker.
//
// Below the mesh's detail the shading carries the ground's roughness, on a path's first surface only (MaterialBump): a bump of
// ridged noise on rock and scree and of smooth noise on soil and grass, in octaves from 1.6 m down to 10 cm, each fading out where
// a pixel covers more than a quarter of it, laid triplanar (so a steep face is rough across, not streaked); and on hard rock the
// laminae step the face, its slope swung up and down through each one (a riser, then a tread), each lamina at its own strength.
// Rough diffuse reflection
// (Oren–Nayar in Fujii's form) is ρ·A·(1 + σ·s/t): per material A = 1/(1 + (1/2 − 2/(3π))·σ) folds into the reflectance it is
// shaded with, σ·A into the backscatter's; Lambert is σ = 0.
//
// Forest (M1.12b) takes the open ground below the tree line, on slopes trees hold to, away from standing water and from the clearings
// its own noise opens (more on dry ridges, fewer in moist hollows). Its canopy is shaded by the kernels of MODIS's BRDF
// (CanopyKernels): ρ_iso·(1 + kv·K_vol + kg·K_geo), the needles' volume scattering and the crowns' shadows on the gaps between them,
// with ρ_iso the spectrum scaled so the albedo under a white sky is the spectrum's; or (--canopy rough) as rough diffuse ground.

#define MAT_ROCK_HARD 0u
#define MAT_ROCK_SOFT 1u
#define MAT_SCREE     2u
#define MAT_SOIL      3u
#define MAT_GRASS     4u
#define MAT_FOREST    5u

StructuredBuffer<uint2>  MaterialMap : register(t22); // MaterialField.Packed: the bed-table offset's bits; loose, area, wet, mottle codes
StructuredBuffer<float4> BedTable    : register(t23); // MaterialField.Beds: hardness, tint u, tint v, 0 per entry

cbuffer MaterialConstants : register(b3) // Vista.World.MaterialConstants
{
    float MatRockSlope;       float MatRockWidth;        float MatHardnessShift;   float MatRockNoise;
    float MatCoverFrom;       float MatCoverTo;          float MatCoverOnSteep;    float MatOutcropSlopeFrom;
    float MatOutcropSlopeTo;  float MatOutcropHardFrom;  float MatOutcropHardTo;   float MatSlopeNoise;
    float MatScreeSlopeFrom;  float MatScreeSlopeTo;     float MatChannelLogFrom;  float MatChannelLogTo;
    float MatWetDepthFrom;    float MatWetDepthTo;       float MatGrassLine;       float MatGrassBand;
    float MatAspectShift;     float MatGrassNoise;       float MatGrassSlopeFrom;  float MatGrassSlopeTo;
    float MatBareFrom;        float MatBareTo;           float MatWetDarkening;    float MatPoleward;
    uint  MatNoiseSeed;       uint  MatPad0;             uint  MatPad4;            uint  MatPad5;
    float MatBreakup1;        float MatBreakup2;         float MatTableScale;      float MatTableLast;
    float MatMapOriginX;      float MatMapOriginZ;       float MatMapCell;         uint  MatMapSize;
    float MatHardColourMid;   float MatHardColourWidth;  float MatBedBrightness;   float MatBedWarmth;
    float MatLaminaContrast;  float MatStepStrength;     float MatLaminaePerEntry; uint  MatMode;
    uint  MatDiffuse;         float MatPixelAngle;       float MatBumpFinest;      uint  MatBumpOctaves;
    float MatRoughRock;       float MatRoughScree;       float MatRoughSoil;       float MatRoughGrass;
    int   MatCamCellX;        int   MatCamCellZ;         float MatCamFracX;        float MatCamFracZ;
    float MatStoneTilt;       float MatStoneGap;         float MatPad6;            float MatPad7;
    float MatTreeLine;        float MatTreeBand;         float MatTreeAspectShift; float MatTreeNoise;
    float MatTreeEdge;        float MatTreeSlopeFrom;    float MatTreeSlopeTo;     float MatBreakup3;
    float MatClearingFrom;    float MatClearingTo;       float MatMoistLogFrom;    float MatMoistLogTo;
    float MatMoistShift;      uint  MatForest;           uint  MatCanopy;          float MatRoughCanopy;
    float MatCanopyVolume;    float MatCanopyGeometric;  float MatCanopyIso;       float MatPad8;
};

// HLSL's smoothstep is not defined for a falling edge, which the rules use; this is MaterialRules.SmoothStep.
float MatSmoothStep(float a, float b, float x)
{
    float t = clamp((x - a) / (b - a), 0.0, 1.0);
    return t * t * (3.0 - 2.0 * t);
}

// ── The breakup noise (Vista.World.MaterialNoise) ──────────────────────────────────────────────────

static const float2 MAT_GRADIENTS[8] =
{
    float2(0.9238795, 0.3826834), float2(0.3826834, 0.9238795), float2(-0.3826834, 0.9238795), float2(-0.9238795, 0.3826834),
    float2(-0.9238795, -0.3826834), float2(-0.3826834, -0.9238795), float2(0.3826834, -0.9238795), float2(0.9238795, -0.3826834),
};

uint MatGradient(uint seed, int ix, int iy)
{
    return PcgHash(seed ^ PcgHash(asuint(ix) ^ PcgHash(asuint(iy)))) >> 29;
}

float MatCorner(uint seed, int ix, int iy, float dx, float dy)
{
    uint g = MatGradient(seed, ix, iy);
    return MAT_GRADIENTS[g].x * dx + MAT_GRADIENTS[g].y * dy;
}

float MatFade(float t)
{
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float MaterialNoise(uint seed, float x, float y)
{
    float fx = floor(x), fy = floor(y);
    int ix = int(fx), iy = int(fy);
    float dx = x - fx, dy = y - fy;
    float n00 = MatCorner(seed, ix, iy, dx, dy);
    float n10 = MatCorner(seed, ix + 1, iy, dx - 1.0, dy);
    float n01 = MatCorner(seed, ix, iy + 1, dx, dy - 1.0);
    float n11 = MatCorner(seed, ix + 1, iy + 1, dx - 1.0, dy - 1.0);
    float u = MatFade(dx), v = MatFade(dy);
    float nx0 = n00 + (n10 - n00) * u;
    float nx1 = n01 + (n11 - n01) * u;
    return (nx0 + (nx1 - nx0) * v) * 1.4142135;
}

// The same noise and its gradient at lattice point `base + local` (an integer cell and a small offset from it, so single precision
// keeps the finest lattice far from the world's origin): (value, ∂/∂u, ∂/∂v) in lattice units.
float3 MaterialNoiseGrad(uint seed, int2 base, float2 local)
{
    float2 f = floor(local);
    int2 i = base + int2(f);
    float2 d = local - f;
    float2 g00 = MAT_GRADIENTS[MatGradient(seed, i.x, i.y)];
    float2 g10 = MAT_GRADIENTS[MatGradient(seed, i.x + 1, i.y)];
    float2 g01 = MAT_GRADIENTS[MatGradient(seed, i.x, i.y + 1)];
    float2 g11 = MAT_GRADIENTS[MatGradient(seed, i.x + 1, i.y + 1)];
    float n00 = dot(g00, d), n10 = dot(g10, d - float2(1.0, 0.0)), n01 = dot(g01, d - float2(0.0, 1.0)), n11 = dot(g11, d - 1.0);
    float2 u = float2(MatFade(d.x), MatFade(d.y));
    float2 du = 30.0 * d * d * (d - 1.0) * (d - 1.0);
    float k1 = n10 - n00, k2 = n01 - n00, k3 = n00 - n10 - n01 + n11;
    float n = n00 + k1 * u.x + k2 * u.y + k3 * u.x * u.y;
    float2 grad = g00 + (g10 - g00) * u.x + (g01 - g00) * u.y + (g00 - g10 - g01 + g11) * (u.x * u.y) + du * float2(k1 + k3 * u.y, k2 + k3 * u.x);
    return float3(n, grad) * 1.4142135;
}

// ── The maps and the bed at a place (MaterialField.At) ──────────────────────────────────────────────

struct MatSample
{
    float Loose;
    float Log10Area;
    float Wet;
    float Hardness;
    float TintU;
    float TintV;
    float Table;
};

float MatMix(float v00, float v10, float v01, float v11, float2 f)
{
    float bottom = v00 + (v10 - v00) * f.x;
    float top = v01 + (v11 - v01) * f.x;
    return bottom + (top - bottom) * f.y;
}

float4 MatCodes(uint codes)
{
    return float4(float(codes & 0xFFu), float((codes >> 8) & 0xFFu), float((codes >> 16) & 0xFFu), float(codes >> 24));
}

float MatDepthOf(float code)
{
    return pow(10.0, code * (4.0 / 255.0) - 2.0) - 0.01;
}

MatSample MaterialSampleAt(float2 world, float height)
{
    MatSample s;
    float extent = MatMapCell * float(MatMapSize - 1u);
    float2 origin = float2(MatMapOriginX, MatMapOriginZ);
    if (MatMapSize < 2u || !(world.x >= origin.x && world.x <= origin.x + extent && world.y >= origin.y && world.y <= origin.y + extent))
    {
        s.Loose = 0.0;
        s.Log10Area = 1.0;
        s.Wet = 0.0;
        s.Hardness = 0.5;
        s.TintU = 0.0;
        s.TintV = 0.0;
        s.Table = 0.0;
        return s;
    }
    float2 uv = clamp((world - origin) / MatMapCell, 0.0, float(MatMapSize - 1u));
    uint2 c = min(uint2(uv), uint2(MatMapSize - 2u, MatMapSize - 2u));
    float2 f = uv - float2(c);
    uint k = c.y * MatMapSize + c.x;
    uint2 p00 = MaterialMap[k];
    uint2 p10 = MaterialMap[k + 1u];
    uint2 p01 = MaterialMap[k + MatMapSize];
    uint2 p11 = MaterialMap[k + MatMapSize + 1u];
    float4 c00 = MatCodes(p00.y), c10 = MatCodes(p10.y), c01 = MatCodes(p01.y), c11 = MatCodes(p11.y);
    float offset = MatMix(asfloat(p00.x), asfloat(p10.x), asfloat(p01.x), asfloat(p11.x), f);
    s.Loose = MatDepthOf(MatMix(c00.x, c10.x, c01.x, c11.x, f));
    s.Log10Area = MatMix(c00.y, c10.y, c01.y, c11.y, f) * (8.0 / 255.0) + 1.0;
    s.Wet = MatDepthOf(MatMix(c00.z, c10.z, c01.z, c11.z, f));
    float mottle = (MatMix(c00.w, c10.w, c01.w, c11.w, f) - 128.0) * (0.25 / 127.0);
    float t = clamp(height * MatTableScale + offset, 0.0, MatTableLast);
    uint e = uint(floor(t));
    float fe = t - float(e);
    float4 bed = BedTable[e];
    float4 next = BedTable[e + 1u];
    s.Hardness = clamp(bed.x + (next.x - bed.x) * fe + mottle, 0.0, 1.0);
    s.TintU = bed.y + (next.y - bed.y) * fe;
    s.TintV = bed.z + (next.z - bed.z) * fe;
    s.Table = t;
    return s;
}

// ── The rules (MaterialRules.Evaluate) ──────────────────────────────────────────────────────────────

struct MatWeights
{
    float Rock;
    float Scree;
    float Soil;
    float Grass;
    float Wet;
    float Stony;
    float Forest;
};

MatWeights MaterialWeightsOf(float slope, float poleward, float altitude, float loose, float log10Area, float waterDepth, float hardness,
    float b1, float b2, float b3)
{
    MatWeights w;
    float cover = MatSmoothStep(MatCoverFrom, MatCoverTo, loose);
    float rockAt = MatRockSlope - MatHardnessShift * (hardness - 0.5) + MatRockNoise * b1;
    float steep = MatSmoothStep(rockAt - MatRockWidth, rockAt + MatRockWidth, slope);
    float outcrop = (1.0 - cover) * MatSmoothStep(MatOutcropSlopeFrom, MatOutcropSlopeTo, slope + MatSlopeNoise * b2)
        * MatSmoothStep(MatOutcropHardFrom, MatOutcropHardTo, hardness);
    float rock = max(steep * (1.0 - MatCoverOnSteep * cover), outcrop);
    float rest = 1.0 - rock;
    float channel = MatSmoothStep(MatChannelLogFrom, MatChannelLogTo, log10Area);
    float talus = MatSmoothStep(MatScreeSlopeFrom, MatScreeSlopeTo, slope + MatSlopeNoise * b2);
    float scree = rest * cover * max(talus, channel);
    float ground = rest - scree;
    float pond = MatSmoothStep(MatWetDepthFrom, MatWetDepthTo, waterDepth);
    float forest = 0.0;
    if (MatForest != 0u)
    {
        float treeAt = MatTreeLine - MatTreeAspectShift * poleward + MatTreeNoise * b1 + MatTreeEdge * b2;
        float below = 1.0 - MatSmoothStep(treeAt - MatTreeBand, treeAt + MatTreeBand, altitude);
        float gentle = 1.0 - MatSmoothStep(MatTreeSlopeFrom, MatTreeSlopeTo, slope);
        float moist = MatSmoothStep(MatMoistLogFrom, MatMoistLogTo, log10Area);
        float stand = MatSmoothStep(MatClearingFrom, MatClearingTo, b3 + MatMoistShift * (moist - 0.5));
        forest = ground * (below * gentle * stand * (1.0 - pond));
    }
    float open = ground - forest;
    float limit = MatGrassLine - MatAspectShift * poleward + MatGrassNoise * b1;
    float high = MatSmoothStep(limit - MatGrassBand, limit + MatGrassBand, altitude);
    float tooSteep = MatSmoothStep(MatGrassSlopeFrom, MatGrassSlopeTo, slope);
    float bare = MatSmoothStep(MatBareFrom, MatBareTo, b2);
    float grass = open * ((1.0 - high) * (1.0 - tooSteep) * (1.0 - bare));
    w.Rock = rock;
    w.Scree = scree;
    w.Soil = open - grass;
    w.Grass = grass;
    w.Wet = max(channel * (1.0 - steep), pond * (1.0 - talus));
    w.Stony = max(high, tooSteep);
    w.Forest = forest;
    return w;
}

// The world grid's normal at a hit, as (x, cos of the slope, z) in the flat world: the finest normal map that holds it, else (the
// 256 m far field, or a mesh without maps) the shading normal against the local vertical.
float3 GroundNormal(float2 world, float3 p, float3 ns)
{
    float2 m;
    if (MapCount != 0u && OnMap(world, Map0Origin, Map0Cell, Map0Size))
        m = SampleMap0(world);
    else if (MapCount >= 2u && OnMap(world, Map1Origin, Map1Cell, Map1Size))
        m = SampleMap1(world);
    else
        return float3(ns.x, dot(ns, LocalUp(p)), ns.z);
    return float3(m.x, sqrt(max(1.0 - m.x * m.x - m.y * m.y, 0.0)), m.y);
}

// The rules' inputs and weights at world (x, z) and height, with the world grid's normal there (MaterialField.Inputs).
MatWeights MaterialWeightsAt(float2 world, float height, float3 normal, out MatSample s, out float slope)
{
    slope = acos(min(normal.y, 1.0)) * (180.0 / PI);
    float b1 = MaterialNoise(MatNoiseSeed, world.x / MatBreakup1, world.y / MatBreakup1);
    float b2 = MaterialNoise(PcgHash(MatNoiseSeed ^ 0x9E3779B9u), world.x / MatBreakup2, world.y / MatBreakup2);
    float b3 = MatForest != 0u ? MaterialNoise(PcgHash(MatNoiseSeed ^ 0x7F4A7C15u), world.x / MatBreakup3, world.y / MatBreakup3) : 0.0;
    s = MaterialSampleAt(world, height);
    return MaterialWeightsOf(slope, normal.z * MatPoleward, GroundAltitude + height, s.Loose, s.Log10Area, s.Wet, s.Hardness, b1, b2, b3);
}

// ── The surface at a hit ───────────────────────────────────────────────────────────────────────────

struct GroundSurface
{
    float4 Refl;   // π × the diffuse part of the BRDF, per companion wavelength (ρ·A; the canopy's ρ_iso)
    float4 Back;   // π × the backscatter's coefficient (ρ·A·σ); 0 for Lambert
    float4 Canopy; // π × the canopy kernels' coefficient (ρ_iso, by the forest's weight); 0 without them
    float3 Albedo; // linear-sRGB albedo, for the albedo output
    float3 Normal; // the shading normal, with the bump on a path's first surface
    uint   Id;     // the material with the largest weight
};

// The bump's gradient in one plane of the triplanar layout, at its octave `k` (wavelength λ): the plane's two coordinates' cells and
// offsets in `base` and `local`, scaled; ridged for rock and scree (weights `ridged`), smooth for soil and grass (`smooth`).
float2 MatBumpOctave(uint seed, int2 base, float2 local, float ridged, float smooth)
{
    float3 n = MaterialNoiseGrad(seed, base, local);
    // The ridge 1 − 2·|n|_ε / |1|_ε (RockDetail's fold): its slope is −2·n/√(n² + ε²) / |1|_ε times the noise's.
    float fold = -2.0 * n.x / (sqrt(n.x * n.x + 0.0144) * 0.8865);
    return (ridged * fold + smooth) * n.yz;
}

// The world coordinate `cell·λ0 + frac + offset` as a cell and an offset at octave k (λ = λ0·2^k): its cell index `cell >> k` and the
// rest, in that octave's lattice units.
void MatSplit(int cell, float frac, float offset, uint k, out int base, out float local)
{
    base = cell >> k;
    local = (float(cell - (base << k)) * MatBumpFinest + frac + offset) / (MatBumpFinest * float(1u << k));
}

// One size of stones on the horizontal plane at octave k (cells of λ = λ0·2^k): of the 3×3 cells around the point, the stone whose
// spot is nearest; its own tilt (a facet: the gradient is constant over the stone, so it shades flat with a sharp edge) and how
// close the point is to the gap between it and the next stone (F2 − F1, in lattice units).
float3 MatStoneOctave(uint seed, int2 base, float2 local)
{
    float2 f = floor(local);
    int2 cell = base + int2(f);
    float2 d = local - f;
    float first = 1.0e9, second = 1.0e9;
    uint nearest = 0u;
    for (int dj = -1; dj <= 1; dj++)
    {
        for (int di = -1; di <= 1; di++)
        {
            uint h = PcgHash(seed ^ PcgHash(asuint(cell.x + di) ^ PcgHash(asuint(cell.y + dj))));
            float2 spot = float2(di, dj) + 0.15 + 0.7 * float2(float(h & 0xFFFFu), float(h >> 16)) * (1.0 / 65536.0);
            float2 r = spot - d;
            float dist = dot(r, r);
            if (dist < first)
            {
                second = first;
                first = dist;
                nearest = h;
            }
            else if (dist < second)
            {
                second = dist;
            }
        }
    }
    uint t = PcgHash(nearest);
    float2 tilt = float2(float(t & 0xFFFFu), float(t >> 16)) * (2.0 / 65536.0) - 1.0;
    return float3(tilt, sqrt(second) - sqrt(first));
}

// The shading normal `ns` at frame point `p` (world offset `o` from the camera, height `height`) with the bump of weights `w` and
// the laminae's steps of a rock of hardness colour `hs`, for a camera hit whose pixel covers `footprint` metres. `shade` is how much
// the gaps between scree's stones darken it there.
float3 MaterialBump(float3 ns, float3 wo, float2 o, float height, MatWeights w, float hs, float lamina, float laminaMetres, float footprint,
    out float shade)
{
    shade = 1.0;
    float3 n = ns;
    // A pixel's footprint along the ground grows as the view grazes it: the octaves fade by that, so none aliases into streaks.
    footprint /= max(abs(dot(ns, wo)), 0.15);
    // The laminae step a hard rock face: its slope × (1 + k·sin 2π·lamina), a riser then a tread, each lamina at its own strength
    // (most faint, a few strong; the sine is 0 at a lamina's edges, so a change of strength makes no seam), fading where a pixel
    // covers a quarter of a lamina.
    float strength = float(PcgHash(asuint(int(floor(lamina))) ^ PcgHash(MatNoiseSeed ^ 0x57E9u)) >> 8) * (1.0 / 16777216.0);
    float step = MatStepStrength * strength * strength * strength * w.Rock * hs * (1.0 - MatSmoothStep(0.25 * laminaMetres, 0.5 * laminaMetres, footprint));
    if (step > 0.0)
    {
        float f = 1.0 + step * sin(2.0 * PI * frac(lamina));
        n = normalize(float3(n.x * f, n.y, n.z * f));
    }

    float ridged = w.Rock * MatRoughRock + w.Scree * MatRoughScree;
    float smoothRough = w.Soil * MatRoughSoil;
    float3 weights = n * n;
    weights *= weights;
    weights /= weights.x + weights.y + weights.z;
    float3 gradient = 0.0;
    for (uint k = 0u; k < MatBumpOctaves; k++)
    {
        float wavelength = MatBumpFinest * float(1u << k);
        float fade = MatSmoothStep(2.0 * footprint, 4.0 * footprint, wavelength);
        if (fade <= 0.0)
            continue;
        float smooth = smoothRough + (k < 2u ? w.Grass * MatRoughGrass : w.Forest * MatRoughCanopy);
        uint seed = PcgHash(MatNoiseSeed ^ (0xB0B0u + k));
        int bx, bz;
        float lx, lz;
        MatSplit(MatCamCellX, MatCamFracX, o.x, k, bx, lx);
        MatSplit(MatCamCellZ, MatCamFracZ, o.y, k, bz, lz);
        float ly = height / wavelength;
        if (weights.y > 0.02)
        {
            float2 g = MatBumpOctave(seed, int2(bx, bz), float2(lx, lz), ridged, smooth);
            gradient += fade * weights.y * float3(g.x, 0.0, g.y);
        }
        if (weights.x > 0.02)
        {
            float2 g = MatBumpOctave(PcgHash(seed ^ 0x1111u), int2(bz, 0), float2(lz, ly), ridged, smooth);
            gradient += fade * weights.x * float3(0.0, g.y, g.x);
        }
        if (weights.z > 0.02)
        {
            float2 g = MatBumpOctave(PcgHash(seed ^ 0x2222u), int2(bx, 0), float2(lx, ly), ridged, smooth);
            gradient += fade * weights.z * float3(g.x, g.y, 0.0);
        }
    }

    // Scree's stones (and the stony soil's, fewer): stones of 0.4, 0.8 and 1.6 m, each a facet of its own tilt (the sizes' tilts add,
    // so a big stone is broken into smaller faces), the gaps between them a little dark.
    float stones = w.Scree + 0.5 * w.Soil * w.Stony;
    if (stones > 0.01)
    {
        for (uint s = 2u; s < min(MatBumpOctaves, 5u); s++)
        {
            float wavelength = MatBumpFinest * float(1u << s);
            float fade = MatSmoothStep(2.0 * footprint, 4.0 * footprint, wavelength);
            if (fade <= 0.0)
                continue;
            int bx, bz;
            float lx, lz;
            MatSplit(MatCamCellX, MatCamFracX, o.x, s, bx, lx);
            MatSplit(MatCamCellZ, MatCamFracZ, o.y, s, bz, lz);
            float3 stone = MatStoneOctave(PcgHash(MatNoiseSeed ^ (0x5707u + s)), int2(bx, bz), float2(lx, lz));
            gradient += fade * stones * MatStoneTilt * float3(stone.x, 0.0, stone.y);
            shade *= 1.0 - fade * stones * MatStoneGap * (1.0 - MatSmoothStep(0.0, 0.15, stone.z));
        }
    }
    // A bumped normal must still face the camera: one turned away (a stone tilted from a grazing view) would shade black where a
    // real stone would hide behind its neighbour. It is turned back until it faces the camera a little.
    float3 bumped = normalize(n - (gradient - dot(gradient, n) * n));
    float facing = dot(bumped, wo);
    return facing < 0.05 ? normalize(bumped + (0.05 - facing) * wo) : bumped;
}

// Fujii's A for a material: 1 for Lambert, else 1/(1 + (1/2 − 2/(3π))·σ), with σ its roughness (MaterialAlbedo's w).
float MatRoughA(uint material, out float sigma)
{
    sigma = MatDiffuse != 0u ? MaterialAlbedo[material].w : 0.0;
    return 1.0 / (1.0 + 0.2877934 * sigma);
}

// The Oren–Nayar factor s/t of Fujii's form for light from `wi` seen along `wo` (both away from the surface).
float OrenNayarG(float3 n, float3 wi, float3 wo)
{
    float ni = dot(n, wi), no = dot(n, wo);
    float s = dot(wi, wo) - ni * no;
    return s > 0.0 ? s / max(max(ni, no), 1.0e-4) : s;
}

// The canopy's kernels for light from `wi` seen along `wo` about `n` (vista-plan §10 M1.12b; Wanner, Li and Strahler 1995; Lucht,
// Schaaf and Strahler 2000): kv·K_vol + kg·K_geo, RossThick's volume scattering and LiSparse-Reciprocal's crowns (height over width
// h/b = 2, round b/r = 1), so the factor on ρ_iso is one more than this, and never below 0 (looking into the shadowed side of the
// crowns toward a low sun). Both zeniths are held within 70°, where MODIS fits them; beyond it the kernels run away.
float CanopyKernels(float3 n, float3 wi, float3 wo)
{
    float ci = max(dot(n, wi), 0.342), cv = max(dot(n, wo), 0.342);
    float cosXi = clamp(dot(wi, wo), -1.0, 1.0);
    float xi = acos(cosXi);
    float vol = ((0.5 * PI - xi) * cosXi + sin(xi)) / (ci + cv) - 0.25 * PI;
    float secI = 1.0 / ci, secV = 1.0 / cv;
    float ti2 = secI * secI - 1.0, tv2 = secV * secV - 1.0;
    float ttCos = (cosXi - ci * cv) * (secI * secV);           // tan θi · tan θv · cos φ
    float d2 = max(ti2 + tv2 - 2.0 * ttCos, 0.0);
    float cross2 = max(ti2 * tv2 - ttCos * ttCos, 0.0);         // (tan θi · tan θv · sin φ)²
    float cosT = min(2.0 * sqrt(d2 + cross2) / (secI + secV), 1.0);
    float t = acos(cosT);
    float overlap = (t - sin(t) * cosT) * (secI + secV) * (1.0 / PI);
    float geo = overlap - secI - secV + 0.5 * (1.0 + cosXi) * secI * secV;
    return max(MatCanopyVolume * vol + MatCanopyGeometric * geo, -1.0);
}

// The ground at frame point `p` with shading normal `ns`, seen along `wo` (toward the ray's origin), for the companion wavelengths
// `idx`; a camera hit passes the metres its pixel covers there as `footprint` (the bump and the laminae's steps), any later surface
// 0 (none).
GroundSurface ShadeGround(float3 p, float3 ns, float3 wo, uint4 idx, float footprint)
{
    GroundSurface g;
    float2 world = WorldXZ(p);
    float height = HeightAbove(p);
    MatSample s;
    float slope;
    MatWeights w = MaterialWeightsAt(world, height, GroundNormal(world, p, ns), s, slope);

    // The rules blend over the 8 m maps' cells; on the ground a boundary is sharper and ragged. So the shading sharpens the blend
    // (each weight to the sixth power, then normalised: a pure material stays pure) after a fine noise (2.5 m and 0.6 m) has pushed
    // rock and scree against the ground; and a meadow is patchily drier, toward the soil's colour.
    float2 offset = WorldOffset(p);
    float fine = MaterialNoise(PcgHash(MatNoiseSeed ^ 0xED6Eu), world.x / 2.5, world.y / 2.5)
               + 0.5 * MaterialNoise(PcgHash(MatNoiseSeed ^ 0xED6Fu), world.x / 0.6, world.y / 0.6);
    // The forest's edge breaks into crowns (a 4 m noise pushes it against the open ground), and its stands differ in tone (a 40 m
    // noise, ±25%: denser and sparser, older and younger), which is the texture a forest shows from afar.
    float forestSharp = 0.0, forestTone = 1.0;
    if (w.Forest > 0.0)
    {
        float crown = MaterialNoise(PcgHash(MatNoiseSeed ^ 0xF0E5u), world.x / 4.0, world.y / 4.0);
        forestSharp = max(w.Forest * (1.0 + 0.6 * crown), 0.0);
        forestSharp *= forestSharp * forestSharp;
        forestSharp *= forestSharp;
        forestTone = 1.0 + 0.25 * MaterialNoise(PcgHash(MatNoiseSeed ^ 0xF0E6u), world.x / 40.0, world.y / 40.0);
    }
    float4 sharp = float4(w.Rock * (1.0 + 0.5 * fine), w.Scree * (1.0 - 0.5 * fine), w.Soil * (1.0 - 0.3 * fine), w.Grass * (1.0 - 0.5 * fine));
    sharp = max(sharp, 0.0);
    sharp *= sharp * sharp;
    sharp *= sharp;
    float total = max(sharp.x + sharp.y + sharp.z + sharp.w + forestSharp, 1.0e-12);
    sharp /= total;
    w.Rock = sharp.x;
    w.Scree = sharp.y;
    w.Soil = sharp.z;
    w.Grass = sharp.w;
    w.Forest = forestSharp / total;
    float dryGrass = 0.4 * MatSmoothStep(-0.1, 0.7, MaterialNoise(PcgHash(MatNoiseSeed ^ 0xD27u), world.x / 7.0, world.y / 7.0));

    // The rock of the bed the hit cuts: soft mixed into hard by its hardness, tinted by the bed, each lamina (thicker or thinner by
    // bed) a little lighter or darker, fading where a pixel covers more than half a lamina.
    float hs = MatSmoothStep(MatHardColourMid - MatHardColourWidth, MatHardColourMid + MatHardColourWidth, s.Hardness);
    float4 lambda = float4(Cie[idx.x].w, Cie[idx.y].w, Cie[idx.z].w, Cie[idx.w].w);
    float laminaePerEntry = MatLaminaePerEntry * (1.0 + 0.4 * s.TintU);
    float lamina = s.Table * laminaePerEntry + 0.5 * s.TintV;
    float laminaMetres = 1.0 / max(laminaePerEntry * MatTableScale, 1.0e-4);
    float shade = float(PcgHash(asuint(int(floor(lamina))) ^ MatNoiseSeed) >> 8) * (2.0 / 16777216.0) - 1.0;
    float layered = 1.0 + MatLaminaContrast * shade * (footprint > 0.0 ? MatSmoothStep(laminaMetres, 0.5 * laminaMetres, footprint) : 0.0);
    float bright = (1.0 + MatBedBrightness * s.TintU) * layered;
    float4 tint = bright * (1.0 + MatBedWarmth * s.TintV * (lambda - 550.0) / 150.0);
    float sHard, sSoft, sScree, sSoil, sGrass;
    float aHard = MatRoughA(MAT_ROCK_HARD, sHard), aSoft = MatRoughA(MAT_ROCK_SOFT, sSoft), aScree = MatRoughA(MAT_SCREE, sScree);
    float aSoil = MatRoughA(MAT_SOIL, sSoil), aGrass = MatRoughA(MAT_GRASS, sGrass);
    float4 hard = MaterialReflectance(MAT_ROCK_HARD, idx) * tint, soft = MaterialReflectance(MAT_ROCK_SOFT, idx) * tint;
    float4 scree = MaterialReflectance(MAT_SCREE, idx);
    float stony = 0.65 * w.Stony; // stony soil is debris with soil in it, between the two
    float4 soil = lerp(MaterialReflectance(MAT_SOIL, idx), scree, stony);
    float4 grass = lerp(MaterialReflectance(MAT_GRASS, idx), MaterialReflectance(MAT_SOIL, idx) * 1.4, dryGrass);
    float dry = 1.0 - MatWetDarkening * w.Wet;
    float4 rockA = lerp(soft * aSoft, hard * aHard, hs);
    float4 rockB = lerp(soft * (aSoft * sSoft), hard * (aHard * sHard), hs);
    float soilA = lerp(aSoil, aScree, stony), soilB = lerp(aSoil * sSoil, aScree * sScree, stony);
    g.Refl = saturate((w.Rock * rockA + w.Scree * aScree * scree + w.Soil * soilA * soil + w.Grass * aGrass * grass) * dry);
    g.Back = saturate((w.Rock * rockB + w.Scree * (aScree * sScree) * scree + w.Soil * soilB * soil + w.Grass * (aGrass * sGrass) * grass) * dry);
    g.Canopy = 0.0;

    float3 rockRgb = lerp(MaterialAlbedo[MAT_ROCK_SOFT].xyz, MaterialAlbedo[MAT_ROCK_HARD].xyz, hs) * bright;
    float3 soilRgb = lerp(MaterialAlbedo[MAT_SOIL].xyz, MaterialAlbedo[MAT_SCREE].xyz, stony);
    float3 grassRgb = lerp(MaterialAlbedo[MAT_GRASS].xyz, MaterialAlbedo[MAT_SOIL].xyz * 1.4, dryGrass);
    g.Albedo = (w.Rock * rockRgb + w.Scree * MaterialAlbedo[MAT_SCREE].xyz + w.Soil * soilRgb + w.Grass * grassRgb) * dry;

    // The forest: by the canopy's kernels, its isotropic part ρ_iso in Refl and the same in Canopy for the kernels; or as rough
    // diffuse ground at its own roughness (and as Lambert's under --diffuse lambert, where ρ_iso would only brighten it).
    if (w.Forest > 0.0)
    {
        float4 forest = MaterialReflectance(MAT_FOREST, idx) * (forestTone * w.Forest * dry);
        bool kernels = MatDiffuse != 0u && MatCanopy != 0u;
        float sForest;
        float aForest = MatRoughA(MAT_FOREST, sForest);
        g.Refl = saturate(g.Refl + forest * (kernels ? MatCanopyIso : aForest));
        g.Back = saturate(g.Back + forest * (kernels ? 0.0 : aForest * sForest));
        g.Canopy = kernels ? saturate(forest * MatCanopyIso) : 0.0;
        g.Albedo += MaterialAlbedo[MAT_FOREST].xyz * (forestTone * w.Forest * dry);
    }

    float gaps = 1.0;
    g.Normal = footprint > 0.0 ? MaterialBump(ns, wo, offset, height, w, hs, lamina, laminaMetres, footprint, gaps) : ns;
    g.Refl *= gaps;
    g.Back *= gaps;
    g.Canopy *= gaps;
    g.Albedo *= gaps;

    uint id = s.Hardness >= 0.5 ? MAT_ROCK_HARD : MAT_ROCK_SOFT;
    float best = w.Rock;
    if (w.Scree > best) { best = w.Scree; id = MAT_SCREE; }
    if (w.Soil > best) { best = w.Soil; id = MAT_SOIL; }
    if (w.Grass > best) { best = w.Grass; id = MAT_GRASS; }
    if (w.Forest > best) { id = MAT_FOREST; }
    g.Id = id;
    return g;
}
