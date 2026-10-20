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
#define SCATTER_HEIGHT_SIZE      16u
#define SCATTER_MU_HALF_SIZE     32u
#define SCATTER_MU_SIZE          64u
#define SCATTER_MU_S_SIZE        64u
#define SCATTER_NU_SIZE          8u
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
};

StructuredBuffer<float4> AtmCoefficients : register(t8);   // per wavelength: Rayleigh, haze scattering, haze extinction, ozone
StructuredBuffer<float4> AtmSun          : register(t9);   // per wavelength: disk radiance, top-of-atmosphere irradiance, ground albedo, 0
StructuredBuffer<float4> ColumnsLut      : register(t10);  // density columns to the top: Rayleigh, haze, ozone, 0
StructuredBuffer<float2> ScatterSingle   : register(t11);  // 4D single scattering without phases (Rayleigh, haze)
StructuredBuffer<float>  ScatterOrder    : register(t12);  // 4D radiance of the previous order
StructuredBuffer<float2> ScatterWeighted : register(t13);  // 4D phase-weighted radiance (Rayleigh, haze): one order, or orders 2… summed
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
float AerosolDensity(float altitude)  { return exp(-altitude / AerosolScaleHeight); }
float OzoneDensity(float altitude)    { return max(0.0, 1.0 - abs(altitude - OzonePeak) / OzoneHalfWidth); }

// (total scattering, extinction, Rayleigh scattering, haze scattering) per metre at an altitude above sea level.
float4 MediumAt(float4 k, float altitude)
{
    float rayleigh = k.x * RayleighDensity(altitude);
    float aerosol = AerosolDensity(altitude);
    float mie = k.y * aerosol;
    float extinction = rayleigh + k.z * aerosol + k.w * OzoneDensity(altitude);
    return float4(rayleigh + mie, extinction, rayleigh, mie);
}

float OpticalDepth(float4 k, float3 columns) { return k.x * columns.x + k.z * columns.y + k.w * columns.z; }

// Step s of `steps` along a ray: (midpoint, length), growing quadratically away from the dense end — the start, or
// the ground for a ray that ends there (C# reference: AtmosphereLuts.Segment).
float2 Segment(float pathLength, uint s, uint steps, bool denseAtEnd)
{
    float u0 = float(s) / float(steps), u1 = float(s + 1u) / float(steps);
    float t0 = pathLength * (denseAtEnd ? 1.0 - (1.0 - u0) * (1.0 - u0) : u0 * u0);
    float t1 = pathLength * (denseAtEnd ? 1.0 - (1.0 - u1) * (1.0 - u1) : u1 * u1);
    return float2(0.5 * (t0 + t1), t1 - t0);
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

// ── Ground irradiance tables ──────────────────────────────────────────────────────────────────────

// A ground-irradiance table with `stride` wavelengths per entry (50 for the sky's, 11 for one order's).
float SampleIrradianceTable(StructuredBuffer<float> table, uint stride, uint lambda, float muS)
{
    uint i0, i1;
    float f;
    Cell((muS + 1.0) * 0.5, GROUND_IRRADIANCE_SIZE, i0, i1, f);
    return lerp(table[i0 * stride + lambda], table[i1 * stride + lambda], f);
}

float SampleGroundIrradiance(uint lambda, float muS) { return SampleIrradianceTable(GroundIrrLut, ATM_WAVELENGTHS, lambda, muS); }

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

ScatterPoint TexelToScatterPoint(uint ih, uint imu, uint imus, uint inu)
{
    float rb = GroundRadius();
    float top = ScatterTop();
    float bigH = ScatterBigH();
    float rho = bigH * float(ih) / float(SCATTER_HEIGHT_SIZE - 1u);
    float h = max(rho * rho / (rb + sqrt(rb * rb + rho * rho)), SCATTER_MIN_HEIGHT);
    rho = sqrt(h * (2.0 * rb + h));
    float r = rb + h;

    ScatterPoint p;
    p.Ground = imu >= SCATTER_MU_HALF_SIZE;
    float x = float(p.Ground ? imu - SCATTER_MU_HALF_SIZE : imu) / float(SCATTER_MU_HALF_SIZE - 1u);
    if (p.Ground)
    {
        float d = h + x * (rho - h);
        p.Mu = d <= 0.0 ? -1.0 : clamp(-(rho * rho + d * d) / (2.0 * r * d), -1.0, 1.0);
    }
    else
    {
        float dMin = top - h;
        float d = dMin + x * (rho + bigH - dMin);
        p.Mu = d <= 0.0 ? 1.0 : clamp(((top - h) * (2.0 * rb + top + h) - d * d) / (2.0 * r * d), -1.0, 1.0);
    }
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
    float rb = GroundRadius();
    float top = ScatterTop();
    float h = clamp(height, 0.0, top);
    float rho = sqrt(h * (2.0 * rb + h));
    float xMu;
    if (ground)
    {
        float d = DistanceToSphereBelow(rb, h, mu);
        xMu = rho > h ? (d - h) / (rho - h) : 0.0;
    }
    else
    {
        float d = DistanceToShellAbove(rb, h, top, mu);
        float dMin = top - h, dMax = rho + ScatterBigH();
        xMu = (d - dMin) / (dMax - dMin);
    }
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
float ScatterDensity(StructuredBuffer<float2> weighted, float4 k, float position, ScatterPoint p)
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
    return k.x * RayleighDensity(altitude) * a.x + k.y * AerosolDensity(altitude) * a.y;
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

// (zenith angle of the horizon, angle from the horizon to the nadir) at a height above the ground sphere.
float2 Horizon(float groundRadius, float height)
{
    float dip = atan2(sqrt(max(height * (2.0 * groundRadius + height), 0.0)), groundRadius);
    return float2(0.5 * ATM_PI + dip, 0.5 * ATM_PI - dip);
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
