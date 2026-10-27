// Vista's spectral atmosphere (vista-plan §5.2, §10 M1.3), shared by the LUT builders (VistaAtmosphereLuts.hlsl)
// and the integrator (VistaIntegrator.hlsl). Every function here is the float twin of a C# reference in
// Vista.Atmosphere.AtmosphereLuts — same formulas, same order — so --gpu-selftest can hold each GPU LUT against
// it. Sphere geometry is written in the stable forms that keep a 6 360 km radius from swallowing metres in float:
// altitudes are carried explicitly and radii are never subtracted.
//
// Frame: one local vertical (+Y) for the whole scene. A point is described by its altitude; its sun angle and
// view angle are cosines to that vertical. The ground sphere sits at the scene's base altitude.

#define ATM_WAVELENGTHS          50u
#define COLUMNS_MU_SIZE          256u
#define COLUMNS_ALTITUDE_SIZE    256u
#define SCATTER_HEIGHT_SIZE      24u
#define SCATTER_MU_HALF_SIZE     32u
#define SCATTER_MU_SIZE          64u
#define SCATTER_MU_S_SIZE        64u
#define SCATTER_NU_SIZE          12u
#define SCATTER_MU_S_MIN         -0.5
#define SCATTER_MS_WAVELENGTHS   11u
#define SCATTER_MIN_HEIGHT       1.0
#define GROUND_IRRADIANCE_SIZE   128u
#define SKY_AZIMUTH_SIZE         64u
#define SKY_ROWS_ABOVE           64u
#define SKY_ROWS_BELOW           32u
#define SKY_ROWS                 96u
#define SKY_TERRAIN_SLICES       12u
#define SKY_SLICES               13u
#define ATM_PI                   3.14159265358979
#define MIN_HEIGHT_ABOVE_GROUND  1.0

cbuffer AtmosphereConstants : register(b1)
{
    float  SeaRadius;            float  GroundAltitude;      float TopAltitude;          float GroundAlbedo;
    float  RayleighScaleHeight;  float  AerosolScaleHeight;  float OzonePeak;            float OzoneHalfWidth;
    float3 AtmToSun;             float  MieG;
    float  CameraAltitude;       float  TerrainLowAltitude;  float TerrainHighAltitude;  float AtmSunOneMinusCos;
    float  ValleyHazeTop;        float  ValleyHazeSoftness;  float ValleyHazeDensity;    float HazeScale;
    // The valleys' pools in the low air (vista-plan §10 M1.13b; VistaLowAir.hlsli): with LowMode 1 the march takes them in three
    // dimensions and leaves the tables' flat layer (their altitude average) out.
    uint   LowMode;              uint   LowLevels;           uint  LowNoiseSeed;         uint  LowVolumeLayers;
    float4 LowLevel0;            // the world's level: origin x, origin z, cell, size
    float4 LowLevel1;            // the far field's
    uint   LowLevel1Offset;      uint   LowVolume0Offset;    uint  LowVolume1Offset;     uint  LowVolumeRatio;
    float  LowSlabTop;           float  LowSlabBottom;       float LowHazeMax;           float LowMistMax;
    float  LowBlendMetres;       float  LowHazeSoftness;     float LowVolumeMargin;      float MistG;
    float  LowWispAmplitude;     float  LowWispScale;        float LowWispVertical;      float LowPatchStrength;
    float  LowPatchScale;        float  MistWaterLimit;      float MistAlbedo;           float LowMarchStep;
    float  LowMsA;               float  LowMsB;              float LowMsC;               uint  LowMsOctaves;
    uint   LowMarchMaxSteps;     float  LowAmbient;          uint  LowCeilingOffset;     uint  LowCeilingBlock;
    float  LowMarchGrowth;       uint   LowBounceSteps;      float LowBounceStep;        float LowMistSoftness;
    float  LowMsForward;         float  LowMsForwardPower;   float LowMsDiffuse;         float LowMsDiffusePower;
    uint   LowSkySteps;          float  LowSkyGrowth;        float LowMistStep;          float LowMistBandStep;
    float  LowFray;              float  LowShadowedMs;       uint  LowVolumePoolLayers;  float LowPad8;
};

StructuredBuffer<float4> AtmCoefficients : register(t8);   // per wavelength: Rayleigh, haze scattering, haze extinction, ozone
StructuredBuffer<float4> AtmSun          : register(t9);   // per wavelength: disk radiance, top-of-atmosphere irradiance, ground albedo, 0
StructuredBuffer<float4> ColumnsLut      : register(t10);  // density columns to the top: Rayleigh, haze, ozone, 0
StructuredBuffer<float2> ScatterSingle   : register(t11);  // 4D single scattering without phases (Rayleigh, haze)
StructuredBuffer<float>  ScatterOrder    : register(t12);  // 4D radiance of the previous order
StructuredBuffer<float2> ScatterWeighted : register(t13);  // 4D phase-weighted radiance (Rayleigh, haze): one order, or orders 2… summed (the integrator's aerial perspective reads the sum)
StructuredBuffer<float>  IrradianceOrder : register(t15);  // the ground's irradiance of one order [entry · 11 + l]
StructuredBuffer<float>  GroundIrrLut    : register(t16);  // sky irradiance on the ground [entry · 50 + λ]
StructuredBuffer<float2> SkyViewLut      : register(t17);  // [((slice · rows + row) · azimuths + column) · 50 + λ] = (smooth, haze)

float GroundRadius() { return SeaRadius + GroundAltitude; }

// ── Geometry on spheres ───────────────────────────────────────────────────────────────────────────

float AltitudeAlong(float radius, float h0, float mu, float t)
{
    float q = h0 * (2.0 * radius + h0) + t * (t + 2.0 * (radius + h0) * mu);
    return q / (radius + sqrt(max(radius * radius + q, 0.0)));
}

bool HitsSphere(float radius, float h, float mu)
{
    float rmu = (radius + h) * mu;
    return mu < 0.0 && rmu * rmu >= h * (2.0 * radius + h);
}

float DistanceToSphereBelow(float radius, float h, float mu)
{
    float rmu = (radius + h) * mu;
    float c = h * (2.0 * radius + h);
    float denominator = -rmu + sqrt(max(rmu * rmu - c, 0.0));
    return denominator > 0.0 ? c / denominator : 0.0;
}

float DistanceToShellAbove(float radius, float h, float hTop, float mu)
{
    float rmu = (radius + h) * mu;
    float gap = (hTop - h) * (2.0 * radius + hTop + h);
    float root = sqrt(max(gap + rmu * rmu, 0.0));
    float d = rmu >= 0.0 ? (rmu + root > 0.0 ? gap / (rmu + root) : 0.0) : root - rmu;
    return max(d, 0.0);
}

// ── Densities and coefficients ────────────────────────────────────────────────────────────────────

float RayleighDensity(float altitude) { return exp(-altitude / RayleighScaleHeight); }
float OzoneDensity(float altitude)    { return max(0.0, 1.0 - abs(altitude - OzonePeak) / OzoneHalfWidth); }

// The valley haze (vista-plan §10 M1.13a; C# reference: AtmosphereModel.ValleyHaze, ValleyHazeMean): more of the haze under
// an inversion's logistic lid, its density in the haze's unit. A march takes its exact mean over each step, never a sample:
// its lid is sharper than any march's steps.
float Softplus(float x) { return max(x, 0.0) + log(1.0 + exp(-abs(x))); }

float ValleyHaze(float altitude)
{
    return ValleyHazeDensity > 0.0 ? ValleyHazeDensity / (1.0 + exp((altitude - ValleyHazeTop) / ValleyHazeSoftness)) : 0.0;
}

float ValleyHazeMean(float a, float b)
{
    float xa = (ValleyHazeTop - a) / ValleyHazeSoftness, xb = (ValleyHazeTop - b) / ValleyHazeSoftness;
    float dx = xa - xb;
    if (abs(dx) < 1.0e-3)
        return ValleyHazeDensity / (1.0 + exp(-0.5 * (xa + xb)));
    return ValleyHazeDensity * (Softplus(xa) - Softplus(xb)) / dx;
}

float AerosolDensity(float altitude) { return HazeScale * exp(-altitude / AerosolScaleHeight) + ValleyHaze(altitude); }

// The valley haze's column from `altitude` straight up, m of the haze's unit (C# reference: AtmosphereModel.ValleyHazeColumn to the top).
float ValleyHazeAbove(float altitude)
{
    return ValleyHazeDensity > 0.0 ? ValleyHazeDensity * ValleyHazeSoftness * Softplus((ValleyHazeTop - altitude) / ValleyHazeSoftness) : 0.0;
}

// The haze over one step whose altitudes are start, middle and end: its own profile at the middle, the valley haze's mean
// along the two halves (C# reference: AtmosphereModel.AerosolStepDensity).
float AerosolStepDensity(float start, float middle, float end)
{
    float own = HazeScale * exp(-middle / AerosolScaleHeight);
    return ValleyHazeDensity > 0.0 ? own + 0.5 * (ValleyHazeMean(start, middle) + ValleyHazeMean(middle, end)) : own;
}

// (total scattering, extinction, Rayleigh scattering, haze scattering) per metre at an altitude above sea level, the haze's
// relative density given (a step's, from AerosolStepDensity).
float4 MediumWith(float4 k, float altitude, float aerosol)
{
    float rayleigh = k.x * RayleighDensity(altitude);
    float mie = k.y * aerosol;
    float extinction = rayleigh + k.z * aerosol + k.w * OzoneDensity(altitude);
    return float4(rayleigh + mie, extinction, rayleigh, mie);
}

// The same at a point.
float4 MediumAt(float4 k, float altitude) { return MediumWith(k, altitude, AerosolDensity(altitude)); }

float OpticalDepth(float4 k, float3 columns) { return k.x * columns.x + k.z * columns.y + k.w * columns.z; }

// Step s of `steps` along a ray: (midpoint, length), growing quadratically away from the dense end — the start, or
// the ground for a ray that ends there (C# reference: AtmosphereLuts.Segment).
float2 SegmentEnds(float pathLength, uint s, uint steps, bool denseAtEnd)
{
    float u0 = float(s) / float(steps), u1 = float(s + 1u) / float(steps);
    float t0 = pathLength * (denseAtEnd ? 1.0 - (1.0 - u0) * (1.0 - u0) : u0 * u0);
    float t1 = pathLength * (denseAtEnd ? 1.0 - (1.0 - u1) * (1.0 - u1) : u1 * u1);
    return float2(t0, t1);
}

float2 Segment(float pathLength, uint s, uint steps, bool denseAtEnd)
{
    float2 ends = SegmentEnds(pathLength, s, steps, denseAtEnd);
    return float2(0.5 * (ends.x + ends.y), ends.y - ends.x);
}

// ∫₀^dt e^(−σt) dt = dt·(1 − e^(−x))/x with x = σ·dt; below x = 0.01 by its series, because 1 − e^(−x) cancels in float.
float StepIntegral(float extinction, float dt)
{
    float x = extinction * dt;
    return x < 0.01 ? dt * (1.0 - x * (0.5 - x * (1.0 / 6.0))) : (1.0 - exp(-x)) / extinction;
}

float RayleighPhase(float cosAngle) { return 3.0 / (16.0 * ATM_PI) * (1.0 + cosAngle * cosAngle); }

float HenyeyGreenstein(float cosAngle, float g)
{
    float denominator = 1.0 + g * g - 2.0 * g * cosAngle;
    return (1.0 - g * g) / (4.0 * ATM_PI * denominator * sqrt(denominator));
}

// Vertex-centred cell: texels i0, i0 + 1 around x ∈ [0, 1] and the weight of the second.
void Cell(float x, uint size, out uint i0, out uint i1, out float f)
{
    float s = saturate(x) * float(size - 1u);
    i0 = min(uint(floor(s)), size - 2u);
    i1 = i0 + 1u;
    f = s - float(i0);
}

// ── Columns LUT ───────────────────────────────────────────────────────────────────────────────────

float2 ColumnsCoordinates(float altitude, float mu)
{
    float h = clamp(altitude, 0.0, TopAltitude);
    float bigH = sqrt(TopAltitude * (2.0 * SeaRadius + TopAltitude));
    float rho = sqrt(h * (2.0 * SeaRadius + h));
    float d = DistanceToShellAbove(SeaRadius, h, TopAltitude, mu);
    float dMin = TopAltitude - h;
    float dMax = rho + bigH;
    float xMu = dMax > dMin ? (d - dMin) / (dMax - dMin) : 0.0;
    return saturate(float2(xMu, rho / bigH));
}

float3 SampleColumns(float altitude, float mu)
{
    float2 x = ColumnsCoordinates(altitude, mu);
    uint i0, i1, j0, j1;
    float fi, fj;
    Cell(x.x, COLUMNS_MU_SIZE, i0, i1, fi);
    Cell(x.y, COLUMNS_ALTITUDE_SIZE, j0, j1, fj);
    float3 low = lerp(ColumnsLut[j0 * COLUMNS_MU_SIZE + i0].xyz, ColumnsLut[j0 * COLUMNS_MU_SIZE + i1].xyz, fi);
    float3 high = lerp(ColumnsLut[j1 * COLUMNS_MU_SIZE + i0].xyz, ColumnsLut[j1 * COLUMNS_MU_SIZE + i1].xyz, fi);
    return lerp(low, high, fj);
}

// The fraction of the sun's disk above the ground sphere's horizon, seen from `height` above it with the disk's centre at
// cos = muS to the vertical: the Earth's shadow with its penumbra. The disk is 0.53° across, so near the terminator the
// penumbra is kilometres deep (2.9–4.9 km up with the sun 2° down). `sunMu` is the direction to take its light along:
// muS, or the horizon itself while the centre is below it (the visible upper limb grazes the ground). C# reference:
// AtmosphereLuts.SunVisibility.
float SunVisibility(float height, float muS, out float sunMu)
{
    sunMu = muS;
    float radius = 2.0 * asin(sqrt(0.5 * AtmSunOneMinusCos));
    if (muS >= sin(radius))
        return 1.0; // the whole disk is above the horizontal, so above any horizon
    float rb = GroundRadius();
    float h = max(height, 0.0);
    float dip = atan2(sqrt(h * (2.0 * rb + h)), rb);
    float above = asin(clamp(muS, -1.0, 1.0)) + dip; // the centre's elevation above the horizon, radians
    float y = clamp(above / radius, -1.0, 1.0);
    sunMu = max(muS, -sin(dip) + 1.0e-5);
    return 0.5 + (y * sqrt(1.0 - y * y) + asin(y)) / ATM_PI;
}

// Sunlight transmittance from an altitude above sea level along a direction at cos = mu to the vertical, for one
// wavelength's coefficients; zero when the ground sphere is in the way (the Earth's shadow).
float SunTransmittance(float4 k, float altitude, float mu)
{
    if (HitsSphere(GroundRadius(), altitude - GroundAltitude, mu))
        return 0.0;
    return exp(-OpticalDepth(k, SampleColumns(altitude, mu)));
}

// The same for four wavelengths at once (one columns lookup).
float4 SunTransmittance4(uint4 idx, float altitude, float mu)
{
    if (HitsSphere(GroundRadius(), altitude - GroundAltitude, mu))
        return 0.0;
    float3 c = SampleColumns(altitude, mu);
    return exp(-float4(OpticalDepth(AtmCoefficients[idx.x], c), OpticalDepth(AtmCoefficients[idx.y], c),
                       OpticalDepth(AtmCoefficients[idx.z], c), OpticalDepth(AtmCoefficients[idx.w], c)));
}

// ── 4D scattering orders (C# reference: Vista.Atmosphere.AtmosphereScattering) ────────────────────

// The tables hold 11 of the 50 hero wavelengths: every fifth, and the last.
uint HeroOf(uint l) { return l == SCATTER_MS_WAVELENGTHS - 1u ? ATM_WAVELENGTHS - 1u : 5u * l; }

struct ScatterPoint
{
    float Height; // above the ground sphere
    float Mu;     // view cosine to the vertical
    float MuS;    // sun cosine to the vertical
    float Nu;     // view-to-sun cosine
    bool  Ground; // the view ray ends on the ground
};

struct ScatterCell
{
    uint  H0, Mu0, MuS0, Nu0;
    float Fh, Fmu, Fmus, Fnu;
};

float ScatterTop() { return SeaRadius + TopAltitude - GroundRadius(); }

float ScatterBigH()
{
    float top = ScatterTop();
    return sqrt(top * (2.0 * GroundRadius() + top));
}

float ClampNu(float mu, float muS, float nu)
{
    float spread = sqrt(max((1.0 - mu * mu) * (1.0 - muS * muS), 0.0));
    return clamp(nu, mu * muS - spread, mu * muS + spread);
}

float MuSFromUnit(float x)
{
    float rb = GroundRadius();
    float top = ScatterTop();
    float bigH = ScatterBigH();
    float dMin = top, dMax = bigH;
    float bigA = (DistanceToShellAbove(rb, 0.0, top, SCATTER_MU_S_MIN) - dMin) / (dMax - dMin);
    float a = (bigA - x * bigA) / (1.0 + x * bigA);
    float d = dMin + min(a, bigA) * (dMax - dMin);
    return d <= 0.0 ? 1.0 : clamp((bigH * bigH - d * d) / (2.0 * rb * d), -1.0, 1.0);
}

float MuSToUnit(float muS)
{
    float rb = GroundRadius();
    float top = ScatterTop();
    float bigH = ScatterBigH();
    float dMin = top, dMax = bigH;
    float a = (DistanceToShellAbove(rb, 0.0, top, muS) - dMin) / (dMax - dMin);
    float bigA = (DistanceToShellAbove(rb, 0.0, top, SCATTER_MU_S_MIN) - dMin) / (dMax - dMin);
    return saturate(max(1.0 - a / bigA, 0.0) / (1.0 + a));
}

// ── Ground irradiance tables ──────────────────────────────────────────────────────────────────────

// A ground-irradiance table with `stride` wavelengths per entry (50 for the sky's, 11 for one order's). Its entries sit
// on the 4D tables' sun map (MuSFromUnit), dense near the horizon, where twilight irradiance changes several-fold per
// degree; evenly spaced cosines, 0.9° apart there, overshot it (vista-plan §10 M1.4). Below the map's lowest sun the
// first entry, already dark, stands.
float GroundIrradianceMuS(uint entry) { return MuSFromUnit(float(entry) / float(GROUND_IRRADIANCE_SIZE - 1u)); }

float SampleIrradianceTable(StructuredBuffer<float> table, uint stride, uint lambda, float muS)
{
    uint i0, i1;
    float f;
    Cell(MuSToUnit(muS), GROUND_IRRADIANCE_SIZE, i0, i1, f);
    return lerp(table[i0 * stride + lambda], table[i1 * stride + lambda], f);
}

float SampleGroundIrradiance(uint lambda, float muS) { return SampleIrradianceTable(GroundIrrLut, ATM_WAVELENGTHS, lambda, muS); }

// (zenith angle of the horizon, angle from the horizon to the nadir) at a height above the ground sphere.
float2 Horizon(float groundRadius, float height)
{
    float dip = atan2(sqrt(max(height * (2.0 * groundRadius + height), 0.0)), groundRadius);
    return float2(0.5 * ATM_PI + dip, 0.5 * ATM_PI - dip);
}

ScatterPoint TexelToScatterPoint(uint ih, uint imu, uint imus, uint inu)
{
    float rb = GroundRadius();
    float rho = ScatterBigH() * float(ih) / float(SCATTER_HEIGHT_SIZE - 1u);
    float h = max(rho * rho / (rb + sqrt(rb * rb + rho * rho)), SCATTER_MIN_HEIGHT);

    // The view angle: split at the ground sphere's horizon, each half dense toward it (the sky view's rows). Bruneton's
    // map by distance put every texel of the ground half within 0.5° of the horizon near the ground, where segments of
    // aerial perspective end (vista-plan §10 M1.4).
    ScatterPoint p;
    p.Ground = imu >= SCATTER_MU_HALF_SIZE;
    float x = float(p.Ground ? imu - SCATTER_MU_HALF_SIZE : imu) / float(SCATTER_MU_HALF_SIZE - 1u);
    float2 horizon = Horizon(rb, h);
    p.Mu = cos(p.Ground ? horizon.x + horizon.y * x * x : horizon.x * (1.0 - (1.0 - x) * (1.0 - x)));
    p.Height = h;
    p.MuS = MuSFromUnit(float(imus) / float(SCATTER_MU_S_SIZE - 1u));
    p.Nu = ClampNu(p.Mu, p.MuS, clamp(2.0 * float(inu) / float(SCATTER_NU_SIZE - 1u) - 1.0, -1.0, 1.0));
    return p;
}

// The cell, axis by axis, so loops can keep what does not change (CellOf composes them).
void CellHeight(float height, out uint h0, out float fh)
{
    float h = clamp(height, 0.0, ScatterTop());
    float rho = sqrt(h * (2.0 * GroundRadius() + h));
    uint unused;
    Cell(rho / ScatterBigH(), SCATTER_HEIGHT_SIZE, h0, unused, fh);
}

void CellMu(float height, float mu, bool ground, out uint mu0, out float fmu)
{
    float2 horizon = Horizon(GroundRadius(), clamp(height, 0.0, ScatterTop()));
    float zenith = acos(clamp(mu, -1.0, 1.0));
    float xMu = ground ? sqrt(saturate((zenith - horizon.x) / horizon.y)) : 1.0 - sqrt(max(1.0 - zenith / horizon.x, 0.0));
    uint unused;
    Cell(xMu, SCATTER_MU_HALF_SIZE, mu0, unused, fmu);
    if (ground)
        mu0 += SCATTER_MU_HALF_SIZE;
}

void CellMuS(float muS, out uint mus0, out float fmus)
{
    uint unused;
    Cell(MuSToUnit(muS), SCATTER_MU_S_SIZE, mus0, unused, fmus);
}

void CellNu(float nu, out uint nu0, out float fnu)
{
    uint unused;
    Cell((nu + 1.0) * 0.5, SCATTER_NU_SIZE, nu0, unused, fnu);
}

ScatterCell CellOf(ScatterPoint p)
{
    ScatterCell c;
    CellHeight(p.Height, c.H0, c.Fh);
    CellMu(p.Height, p.Mu, p.Ground, c.Mu0, c.Fmu);
    CellMuS(p.MuS, c.MuS0, c.Fmus);
    CellNu(p.Nu, c.Nu0, c.Fnu);
    return c;
}

uint ScatterIndex(uint ih, uint imu, uint imus, uint inu, uint l)
{
    return (((ih * SCATTER_MU_SIZE + imu) * SCATTER_MU_S_SIZE + imus) * SCATTER_NU_SIZE + inu) * SCATTER_MS_WAVELENGTHS + l;
}

float CornerWeight(ScatterCell c, uint corner)
{
    return ((corner & 1u) != 0u ? c.Fh : 1.0 - c.Fh) * ((corner & 2u) != 0u ? c.Fmu : 1.0 - c.Fmu)
         * ((corner & 4u) != 0u ? c.Fmus : 1.0 - c.Fmus) * ((corner & 8u) != 0u ? c.Fnu : 1.0 - c.Fnu);
}

uint CornerIndex(ScatterCell c, uint corner, uint l)
{
    return ScatterIndex(c.H0 + (corner & 1u), c.Mu0 + ((corner >> 1u) & 1u), c.MuS0 + ((corner >> 2u) & 1u), c.Nu0 + ((corner >> 3u) & 1u), l);
}

float Lookup1(StructuredBuffer<float> table, ScatterCell c, uint l)
{
    float sum = 0.0;
    [unroll] for (uint corner = 0u; corner < 16u; corner++)
        sum += CornerWeight(c, corner) * table[CornerIndex(c, corner, l)];
    return sum;
}

float2 Lookup2(StructuredBuffer<float2> table, ScatterCell c, uint l)
{
    float2 sum = 0.0;
    [unroll] for (uint corner = 0u; corner < 16u; corner++)
        sum += CornerWeight(c, corner) * table[CornerIndex(c, corner, l)];
    return sum;
}

// The scattering density of a phase-weighted radiance table at a point, for a wavelength at `position` among the
// table wavelengths (an integer inside the iteration; AtmSun[λ].w for a hero wavelength): σ_R(h)·A_R + σ_M(h)·A_M.
// Zero below the tables' lowest sun (30° down), not the edge texel's last glimmer.
// The haze's relative density is given, as a march's step has it (AerosolStepDensity).
float ScatterDensityWith(StructuredBuffer<float2> weighted, float4 k, float position, ScatterPoint p, float aerosol)
{
    if (p.MuS < SCATTER_MU_S_MIN)
        return 0.0;
    ScatterCell c = CellOf(p);
    uint l0 = min(uint(floor(position)), SCATTER_MS_WAVELENGTHS - 2u);
    float f = position - float(l0);
    float2 a = Lookup2(weighted, c, l0);
    if (f > 0.0)
        a = lerp(a, Lookup2(weighted, c, l0 + 1u), f);
    float altitude = GroundAltitude + p.Height;
    return k.x * RayleighDensity(altitude) * a.x + k.y * aerosol * a.y;
}

// ── Sky view ──────────────────────────────────────────────────────────────────────────────────────

float ClampAltitude(float altitude)
{
    return clamp(altitude, GroundAltitude + MIN_HEIGHT_ABOVE_GROUND, TopAltitude - MIN_HEIGHT_ABOVE_GROUND);
}

float SliceAltitude(uint slice)
{
    float altitude = slice == 0u
        ? CameraAltitude
        : TerrainLowAltitude + (TerrainHighAltitude - TerrainLowAltitude) * float(slice - 1u) / float(SKY_TERRAIN_SLICES - 1u);
    return ClampAltitude(altitude);
}

float RelativeAzimuth(float3 dir, float3 toSun)
{
    float dirLength = length(dir.xz);
    float sunLength = length(toSun.xz);
    if (dirLength < 1.0e-6 || sunLength < 1.0e-6)
        return 0.0;
    return acos(clamp(dot(dir.xz, toSun.xz) / (dirLength * sunLength), -1.0, 1.0));
}

// One slice's (smooth, haze) for four wavelengths, bilinear on that slice's own horizon map.
void SampleSkyViewSlice(uint slice, float mu, float phi, uint4 idx, out float4 smooth, out float4 mie)
{
    float height = SliceAltitude(slice) - GroundAltitude;
    float2 horizon = Horizon(GroundRadius(), height);
    float zenith = acos(clamp(mu, -1.0, 1.0));
    uint first, count;
    float v;
    if (zenith <= horizon.x)
    {
        first = 0u;
        count = SKY_ROWS_ABOVE;
        v = 1.0 - sqrt(max(1.0 - zenith / horizon.x, 0.0));
    }
    else
    {
        first = SKY_ROWS_ABOVE;
        count = SKY_ROWS_BELOW;
        v = sqrt(saturate((zenith - horizon.x) / horizon.y));
    }
    uint r0, r1, c0, c1;
    float fr, fc;
    Cell(v, count, r0, r1, fr);
    Cell(saturate(phi / ATM_PI), SKY_AZIMUTH_SIZE, c0, c1, fc);
    uint row0 = (slice * SKY_ROWS + first + r0) * SKY_AZIMUTH_SIZE;
    uint row1 = (slice * SKY_ROWS + first + r1) * SKY_AZIMUTH_SIZE;
    uint a00 = (row0 + c0) * ATM_WAVELENGTHS, a10 = (row0 + c1) * ATM_WAVELENGTHS;
    uint a01 = (row1 + c0) * ATM_WAVELENGTHS, a11 = (row1 + c1) * ATM_WAVELENGTHS;
    float2 t[4];
    [unroll] for (uint k = 0u; k < 4u; k++)
    {
        uint l = idx[k];
        float2 low = lerp(SkyViewLut[a00 + l], SkyViewLut[a10 + l], fc);
        float2 high = lerp(SkyViewLut[a01 + l], SkyViewLut[a11 + l], fc);
        t[k] = lerp(low, high, fr);
    }
    smooth = float4(t[0].x, t[1].x, t[2].x, t[3].x);
    mie = float4(t[0].y, t[1].y, t[2].y, t[3].y);
}

// The sky toward `dir` for four wavelengths, in W·m⁻²·sr⁻¹·nm⁻¹ (no sun disk). Camera rays read slice 0, at the
// camera's exact altitude; rays leaving the terrain blend the two terrain slices around their altitude.
float4 SkyRadiance(bool cameraRay, float altitude, float3 dir, uint4 idx)
{
    float mu = dir.y;
    float phi = RelativeAzimuth(dir, AtmToSun);
    float4 smooth, mie;
    if (cameraRay)
    {
        SampleSkyViewSlice(0u, mu, phi, idx, smooth, mie);
    }
    else
    {
        float span = TerrainHighAltitude - TerrainLowAltitude;
        float s = span > 1.0e-3 ? saturate((altitude - TerrainLowAltitude) / span) * float(SKY_TERRAIN_SLICES - 1u) : 0.0;
        uint i0 = min(uint(floor(s)), SKY_TERRAIN_SLICES - 2u);
        float f = s - float(i0);
        float4 smooth0, mie0, smooth1, mie1;
        SampleSkyViewSlice(1u + i0, mu, phi, idx, smooth0, mie0);
        SampleSkyViewSlice(2u + i0, mu, phi, idx, smooth1, mie1);
        smooth = lerp(smooth0, smooth1, f);
        mie = lerp(mie0, mie1, f);
    }
    float4 toa = float4(AtmSun[idx.x].y, AtmSun[idx.y].y, AtmSun[idx.z].y, AtmSun[idx.w].y);
    return toa * (smooth + HenyeyGreenstein(dot(dir, AtmToSun), MieG) * mie);
}

// ── Aerial perspective (vista-plan §10 M1.4; C# reference: Vista.Atmosphere.AerialPerspective) ────

#ifndef AERIAL_STEPS
#define AERIAL_STEPS 8u          // the most steps a segment takes
#endif
#ifndef AERIAL_STEP_LENGTH
#define AERIAL_STEP_LENGTH 1000.0 // a segment takes 2 steps, plus one per this many metres, up to AERIAL_STEPS
#endif
#ifndef AERIAL_MS_SPAN
#define AERIAL_MS_SPAN 250.0     // a segment climbing or dropping less than this reads the orders once, at its middle
#endif

// A straight segment through the air, described from its start: the start's height above the ground sphere, the
// direction's and the sun's cosines to the vertical there, the direction-to-sun cosine, and the length.
struct AirSegment
{
    float Height;
    float Mu;
    float MuS;
    float Nu;
    float Length;
};

// The 4D domain point `t` metres along a segment: height, the view and sun cosines to the vertical there (they turn as
// the ray crosses the sphere), and which half of the view map its ray belongs to.
ScatterPoint AirPointAt(AirSegment seg, float t)
{
    float rb = GroundRadius();
    float r0 = rb + seg.Height;
    float h = max(AltitudeAlong(rb, seg.Height, seg.Mu, t), 0.0);
    float r = rb + h;
    ScatterPoint p;
    p.Height = h;
    p.Mu = clamp((r0 * seg.Mu + t) / r, -1.0, 1.0);
    p.MuS = clamp((r0 * seg.MuS + t * seg.Nu) / r, -1.0, 1.0);
    p.Nu = ClampNu(p.Mu, p.MuS, seg.Nu);
    p.Ground = HitsSphere(rb, h, p.Mu);
    return p;
}

// The phase-weighted radiance of the summed orders (Rayleigh, haze) at a point for four wavelengths at their table
// positions: one cell, each corner read once, each wavelength blended between its two table wavelengths. Zero below
// the tables' lowest sun, as in ScatterDensity.
void LookupWeighted4(ScatterPoint p, float4 positions, out float4 rayleigh, out float4 mie)
{
    rayleigh = 0.0;
    mie = 0.0;
    if (p.MuS < SCATTER_MU_S_MIN)
        return;
    ScatterCell c = CellOf(p);
    uint4 l0 = min(uint4(floor(positions)), SCATTER_MS_WAVELENGTHS - 2u);
    float4 f = positions - float4(l0);
    [unroll] for (uint corner = 0u; corner < 16u; corner++)
    {
        float w = CornerWeight(c, corner);
        uint base = CornerIndex(c, corner, 0u);
        [unroll] for (uint i = 0u; i < 4u; i++)
        {
            float2 v = lerp(ScatterWeighted[base + l0[i]], ScatterWeighted[base + l0[i] + 1u], f[i]);
            rayleigh[i] += w * v.x;
            mie[i] += w * v.y;
        }
    }
}

// The air along a segment for four wavelengths: its transmittance, and the light it scatters toward the segment's
// start per unit solar irradiance at the top of the atmosphere. AERIAL_STEPS steps, dense at the segment's lower end
// (the haze lives there): extinction from the analytic densities, single scattering with the sun's transmittance from
// the columns LUT (the Earth's shadow included; a mountain's is not, vista-plan §10 M1.4), and multiple scattering
// from the summed orders, whose phase-weighted radiance is read at the two ends and blended along the segment — it is
// smooth, while the densities that turn it into emission are not.
void AerialSegment(AirSegment seg, uint4 idx, out float4 transmittance, out float4 inscatter)
{
    float4 k0 = AtmCoefficients[idx.x], k1 = AtmCoefficients[idx.y], k2 = AtmCoefficients[idx.z], k3 = AtmCoefficients[idx.w];
    float4 kR = float4(k0.x, k1.x, k2.x, k3.x);
    float4 kMs = float4(k0.y, k1.y, k2.y, k3.y);
    float4 kMe = float4(k0.z, k1.z, k2.z, k3.z);
    float4 kO = float4(k0.w, k1.w, k2.w, k3.w);
    float4 positions = float4(AtmSun[idx.x].w, AtmSun[idx.y].w, AtmSun[idx.z].w, AtmSun[idx.w].w);

    float rb = GroundRadius();
    float r0 = rb + seg.Height;
    float hEnd = AltitudeAlong(rb, seg.Height, seg.Mu, seg.Length);
    bool denseAtEnd = hEnd < seg.Height;
    uint steps = min(AERIAL_STEPS, 2u + uint(seg.Length / AERIAL_STEP_LENGTH));

    float4 aR0 = 0.0, aM0 = 0.0, aR1 = 0.0, aM1 = 0.0;
#ifndef SINGLE_SCATTERING_ONLY
    if (abs(hEnd - seg.Height) < AERIAL_MS_SPAN)
    {
        LookupWeighted4(AirPointAt(seg, 0.5 * seg.Length), positions, aR0, aM0);
        aR1 = aR0;
        aM1 = aM0;
    }
    else
    {
        LookupWeighted4(AirPointAt(seg, 0.0), positions, aR0, aM0);
        LookupWeighted4(AirPointAt(seg, seg.Length), positions, aR1, aM1);
    }
#endif
    float phaseR = RayleighPhase(seg.Nu);
    float phaseM = HenyeyGreenstein(seg.Nu, MieG);
    transmittance = 1.0;
    inscatter = 0.0;
    float start = GroundAltitude + max(AltitudeAlong(rb, seg.Height, seg.Mu, 0.0), 0.0);
    for (uint s = 0u; s < steps; s++)
    {
        float2 ends = SegmentEnds(seg.Length, s, steps, denseAtEnd);
        float t = 0.5 * (ends.x + ends.y), dt = ends.y - ends.x;
        float hg = max(AltitudeAlong(rb, seg.Height, seg.Mu, t), 0.0);
        float muSp = (r0 * seg.MuS + t * seg.Nu) / (rb + hg);
        float altitude = GroundAltitude + hg;
        float end = GroundAltitude + max(AltitudeAlong(rb, seg.Height, seg.Mu, ends.y), 0.0);
        float aerosol = AerosolStepDensity(start, altitude, end);
        start = end;
        float4 sigmaR = kR * RayleighDensity(altitude);
        float4 sigmaM = kMs * aerosol;
        float4 extinction = sigmaR + kMe * aerosol + kO * OzoneDensity(altitude);
        float4 sun = 0.0;
        float sunMu;
        float visible = SunVisibility(hg, muSp, sunMu);
        if (visible > 0.0)
        {
            float3 c = SampleColumns(altitude, sunMu);
            sun = visible * exp(-(kR * c.x + kMe * c.y + kO * c.z));
        }
        float f = seg.Length > 0.0 ? t / seg.Length : 0.0;
        float4 emission = sun * (sigmaR * phaseR + sigmaM * phaseM) + sigmaR * lerp(aR0, aR1, f) + sigmaM * lerp(aM0, aM1, f);
        float4 integral = float4(StepIntegral(extinction.x, dt), StepIntegral(extinction.y, dt), StepIntegral(extinction.z, dt), StepIntegral(extinction.w, dt));
        inscatter += transmittance * integral * emission;
        transmittance *= exp(-extinction * dt);
    }
}

// The transmittance alone, on the same steps: what the sky-view difference (AERIAL_SKY_DIFFERENCE) multiplies the far
// end's sky by.
float4 AerialTransmittance(AirSegment seg, uint4 idx)
{
    float4 k0 = AtmCoefficients[idx.x], k1 = AtmCoefficients[idx.y], k2 = AtmCoefficients[idx.z], k3 = AtmCoefficients[idx.w];
    float4 kR = float4(k0.x, k1.x, k2.x, k3.x);
    float4 kMe = float4(k0.z, k1.z, k2.z, k3.z);
    float4 kO = float4(k0.w, k1.w, k2.w, k3.w);
    float rb = GroundRadius();
    bool denseAtEnd = AltitudeAlong(rb, seg.Height, seg.Mu, seg.Length) < seg.Height;
    uint steps = min(AERIAL_STEPS, 2u + uint(seg.Length / AERIAL_STEP_LENGTH));
    float4 depth = 0.0;
    float start = GroundAltitude + max(AltitudeAlong(rb, seg.Height, seg.Mu, 0.0), 0.0);
    for (uint s = 0u; s < steps; s++)
    {
        float2 ends = SegmentEnds(seg.Length, s, steps, denseAtEnd);
        float altitude = GroundAltitude + max(AltitudeAlong(rb, seg.Height, seg.Mu, 0.5 * (ends.x + ends.y)), 0.0);
        float end = GroundAltitude + max(AltitudeAlong(rb, seg.Height, seg.Mu, ends.y), 0.0);
        depth += (kR * RayleighDensity(altitude) + kMe * AerosolStepDensity(start, altitude, end) + kO * OzoneDensity(altitude)) * (ends.y - ends.x);
        start = end;
    }
    return exp(-depth);
}
