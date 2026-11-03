// The ground's materials (vista-plan §4.3, §10 M1.12), included by VistaIntegrator.hlsl after its helpers (PcgHash, the normal
// maps, WorldXZ, HeightAbove, LocalUp) and the surface tables. Per hit, the rules of Vista.World.MaterialRules put rock, scree,
// soil and grass where the slope, the altitude, the aspect, the erosion's maps and the hardness of the bed the surface cuts say,
// and blend their spectra; the C# twins, operation for operation, are MaterialNoise, MaterialField.Inputs and
// MaterialRules.Evaluate (--gpu-selftest's parity check reads CSMaterialProbe). The constants come from C# once
// (MaterialConstants), so the two sides read one definition.
//
// Rock takes its colour from its bed (M1.11d's strata): the bed table's hardness at the hit's height mixes the soft rock's spectrum
// into the hard rock's, the bed's own two tints brighten or darken it and warm or cool it, and its laminae (about a metre thick)
// are each a little lighter or darker. Where the rock is massive (M1.12c) its hardness moves toward massive rock's and the beds'
// tints and laminae fade. Soil turns toward scree's colour where it is stony; wet ground is darker.
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
//
// Snow and glaciers (M1.13): the world's glacier ice covers the ground (firn above the glaciers' own equilibrium line, bare ice below,
// grey with debris where it thins), and the season's snow lies above the snowline, lower on poleward faces, in hollows and in the
// wind's lee, shed from slopes steeper than snow holds on (less in gullies) and scoured from ridges; it covers the open ground and the
// glaciers, and shows in the forest's texture only as a dusting in its gaps. Wet below the freezing level, dry above it. Where a
// glacier flows over steeper ground its firn and ice crack into crevasse fields across the flow (a shading effect, like the laminae).

#define MAT_ROCK_HARD 0u
#define MAT_ROCK_SOFT 1u
#define MAT_SCREE     2u
#define MAT_SOIL      3u
#define MAT_GRASS     4u
#define MAT_FOREST    5u
#define MAT_SNOW      6u
#define MAT_ICE       7u
#define MAT_FIRN      8u
#define MAT_SNOW_WET  9u

StructuredBuffer<uint3>  MaterialMap : register(t22); // MaterialField.Packed: the bed-table offset's bits; loose, area, wet, mottle codes; massive, hollow, ice codes
StructuredBuffer<float4> BedTable    : register(t23); // MaterialField.Beds: hardness, tint u, tint v, massive rock's hardness per entry

cbuffer MaterialConstants : register(b3) // Vista.World.MaterialConstants
{
    float MatRockSlope;       float MatRockWidth;        float MatHardnessShift;   float MatRockNoise;
    float MatCoverFrom;       float MatCoverTo;          float MatCoverOnSteep;    float MatOutcropSlopeFrom;
    float MatOutcropSlopeTo;  float MatOutcropHardFrom;  float MatOutcropHardTo;   float MatSlopeNoise;
    float MatScreeSlopeFrom;  float MatScreeSlopeTo;     float MatChannelLogFrom;  float MatChannelLogTo;
    float MatWetDepthFrom;    float MatWetDepthTo;       float MatGrassLine;       float MatGrassBand;
    float MatAspectShift;     float MatGrassNoise;       float MatGrassSlopeFrom;  float MatGrassSlopeTo;
    float MatBareFrom;        float MatBareTo;           float MatWetDarkening;    float MatPoleward;
    uint  MatNoiseSeed;       uint  MatStructure;        uint  MatStructureOctaves; uint MatPad5;
    float MatBreakup1;        float MatBreakup2;         float MatTableScale;      float MatTableLast;
    float MatMapOriginX;      float MatMapOriginZ;       float MatMapCell;         uint  MatMapSize;
    float MatHardColourMid;   float MatHardColourWidth;  float MatBedBrightness;   float MatBedWarmth;
    float MatLaminaContrast;  float MatStepStrength;     float MatLaminaePerEntry; uint  MatMode;
    uint  MatDiffuse;         float MatPixelAngle;       float MatBumpFinest;      uint  MatBumpOctaves;
    float MatRoughRock;       float MatRoughScree;       float MatRoughSoil;       float MatRoughGrass;
    int   MatCamCellX;        int   MatCamCellZ;         float MatCamFracX;        float MatCamFracZ;
    float MatStoneTilt;       float MatStoneGap;         float MatStructureDepth;  float MatStructureCoarsest;
    float MatTreeLine;        float MatTreeBand;         float MatTreeAspectShift; float MatTreeNoise;
    float MatTreeEdge;        float MatTreeSlopeFrom;    float MatTreeSlopeTo;     float MatBreakup3;
    float MatClearingFrom;    float MatClearingTo;       float MatMoistLogFrom;    float MatMoistLogTo;
    float MatMoistShift;      uint  MatForest;           uint  MatCanopy;          float MatRoughCanopy;
    float MatCanopyVolume;    float MatCanopyGeometric;  float MatCanopyIso;       float MatPad8;
    float MatSnowLine;        float MatSnowBand;         float MatSnowAspectShift; float MatSnowNoise;
    float MatSnowEdge;        float MatSnowSlopeFrom;    float MatSnowSlopeTo;     float MatSnowGully;
    float MatHollow;          float MatSnowHollowShift;  float MatSnowRidgeScour;  float MatSnowWindShift;
    float MatWindX;           float MatWindZ;            float MatFreezingLevel;   float MatSnowWetBand;
    float MatSnowPatch;       float MatForestDusting;    uint  MatSnow;            uint  MatGlaciers;
    float MatIceFrom;         float MatIceTo;            float MatGlacierLine;     float MatFirnBand;
    float MatFirnNoise;       float MatDebris;           float MatRoughSnow;       float MatPad9;
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
    float Massive; // how massive the rock is (M1.12c): 0 layered, 1 massive
    float Hollow;  // how far the ground lies below its surroundings, m (M1.13): + hollows, − ridges
    float Ice;     // the glacier ice on the ground, m (M1.13)
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
        s.Massive = 0.0;
        s.Hollow = 0.0;
        s.Ice = 0.0;
        return s;
    }
    float2 uv = clamp((world - origin) / MatMapCell, 0.0, float(MatMapSize - 1u));
    uint2 c = min(uint2(uv), uint2(MatMapSize - 2u, MatMapSize - 2u));
    float2 f = uv - float2(c);
    uint k = c.y * MatMapSize + c.x;
    uint3 p00 = MaterialMap[k];
    uint3 p10 = MaterialMap[k + 1u];
    uint3 p01 = MaterialMap[k + MatMapSize];
    uint3 p11 = MaterialMap[k + MatMapSize + 1u];
    float4 c00 = MatCodes(p00.y), c10 = MatCodes(p10.y), c01 = MatCodes(p01.y), c11 = MatCodes(p11.y);
    float offset = MatMix(asfloat(p00.x), asfloat(p10.x), asfloat(p01.x), asfloat(p11.x), f);
    s.Loose = MatDepthOf(MatMix(c00.x, c10.x, c01.x, c11.x, f));
    s.Log10Area = MatMix(c00.y, c10.y, c01.y, c11.y, f) * (8.0 / 255.0) + 1.0;
    s.Wet = MatDepthOf(MatMix(c00.z, c10.z, c01.z, c11.z, f));
    float mottle = (MatMix(c00.w, c10.w, c01.w, c11.w, f) - 128.0) * (0.25 / 127.0);
    float massive = MatMix(float(p00.z & 0xFFu), float(p10.z & 0xFFu), float(p01.z & 0xFFu), float(p11.z & 0xFFu), f) * (1.0 / 255.0);
    float hollow = (MatMix(float((p00.z >> 8) & 0xFFu), float((p10.z >> 8) & 0xFFu), float((p01.z >> 8) & 0xFFu), float((p11.z >> 8) & 0xFFu), f) - 128.0)
        * (20.0 / 127.0);
    float ice = pow(10.0, MatMix(float((p00.z >> 16) & 0xFFu), float((p10.z >> 16) & 0xFFu), float((p01.z >> 16) & 0xFFu), float((p11.z >> 16) & 0xFFu), f)
        * (3.0 / 255.0)) - 1.0;
    float t = clamp(height * MatTableScale + offset, 0.0, MatTableLast);
    uint e = uint(floor(t));
    float fe = t - float(e);
    float4 bed = BedTable[e];
    float4 next = BedTable[e + 1u];
    float hard = bed.x + (next.x - bed.x) * fe;
    hard += (bed.w + (next.w - bed.w) * fe - hard) * massive;
    float layered = 1.0 - massive;
    s.Hardness = clamp(hard + mottle, 0.0, 1.0);
    s.TintU = (bed.y + (next.y - bed.y) * fe) * layered;
    s.TintV = (bed.z + (next.z - bed.z) * fe) * layered;
    s.Table = t;
    s.Massive = massive;
    s.Hollow = hollow;
    s.Ice = ice;
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
    float Snow;    // the season's snow (M1.13)
    float Firn;    // a glacier's old snow above its equilibrium line
    float Ice;     // bare glacier ice below it
    float SnowWet; // how wet the snow is: 0 dry, 1 wet
    float Dusting; // the share of the forest's texture that shows snow
};

MatWeights MaterialWeightsOf(float slope, float poleward, float altitude, float loose, float log10Area, float waterDepth, float hardness,
    float b1, float b2, float b3, float hollowMetres, float iceMetres, float windward)
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
    float soil = open - grass;
    w.Wet = max(channel * (1.0 - steep), pond * (1.0 - talus));
    w.Stony = max(high, tooSteep);

    // The glaciers (M1.13): the world's ice covers the ground, firn above their equilibrium line, bare ice below.
    float firn = 0.0, ice = 0.0;
    if (MatGlaciers != 0u)
    {
        float glacier = MatSmoothStep(MatIceFrom, MatIceTo, iceMetres);
        float firnAt = MatGlacierLine + MatFirnNoise * b1;
        float above = MatSmoothStep(firnAt - MatFirnBand, firnAt + MatFirnBand, altitude);
        float uncovered = 1.0 - glacier;
        rock *= uncovered;
        scree *= uncovered;
        soil *= uncovered;
        grass *= uncovered;
        forest *= uncovered;
        firn = glacier * above;
        ice = glacier - firn;
    }

    // The season's snow (MaterialRules.Evaluate).
    float snow = 0.0, snowWet = 0.0, dusting = 0.0;
    if (MatSnow != 0u)
    {
        float hollow = clamp(hollowMetres / MatHollow, -1.0, 1.0);
        snowWet = 1.0 - MatSmoothStep(MatFreezingLevel - MatSnowWetBand, MatFreezingLevel + MatSnowWetBand, altitude);
        float snowAt = MatSnowLine - MatSnowAspectShift * poleward - MatSnowHollowShift * hollow + MatSnowWindShift * windward
            + MatSnowNoise * b1 + MatSnowEdge * (1.0 + MatSnowPatch * snowWet) * b2;
        float season = MatSmoothStep(snowAt - MatSnowBand, snowAt + MatSnowBand, altitude);
        float shedAt = MatSnowSlopeFrom + MatSnowGully * max(hollow, 0.0);
        float hold = 1.0 - MatSmoothStep(shedAt, shedAt + (MatSnowSlopeTo - MatSnowSlopeFrom), slope + MatSlopeNoise * b2);
        float lying = season * hold * (1.0 - MatSnowRidgeScour * max(-hollow, 0.0));
        float covered = 1.0 - lying;
        snow = lying * (rock + scree + soil + grass + firn + ice);
        rock *= covered;
        scree *= covered;
        soil *= covered;
        grass *= covered;
        firn *= covered;
        ice *= covered;
        dusting = season * MatForestDusting;
    }
    w.Rock = rock;
    w.Scree = scree;
    w.Soil = soil;
    w.Grass = grass;
    w.Forest = forest;
    w.Snow = snow;
    w.Firn = firn;
    w.Ice = ice;
    w.SnowWet = snowWet;
    w.Dusting = dusting;
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
    return MaterialWeightsOf(slope, normal.z * MatPoleward, GroundAltitude + height, s.Loose, s.Log10Area, s.Wet, s.Hardness, b1, b2, b3,
        s.Hollow, s.Ice, -(normal.x * MatWindX + normal.z * MatWindZ));
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
    float  Sun;    // how much of the sun reaches past the rock's own structure (M1a.4), on a path's first surface; else 1
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

// ── The rock's structure (M1a.4; Vista.World.RockStructure) ──────────────────────────────────────

#define RS_CELL          2.0        // RockStructure.Cell
#define RS_KERNEL        1.1        // RockStructure.KernelRadius
#define RS_RIB_ROUNDING  0.3        // RockStructure.RibRounding
#define RS_SLOPE_FROM    0.47       // RockStructure.SlopeFrom
#define RS_SLOPE_TO      0.84       // RockStructure.SlopeTo
#define RS_SEC_MAX       4.0        // RockStructure.SecMax
#define RS_CONTRAST_FROM 0.1        // RockStructure.ContrastFrom
#define RS_CONTRAST_TO   0.45       // RockStructure.ContrastTo
#define RS_SHADOW_OCTAVES 2u        // the gullies' octaves whose shadows the shading casts (one more near the camera)
#define RS_SHADOW_NEAR    0.25      // the pixel's footprint, m, below which the next octave's shadows are cast too
#define RS_SHADOW_STEPS   5u        // the steps toward the sun, 1.5 m and each twice the last plus 1.5 m, out to 46 m
#define RS_JOINT_SPACING  3.0       // the joints' mean spacing, m (the second set 1.4 times it)
#define RS_JOINT_TILT     0.6       // how far a block's face turns from the rock's, at most (a gradient)
#define RS_JOINT_CRACK    0.03      // a joint's open width, m
#define RS_JOINT_DARK     0.55      // how much an open joint darkens what lies in it

// One octave's phasor at world point `p`: stripes of angular frequency `omega` across the fall line (`across`, a unit vector),
// from a jittered point in each of the 3×3 cells (side `cell`) around p, each with its own phase and a radial kernel RS_KERNEL cells
// wide (RockStructure.Phasor). The phasor Z, its real and imaginary parts' gradients, and the kernels' sum.
void GullyPhasor(uint seed, float2 p, float omega, float cell, float2 across, out float2 z, out float2 dRe, out float2 dIm, out float weights)
{
    float2 f = floor(p / cell);
    int2 c = int2(f);
    z = 0.0;
    dRe = 0.0;
    dIm = 0.0;
    weights = 0.0;
    float inv = 1.0 / (RS_KERNEL * RS_KERNEL * cell * cell);
    for (int j = -1; j <= 1; j++)
    {
        for (int i = -1; i <= 1; i++)
        {
            uint h = PcgHash(seed ^ PcgHash(asuint(c.x + i) ^ PcgHash(asuint(c.y + j))));
            float2 q = (f + float2(i, j) + 0.15 + 0.7 * float2(float(h & 0xFFFFu), float(h >> 16)) * (1.0 / 65536.0)) * cell;
            float2 r = p - q;
            float d2 = dot(r, r) * inv;
            if (d2 >= 1.0)
                continue;
            float k = 1.0 - d2;
            float w = k * k * k;
            float dw = -6.0 * k * k * inv;
            float phase = omega * dot(r, across) + float(PcgHash(h) >> 8) * (6.2831853 / 16777216.0);
            float s, co;
            sincos(phase, s, co);
            z += w * float2(co, s);
            dRe += (dw * co) * r - (w * s * omega) * across;
            dIm += (dw * s) * r + (w * co * omega) * across;
            weights += w;
        }
    }
}

// The gullies and ribs down the fall line at world point `p` (m) on ground whose height gradient is `g0` (the world grid's), for a
// pixel covering `footprint` metres along the ground (RockStructure.Gullies): MatStructureOctaves octaves from MatStructureCoarsest
// metres down, each a phasor's stripes across the fall line shaped into a rib and a rounded gully floor, each finer octave following
// the fall line of the grid and the coarser ones (so gullies branch into their tributaries up the slope), each fading out between four
// and two pixels a wavelength. Returns the gradient it adds to the ground's, and in `floorShare` how far the point lies in a gully's
// floor (0 on a rib, 1 at the floor), by the octaves' depths.
float2 RockGullies(float2 p, float2 g0, float footprint, out float floorShare)
{
    floorShare = 0.0;
    float steep = length(g0);
    float slopeFade = MatSmoothStep(RS_SLOPE_FROM, RS_SLOPE_TO, steep);
    if (slopeFade <= 0.0 || MatStructureDepth <= 0.0)
        return 0.0;
    float sec = min(sqrt(1.0 + steep * steep), RS_SEC_MAX);
    float roundedPi = sqrt(PI * PI + RS_RIB_ROUNDING * RS_RIB_ROUNDING) - RS_RIB_ROUNDING;
    float2 g = g0;
    float2 added = 0.0;
    float depthSum = 0.0;
    float lambda = MatStructureCoarsest;
    for (uint k = 0u; k < MatStructureOctaves; k++, lambda *= 0.5)
    {
        float fade = MatSmoothStep(2.0 * footprint, 4.0 * footprint, lambda);
        if (fade <= 0.0)
            break;
        float len = length(g);
        if (len < 1.0e-4)
            break;
        float2 across = float2(-g.y, g.x) / len;
        float2 z, dRe, dIm;
        float weights;
        float omega = 6.2831853 / lambda;
        GullyPhasor(PcgHash(MatNoiseSeed ^ (0x6A11u + k)), p, omega, RS_CELL * lambda, across, z, dRe, dIm, weights);
        float m2 = dot(z, z);
        if (m2 < 1.0e-12 || weights <= 0.0)
            continue;
        float contrast = MatSmoothStep(RS_CONTRAST_FROM, RS_CONTRAST_TO, sqrt(m2) / weights);
        float phi = atan2(z.y, z.x);
        float2 dPhi = (z.x * dIm - z.y * dRe) / m2;
        // The profile 2t² − 2/3, t = 1 − u/π, u the rounded |φ| (RockStructure.Profile): a rib at φ = 0, a rounded floor at ±π.
        float root = sqrt(phi * phi + RS_RIB_ROUNDING * RS_RIB_ROUNDING);
        float t = 1.0 - (root - RS_RIB_ROUNDING) * (PI / roundedPi) * (1.0 / PI);
        float profileSlope = -4.0 * t * (1.0 / PI) * (phi / root * (PI / roundedPi));
        float amplitude = MatStructureDepth * lambda * sec * slopeFade * contrast * fade;
        float2 grad = (amplitude * profileSlope) * dPhi;
        added += grad;
        g += grad;
        float depth = MatStructureDepth * lambda * contrast * fade;
        floorShare += depth * (1.0 - t) * (1.0 - t);
        depthSum += depth;
    }
    floorShare = depthSum > 0.0 ? floorShare / depthSum : 0.0;
    return added;
}

// One octave's phasor at world point `p` without its gradient (GullyPhasor's sum alone), for the shadows' march.
float2 GullyPhasorValue(uint seed, float2 p, float omega, float cell, float2 across, out float weights)
{
    float2 f = floor(p / cell);
    int2 c = int2(f);
    float2 z = 0.0;
    weights = 0.0;
    float inv = 1.0 / (RS_KERNEL * RS_KERNEL * cell * cell);
    for (int j = -1; j <= 1; j++)
    {
        for (int i = -1; i <= 1; i++)
        {
            uint h = PcgHash(seed ^ PcgHash(asuint(c.x + i) ^ PcgHash(asuint(c.y + j))));
            float2 r = p - (f + float2(i, j) + 0.15 + 0.7 * float2(float(h & 0xFFFFu), float(h >> 16)) * (1.0 / 65536.0)) * cell;
            float k = max(1.0 - dot(r, r) * inv, 0.0);
            float w = k * k * k;
            float s, co;
            sincos(omega * dot(r, across) + float(PcgHash(h) >> 8) * (6.2831853 / 16777216.0), s, co);
            z += w * float2(co, s);
            weights += w;
        }
    }
    return z;
}

// The gullies' height at world point `p` on ground of height gradient `g` (the grid's), their first `octaves` octaves, every octave
// whole, each on the grid's own fall line (RockGullies turns each finer octave with the coarser ones' slope too; the shadows of the
// coarse octaves this marches barely see the difference), for the shadows they cast.
float RockGullyHeight(float2 p, float2 g, uint octaves)
{
    float steep = length(g);
    float slopeFade = MatSmoothStep(RS_SLOPE_FROM, RS_SLOPE_TO, steep);
    if (slopeFade <= 0.0)
        return 0.0;
    float2 across = float2(-g.y, g.x) / steep;
    float scale = MatStructureDepth * min(sqrt(1.0 + steep * steep), RS_SEC_MAX) * slopeFade;
    float roundedPi = sqrt(PI * PI + RS_RIB_ROUNDING * RS_RIB_ROUNDING) - RS_RIB_ROUNDING;
    float height = 0.0;
    float lambda = MatStructureCoarsest;
    for (uint k = 0u; k < octaves; k++, lambda *= 0.5)
    {
        float weights;
        float2 z = GullyPhasorValue(PcgHash(MatNoiseSeed ^ (0x6A11u + k)), p, 6.2831853 / lambda, RS_CELL * lambda, across, weights);
        float m2 = dot(z, z);
        if (m2 < 1.0e-12 || weights <= 0.0)
            continue;
        float phi = atan2(z.y, z.x);
        float t = 1.0 - (sqrt(phi * phi + RS_RIB_ROUNDING * RS_RIB_ROUNDING) - RS_RIB_ROUNDING) * (1.0 / roundedPi);
        height += scale * lambda * MatSmoothStep(RS_CONTRAST_FROM, RS_CONTRAST_TO, sqrt(m2) / weights) * (2.0 * t * t - 2.0 / 3.0);
    }
    return height;
}

// How much of the sun reaches frame point `p` (world point `world`, the world grid's gradient `g0` there) past the gullies' ribs
// toward it (vista-plan §10 M1a.4): the ground toward the sun as the grid's own (its slope integrated along the way) plus the gullies'
// height (their coarse octaves, the grid's own fall line at each step), marched out to a wavelength of the coarsest; where it rises over the sun's ray the
// point lies in a rib's shadow, softened over a few pixels' worth of height. The mesh casts the grid's own shadows; these are the
// shadows of the relief the mesh leaves out. Its cost is most of the structure's, so it marches only the coarse octaves, in steps that
// double, and stops once no rib could reach the sun's ray.
float RockSunShadow(float3 p, float2 world, float2 g0, float footprint)
{
    float sinE = dot(AtmToSun, LocalUp(p));
    float2 toward = AtmToSun.xz;
    float flat = length(toward);
    if (sinE <= 0.0 || flat < 1.0e-4)
        return 1.0;
    toward /= flat;
    float tanE = sinE / max(sqrt(1.0 - sinE * sinE), 1.0e-4);
    // The octaves whose ribs cast shadows wider than a few pixels (a rib's shadow is about as wide as the rib), and the most their
    // height can differ between two places: floor to rib, at the slope here with a margin for steeper places on the way.
    uint octaves = 0u;
    float range = 0.0;
    float steep = length(g0);
    float sec = min(sqrt(1.0 + steep * steep), RS_SEC_MAX);
    uint most = min(MatStructureOctaves, RS_SHADOW_OCTAVES + (footprint < RS_SHADOW_NEAR ? 1u : 0u));
    for (float lambda = MatStructureCoarsest; octaves < most && lambda >= 8.0 * footprint; lambda *= 0.5)
    {
        range += 2.0 * MatStructureDepth * lambda * min(1.5 * sec, RS_SEC_MAX);
        octaves++;
    }
    if (octaves == 0u)
        return 1.0;
    float here = RockGullyHeight(world, g0, octaves);
    float lit = 1.0;
    float r = 0.0, ground = 0.0;
    float2 g = g0;
    for (uint k = 0u; k < RS_SHADOW_STEPS; k++)
    {
        float last = r;
        r = r * 2.0 + 1.5;
        float2 q = world + toward * r;
        float2 m = OnMap(q, Map0Origin, Map0Cell, Map0Size) ? SampleMap0(q) : -g * rsqrt(1.0 + dot(g, g));
        float2 gq = -m / max(sqrt(max(1.0 - dot(m, m), 0.0)), 0.05);
        // The grid's own ground toward the sun, its slope integrated along the way (the trapezoid rule between the steps).
        ground += (r - last) * 0.5 * dot(g + gq, toward);
        g = gq;
        float rise = ground + RockGullyHeight(q, gq, octaves) - here - r * tanE;
        float soft = 0.02 * r + footprint;
        lit = min(lit, MatSmoothStep(soft, -soft, rise));
        // Done once the ray stands higher over the ground than any rib can: farther out only the ground itself could rise to it,
        // and the mesh casts that shadow.
        if (lit <= 0.0 || r > MatStructureCoarsest || r * tanE - ground > range + soft)
            break;
    }
    return lit;
}

// One entry of the ledge table that follows the beds in BedTable, four entries a float4.
float LedgeEntry(uint base, uint e)
{
    float4 v = BedTable[base + (e >> 2)];
    uint k = e & 3u;
    return k == 0u ? v.x : k == 1u ? v.y : k == 2u ? v.z : v.w;
}

// The ledges' height at bed-table coordinate t (m) and its slope by the coordinate (m per entry): a Catmull–Rom spline through the
// ledge table (RockStructure.LedgeAt).
float2 LedgeAt(float t)
{
    uint entries = uint(MatTableLast) + 2u;
    t = clamp(t, 1.0, float(entries) - 3.0);
    uint e = uint(floor(t));
    float f = t - float(e);
    float p0 = LedgeEntry(entries, e - 1u), p1 = LedgeEntry(entries, e), p2 = LedgeEntry(entries, e + 1u), p3 = LedgeEntry(entries, e + 2u);
    float a = -p0 + p2, b = 2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3, c = -p0 + 3.0 * p1 - 3.0 * p2 + p3;
    return float2(0.5 * (2.0 * p1 + f * (a + f * (b + f * c))), 0.5 * (a + f * (2.0 * b + 3.0 * f * c)));
}

// The ledges and cliff bands the beds' hardness makes at world point `world`, on ground of height gradient `g` at bed-table
// coordinate `t` (MatSample.Table): the gradient of the ledges' height (1 − massive)·G(t), with ∇t = TableScale·∇h + ∇offset.
float2 RockLedges(float2 world, float2 g, float t, float massive)
{
    float layered = 1.0 - massive;
    if (layered <= 0.0 || MatMapSize < 2u)
        return 0.0;
    float slope = LedgeAt(t).y * layered;
    // The bed-table offset's gradient from the material map's four samples around the point.
    float2 uv = clamp((world - float2(MatMapOriginX, MatMapOriginZ)) / MatMapCell, 0.0, float(MatMapSize - 1u));
    uint2 c = min(uint2(uv), uint2(MatMapSize - 2u, MatMapSize - 2u));
    float2 f = uv - float2(c);
    uint k = c.y * MatMapSize + c.x;
    float o00 = asfloat(MaterialMap[k].x), o10 = asfloat(MaterialMap[k + 1u].x);
    float o01 = asfloat(MaterialMap[k + MatMapSize].x), o11 = asfloat(MaterialMap[k + MatMapSize + 1u].x);
    float2 dOffset = float2((o10 - o00) * (1.0 - f.y) + (o11 - o01) * f.y, (o01 - o00) * (1.0 - f.x) + (o11 - o10) * f.x) / MatMapCell;
    return slope * (MatTableScale * g + dOffset);
}

// The normal `n` (y up) with the height gradient `added` added to the ground's.
float3 AddGradient(float3 n, float2 added)
{
    float2 g = float2(-n.x, -n.z) / max(n.y, 0.05) + added;
    return normalize(float3(-g.x, 1.0, -g.y));
}

// The jointed blocks of bare rock at world point `world` (M1a.4): the rock breaks along its bedding (the laminae, coordinate
// `lamina`, each `laminaMetres` thick) and across it along two sets of vertical joints, at right angles and turning slowly over the
// world, each about RS_JOINT_SPACING apart and staggered from one bed to the next (a joint ends at a bedding plane, as in real rock);
// a second, finer breaking splits each block in two along each set. Each block's face turns its own way (a facet: its tilt in the
// rock's tangent plane, constant over the block, so the face shades flat with a sharp edge), and an open joint is dark. Returns the
// tilt (two tangent coordinates) and, in `crack`, how far the point lies in an open joint (0 none, 1 in it); faded where a pixel
// covers more than a quarter of a block.
float2 RockJoints(float2 world, float lamina, float laminaMetres, float footprint, out float crack)
{
    crack = 0.0;
    float fade = MatSmoothStep(2.0 * footprint, 4.0 * footprint, RS_JOINT_SPACING);
    if (fade <= 0.0)
        return 0.0;
    float turn = PI * MaterialNoise(PcgHash(MatNoiseSeed ^ 0x101Fu), world.x / 1500.0, world.y / 1500.0);
    float2 a = float2(cos(turn), sin(turn));
    float2 b = float2(-a.y, a.x);
    int bed = int(floor(lamina));
    uint bedHash = PcgHash(asuint(bed) ^ PcgHash(MatNoiseSeed ^ 0x7017u));
    float u = dot(world, a) / RS_JOINT_SPACING + float(bedHash & 0xFFFFu) * (1.0 / 65536.0);
    int i = int(floor(u));
    uint rowHash = PcgHash(bedHash ^ asuint(i));
    float v = dot(world, b) / (1.4 * RS_JOINT_SPACING) + float(rowHash & 0xFFFFu) * (1.0 / 65536.0);
    int j = int(floor(v));
    uint block = PcgHash(rowHash ^ PcgHash(asuint(j)));
    // The finer breaking: each block split along each set where its own hash says, a half block apart.
    uint halves = (uint(frac(u) * 2.0) & 1u) | ((uint(frac(v) * 2.0) & 1u) << 1);
    uint piece = PcgHash(block ^ (halves + 1u));
    float2 tilt = (float2(float(block & 0xFFFFu), float(block >> 16)) * (2.0 / 65536.0) - 1.0)
        + 0.5 * (float2(float(piece & 0xFFFFu), float(piece >> 16)) * (2.0 / 65536.0) - 1.0)
          * MatSmoothStep(2.0 * footprint, 4.0 * footprint, 0.5 * RS_JOINT_SPACING);
    // How close the point lies to a joint, m (the bedding planes are the laminae's steps).
    float du = min(frac(u), 1.0 - frac(u)) * RS_JOINT_SPACING;
    float dv = min(frac(v), 1.0 - frac(v)) * 1.4 * RS_JOINT_SPACING;
    float open = RS_JOINT_CRACK * (0.5 + float(block >> 28) * (1.0 / 16.0));
    crack = fade * (1.0 - MatSmoothStep(0.5 * open, open + 0.5 * footprint, min(du, dv)));
    return fade * RS_JOINT_TILT * tilt;
}

// The shading normal `ns` at frame point `p` (world offset `o` from the camera, height `height`, world point `world`) with the bump of
// weights `w` and the laminae's steps of a rock of hardness colour `hs`, for a camera hit whose pixel covers `footprint` metres.
// `shade` is how much the gaps between scree's stones and the rock's open joints darken it there.
float3 MaterialBump(float3 ns, float3 wo, float2 o, float2 world, float height, MatWeights w, float hs, float lamina, float laminaMetres, float footprint,
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
    float smoothRough = w.Soil * MatRoughSoil + (w.Snow + w.Firn + w.Ice) * MatRoughSnow;
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
    // Bare rock's jointed blocks (M1a.4), each a facet in the rock's own tangent plane, more on hard rock.
    if (MatStructure != 0u && w.Rock > 0.01)
    {
        float crack;
        float2 tilt = RockJoints(world, lamina, laminaMetres, footprint, crack);
        float blocky = w.Rock * (0.4 + 0.6 * hs);
        float3 side = abs(n.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
        float3 t1 = normalize(cross(n, side));
        float3 t2 = cross(n, t1);
        gradient -= blocky * (tilt.x * t1 + tilt.y * t2);
        shade *= 1.0 - blocky * RS_JOINT_DARK * crack;
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
    float3 gridNormal = GroundNormal(world, p, ns);
    // The rock's own shadows (M1a.4) first, while little else is live: the march is the structure's cost, and most of that cost is
    // the registers it holds while the rest of the shading holds its own.
    float sunShadow = 1.0;
    if (MatStructure != 0u && footprint > 0.0 && SunUp != 0u && dot(ns, AtmToSun) > 0.0)
        sunShadow = RockSunShadow(p, world, -gridNormal.xz / max(gridNormal.y, 0.05), footprint / max(abs(dot(ns, wo)), 0.15));
    MatWeights w = MaterialWeightsAt(world, height, gridNormal, s, slope);

    // The rock's structure (M1a.4) on a camera hit: gullies and ribs down the fall line, then the beds' ledges on that ground, by
    // the rules' weight of rock.
    float floorShare = 0.0;
    g.Sun = 1.0;
    if (MatStructure != 0u && footprint > 0.0 && w.Rock > 0.0)
    {
        float along = footprint / max(abs(dot(ns, wo)), 0.15);
        float2 g0 = -gridNormal.xz / max(gridNormal.y, 0.05);
        float2 gullies = RockGullies(world, g0, along, floorShare);
        float2 here = -ns.xz / max(ns.y, 0.05) + gullies;
        float2 ledges = RockLedges(world, here, s.Table, s.Massive);
        ns = AddGradient(ns, w.Rock * (gullies + ledges));
        g.Sun = 1.0 - w.Rock * (1.0 - sunShadow);
        // Debris gathers in the gullies' floors where the face is gentle enough to hold it (less than about 45°).
        float debris = w.Rock * 0.8 * MatSmoothStep(0.55, 0.9, floorShare) * (1.0 - MatSmoothStep(38.0, 52.0, slope));
        w.Rock -= debris;
        w.Scree += debris;
    }

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
    // Snow (M1.13) drifts against the ground's bumps (the fine noise pushes it into the low spots), and its edge is as sharp.
    float3 white = 0.0;
    if (w.Snow + w.Firn + w.Ice > 0.0)
    {
        white = max(float3(w.Snow * (1.0 - 0.6 * fine), w.Firn * (1.0 - 0.3 * fine), w.Ice), 0.0);
        white *= white * white;
        white *= white;
    }
    float total = max(sharp.x + sharp.y + sharp.z + sharp.w + forestSharp + white.x + white.y + white.z, 1.0e-12);
    sharp /= total;
    w.Rock = sharp.x;
    w.Scree = sharp.y;
    w.Soil = sharp.z;
    w.Grass = sharp.w;
    w.Forest = forestSharp / total;
    w.Snow = white.x / total;
    w.Firn = white.y / total;
    w.Ice = white.z / total;
    float dryGrass = 0.4 * MatSmoothStep(-0.1, 0.7, MaterialNoise(PcgHash(MatNoiseSeed ^ 0xD27u), world.x / 7.0, world.y / 7.0));

    // The rock of the bed the hit cuts: soft mixed into hard by its hardness, tinted by the bed, each lamina (thicker or thinner by
    // bed) a little lighter or darker, fading where a pixel covers more than half a lamina, and where the rock is massive (M1.12c).
    float hs = MatSmoothStep(MatHardColourMid - MatHardColourWidth, MatHardColourMid + MatHardColourWidth, s.Hardness);
    float4 lambda = float4(Cie[idx.x].w, Cie[idx.y].w, Cie[idx.z].w, Cie[idx.w].w);
    float laminaePerEntry = MatLaminaePerEntry * (1.0 + 0.4 * s.TintU);
    float lamina = s.Table * laminaePerEntry + 0.5 * s.TintV;
    float laminaMetres = 1.0 / max(laminaePerEntry * MatTableScale, 1.0e-4);
    float shade = float(PcgHash(asuint(int(floor(lamina))) ^ MatNoiseSeed) >> 8) * (2.0 / 16777216.0) - 1.0;
    float bedded = 1.0 - s.Massive;
    float layered = 1.0 + MatLaminaContrast * bedded * shade * (footprint > 0.0 ? MatSmoothStep(laminaMetres, 0.5 * laminaMetres, footprint) : 0.0);
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
        float dust = 0.0;
        if (w.Dusting > 0.0)
        {
            // Snow in the gaps between the crowns (the 4 m noise's low spots), at most the dusting's share of the forest's texture.
            float gap = MatSmoothStep(0.1, -0.5, MaterialNoise(PcgHash(MatNoiseSeed ^ 0xF0E5u), world.x / 4.0, world.y / 4.0));
            dust = w.Dusting * gap;
            float4 dusted = lerp(MaterialReflectance(MAT_SNOW, idx), MaterialReflectance(MAT_SNOW_WET, idx), w.SnowWet) * (dust * w.Forest);
            forest *= 1.0 - dust;
            g.Refl = saturate(g.Refl + dusted);
            g.Albedo += lerp(MaterialAlbedo[MAT_SNOW].xyz, MaterialAlbedo[MAT_SNOW_WET].xyz, w.SnowWet) * (dust * w.Forest);
        }
        bool kernels = MatDiffuse != 0u && MatCanopy != 0u;
        float sForest;
        float aForest = MatRoughA(MAT_FOREST, sForest);
        g.Refl = saturate(g.Refl + forest * (kernels ? MatCanopyIso : aForest));
        g.Back = saturate(g.Back + forest * (kernels ? 0.0 : aForest * sForest));
        g.Canopy = kernels ? saturate(forest * MatCanopyIso) : 0.0;
        g.Albedo += MaterialAlbedo[MAT_FOREST].xyz * (forestTone * w.Forest * dry * (1.0 - dust));
    }

    // Snow, firn and glacier ice (M1.13), Lambertian at their own roughness (MatRoughA: 0 for snow); the season's snow wet or dry,
    // the bare ice grey with debris where it thins toward its edges and snout.
    if (w.Snow + w.Firn + w.Ice > 0.0)
    {
        float sSnow, sIce, sFirn;
        float aSnow = MatRoughA(MAT_SNOW, sSnow), aIce = MatRoughA(MAT_ICE, sIce), aFirn = MatRoughA(MAT_FIRN, sFirn);
        float4 snowRefl = lerp(MaterialReflectance(MAT_SNOW, idx), MaterialReflectance(MAT_SNOW_WET, idx), w.SnowWet);
        float debris = 1.0 - MatSmoothStep(0.0, MatDebris, s.Ice);
        float4 iceRefl = lerp(MaterialReflectance(MAT_ICE, idx), scree, debris);
        // Crevasses: where a glacier flows over steeper ground its surface stretches and cracks across the flow, in fields of
        // fissures some 30 m apart and a few metres wide whose depths hold blue shadow. A fissure darkens what lies there to a fifth;
        // where a pixel spans more than a quarter of their spacing (or on a path's later surfaces) the field shows as its mean tone.
        float crevasse = 0.0;
        float2 downhill = gridNormal.xz;
        if (w.Firn + w.Ice > 0.0 && dot(downhill, downhill) > 1.0e-6)
        {
            downhill = normalize(downhill);
            float field = MatSmoothStep(8.0, 14.0, slope) * (1.0 - MatSmoothStep(32.0, 40.0, slope));
            float along = dot(world, downhill) / 30.0 + 0.8 * MaterialNoise(PcgHash(MatNoiseSeed ^ 0xC2E1u), world.x / 90.0, world.y / 90.0);
            float across = dot(world, float2(-downhill.y, downhill.x)) / 45.0;
            float broken = MatSmoothStep(-0.25, 0.15, MaterialNoise(PcgHash(MatNoiseSeed ^ 0xC2E2u), across, along * 0.7));
            float resolved = footprint > 0.0 ? 1.0 - MatSmoothStep(3.0, 7.5, footprint) : 0.0;
            float fissure = MatSmoothStep(0.86, 0.9, frac(along)) * (1.0 - MatSmoothStep(0.97, 0.995, frac(along)));
            crevasse = field * broken * lerp(0.1, fissure, resolved);
        }
        float open = 1.0 - 0.8 * crevasse;
        g.Refl = saturate(g.Refl + w.Snow * aSnow * snowRefl + (w.Firn * aFirn * MaterialReflectance(MAT_FIRN, idx) + w.Ice * aIce * iceRefl) * open);
        g.Back = saturate(g.Back + w.Snow * (aSnow * sSnow) * snowRefl + (w.Firn * (aFirn * sFirn) * MaterialReflectance(MAT_FIRN, idx)
            + w.Ice * (aIce * sIce) * iceRefl) * open);
        g.Albedo += w.Snow * lerp(MaterialAlbedo[MAT_SNOW].xyz, MaterialAlbedo[MAT_SNOW_WET].xyz, w.SnowWet) + (w.Firn * MaterialAlbedo[MAT_FIRN].xyz
            + w.Ice * lerp(MaterialAlbedo[MAT_ICE].xyz, MaterialAlbedo[MAT_SCREE].xyz, debris)) * open;
    }

    float gaps = 1.0;
    g.Normal = footprint > 0.0 ? MaterialBump(ns, wo, offset, world, height, w, hs * bedded, lamina, laminaMetres, footprint, gaps) : ns;
    g.Refl *= gaps;
    g.Back *= gaps;
    g.Canopy *= gaps;
    g.Albedo *= gaps;

    uint id = s.Hardness >= 0.5 ? MAT_ROCK_HARD : MAT_ROCK_SOFT;
    float best = w.Rock;
    if (w.Scree > best) { best = w.Scree; id = MAT_SCREE; }
    if (w.Soil > best) { best = w.Soil; id = MAT_SOIL; }
    if (w.Grass > best) { best = w.Grass; id = MAT_GRASS; }
    if (w.Forest > best) { best = w.Forest; id = MAT_FOREST; }
    if (w.Snow > best) { best = w.Snow; id = MAT_SNOW; }
    if (w.Firn > best) { best = w.Firn; id = MAT_FIRN; }
    if (w.Ice > best) { id = MAT_ICE; }
    g.Id = id;
    return g;
}
