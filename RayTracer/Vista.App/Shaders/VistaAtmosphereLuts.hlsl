// Vista atmosphere LUT builders (vista-plan §10 M1.3), one entry point per table, in build order:
//   CSColumns              density columns to the top of the atmosphere (wavelength-free transmittance)
//   CSDirectIrradiance     sunlight on the ground: the order-0 irradiance                     (reads Columns)
//   CSSingleScattering     4D single scattering without phases                                (reads Columns)
//   then for each order n = 2 … 5 (Bruneton 2017's iteration):
//     CSScatteringDensity    4D phase-weighted radiance of order n, added to the sum (reads Single or Order, IrradianceOrder)
//     CSIndirectIrradiance   the ground's irradiance of order n − 1                  (reads Single or Order)
//     CSMultipleScattering   4D radiance of order n                                  (reads the order-n density)
//   CSGroundIrradiance     the sky's irradiance on the ground                         (reads Columns, the summed orders)
//   CSSkyView              the sky per direction in altitude slices, for one sun      (reads Columns, the sum, GroundIrr)
// Each thread computes one texel of one wavelength and writes only it (gather-only, no atomics; the density adds
// to its own texel of the sum), so a build is bit-identical every time on one driver. The 4D tables and the
// irradiance by order hold 11 of the 50 wavelengths (HeroOf); the sky march interpolates between them. C#
// references: Vista.Atmosphere.AtmosphereLuts and AtmosphereScattering. Values are per unit solar irradiance at the
// top of the atmosphere.

#include "VistaAtmosphere.hlsli"

#define COLUMN_STEPS                 1024u
#define SCATTER_STEPS                40u
#define DENSITY_THETA_ABOVE          16u
#define DENSITY_THETA_BELOW          16u
#define DENSITY_THETA                (DENSITY_THETA_ABOVE + DENSITY_THETA_BELOW)
#define DENSITY_PHI                  32u
#define IRRADIANCE_THETA             16u
#define IRRADIANCE_PHI               16u
#define GROUND_DIRECTIONS_PER_SIDE   32u
#define SKY_STEPS                    128u

cbuffer BuildConstants : register(b0)
{
    uint ItemCount;   // work items in the whole table
    uint FirstItem;   // the first this dispatch covers
    uint Order;       // the scattering order, for the order kernels
    uint _build0;
};

RWStructuredBuffer<float> LutOut  : register(u0);
RWStructuredBuffer<float> LutSum  : register(u1); // the density kernel's running sum of orders

// ── Columns ───────────────────────────────────────────────────────────────────────────────────────

[numthreads(64, 1, 1)]
void CSColumns(uint3 tid : SV_DispatchThreadID)
{
    uint item = FirstItem + tid.x;
    if (item >= ItemCount)
        return;
    uint i = item % COLUMNS_MU_SIZE;
    uint j = item / COLUMNS_MU_SIZE;
    float xMu = float(i) / float(COLUMNS_MU_SIZE - 1u);
    float xH = float(j) / float(COLUMNS_ALTITUDE_SIZE - 1u);

    // ColumnsCoordinatesToAltitudeMu.
    float bigH = sqrt(TopAltitude * (2.0 * SeaRadius + TopAltitude));
    float rho = bigH * xH;
    float h = rho * rho / (SeaRadius + sqrt(SeaRadius * SeaRadius + rho * rho));
    float r = SeaRadius + h;
    float dMin = TopAltitude - h;
    float dMax = rho + bigH;
    float d = dMin + xMu * (dMax - dMin);
    float mu = d <= 0.0 ? 1.0 : clamp(((TopAltitude - h) * (2.0 * SeaRadius + TopAltitude + h) - d * d) / (2.0 * r * d), -1.0, 1.0);

    // ColumnsAlong.
    float pathLength = DistanceToShellAbove(SeaRadius, h, TopAltitude, mu);
    float dt = pathLength / float(COLUMN_STEPS);
    float3 sum = 0.0;
    float start = AltitudeAlong(SeaRadius, h, mu, 0.0);
    for (uint k = 0u; k < COLUMN_STEPS; k++)
    {
        float a = AltitudeAlong(SeaRadius, h, mu, (float(k) + 0.5) * dt);
        float end = AltitudeAlong(SeaRadius, h, mu, float(k + 1u) * dt);
        sum += float3(RayleighDensity(a), AerosolStepDensity(start, a, end), OzoneDensity(a));
        start = end;
    }
    LutOut[item * 4u] = sum.x * dt;
    LutOut[item * 4u + 1u] = sum.y * dt;
    LutOut[item * 4u + 2u] = sum.z * dt;
    LutOut[item * 4u + 3u] = 0.0;
}

// ── Ground irradiance by order ────────────────────────────────────────────────────────────────────

[numthreads(64, 1, 1)]
void CSDirectIrradiance(uint3 tid : SV_DispatchThreadID)
{
    uint item = FirstItem + tid.x;
    if (item >= ItemCount)
        return;
    uint l = item % SCATTER_MS_WAVELENGTHS;
    uint entry = item / SCATTER_MS_WAVELENGTHS;
    float muS = GroundIrradianceMuS(entry);
    LutOut[item] = muS > 0.0 ? muS * exp(-OpticalDepth(AtmCoefficients[HeroOf(l)], SampleColumns(GroundAltitude, muS))) : 0.0;
}

[numthreads(64, 1, 1)]
void CSIndirectIrradiance(uint3 tid : SV_DispatchThreadID)
{
    uint item = FirstItem + tid.x;
    if (item >= ItemCount)
        return;
    uint l = item % SCATTER_MS_WAVELENGTHS;
    uint entry = item / SCATTER_MS_WAVELENGTHS;
    float muS = GroundIrradianceMuS(entry);
    float sinS = sqrt(max(1.0 - muS * muS, 0.0));
    float dTheta = 0.5 * ATM_PI / float(IRRADIANCE_THETA);
    float dPhi = 2.0 * ATM_PI / float(IRRADIANCE_PHI);
    bool firstOrder = Order == 1u;
    float sum = 0.0;
    for (uint i = 0u; i < IRRADIANCE_THETA; i++)
    {
        float theta = (float(i) + 0.5) * dTheta;
        float cosTheta = cos(theta), sinTheta = sin(theta);
        for (uint j = 0u; j < IRRADIANCE_PHI; j++)
        {
            float phi = (float(j) + 0.5) * dPhi;
            float nu = sinTheta * cos(phi) * sinS + cosTheta * muS;
            ScatterPoint p;
            p.Height = 0.0;
            p.Mu = cosTheta;
            p.MuS = muS;
            p.Nu = ClampNu(cosTheta, muS, nu);
            p.Ground = false;
            ScatterCell c = CellOf(p);
            float radiance;
            if (firstOrder)
            {
                float2 single = Lookup2(ScatterSingle, c, l);
                radiance = single.x * RayleighPhase(nu) + single.y * HenyeyGreenstein(nu, MieG);
            }
            else
            {
                radiance = Lookup1(ScatterOrder, c, l);
            }
            sum += radiance * cosTheta * sinTheta * dTheta * dPhi;
        }
    }
    LutOut[item] = sum;
}

// ── 4D tables ─────────────────────────────────────────────────────────────────────────────────────

void ScatterItem(uint item, out uint l, out uint ih, out uint imu, out uint imus, out uint inu)
{
    l = item % SCATTER_MS_WAVELENGTHS;
    uint texel = item / SCATTER_MS_WAVELENGTHS;
    inu = texel % SCATTER_NU_SIZE;
    imus = (texel / SCATTER_NU_SIZE) % SCATTER_MU_S_SIZE;
    imu = (texel / (SCATTER_NU_SIZE * SCATTER_MU_S_SIZE)) % SCATTER_MU_SIZE;
    ih = texel / (SCATTER_NU_SIZE * SCATTER_MU_S_SIZE * SCATTER_MU_SIZE);
}

// Polar angle i of the density's sphere quadrature (C# reference: AtmosphereScattering.DensityPolar): DENSITY_THETA_ABOVE
// angles above the ground sphere's horizon (zenith angle `horizon`) and DENSITY_THETA_BELOW below it, each half dense
// toward the horizon, so no angle straddles it — from a few kilometres up the twilight glow lies in the 2° between the
// horizontal and the dipped horizon, which a fixed grid called ground (vista-plan §10 M1.4).
void DensityPolar(uint i, float horizon, out float theta, out float dTheta, out bool ground)
{
    ground = i >= DENSITY_THETA_ABOVE;
    if (!ground)
    {
        float u = (float(i) + 0.5) / float(DENSITY_THETA_ABOVE);
        theta = horizon * (1.0 - (1.0 - u) * (1.0 - u));
        dTheta = horizon * 2.0 * (1.0 - u) / float(DENSITY_THETA_ABOVE);
    }
    else
    {
        float u = (float(i - DENSITY_THETA_ABOVE) + 0.5) / float(DENSITY_THETA_BELOW);
        float span = ATM_PI - horizon;
        theta = horizon + span * u * u;
        dTheta = span * 2.0 * u / float(DENSITY_THETA_BELOW);
    }
}

// The weights' corrections per half, so a uniform field integrates to each half's exact solid angle (2π(1 ∓ cos θ_h)):
// the warped midpoint rule alone overshoots by 0.2%.
float2 DensityScales(float horizon)
{
    float2 sum = 0.0;
    for (uint i = 0u; i < DENSITY_THETA; i++)
    {
        float theta, dTheta;
        bool ground;
        DensityPolar(i, horizon, theta, dTheta, ground);
        if (ground)
            sum.y += sin(theta) * dTheta;
        else
            sum.x += sin(theta) * dTheta;
    }
    float c = cos(horizon);
    return float2((1.0 - c) / sum.x, (1.0 + c) / sum.y);
}

float RayLength(ScatterPoint p)
{
    float rb = GroundRadius();
    return p.Ground ? DistanceToSphereBelow(rb, p.Height, p.Mu) : DistanceToShellAbove(rb, p.Height, ScatterTop(), p.Mu);
}

// Step s of `steps` along a ray from `height` above the ground sphere at cos = mu: (midpoint, length, its height, the haze's
// relative density over it). C# reference: AtmosphereScattering.StepAt and the sky march's step.
float4 MarchStep(float pathLength, uint s, uint steps, bool denseAtEnd, float height, float mu)
{
    float rb = GroundRadius();
    float2 ends = SegmentEnds(pathLength, s, steps, denseAtEnd);
    float t = 0.5 * (ends.x + ends.y);
    float hg = AltitudeAlong(rb, height, mu, t);
    float aerosol = AerosolStepDensity(GroundAltitude + AltitudeAlong(rb, height, mu, ends.x), GroundAltitude + hg,
        GroundAltitude + AltitudeAlong(rb, height, mu, ends.y));
    return float4(t, ends.y - ends.x, hg, aerosol);
}

[numthreads(64, 1, 1)]
void CSSingleScattering(uint3 tid : SV_DispatchThreadID)
{
    uint item = FirstItem + tid.x;
    if (item >= ItemCount)
        return;
    uint l, ih, imu, imus, inu;
    ScatterItem(item, l, ih, imu, imus, inu);
    ScatterPoint p = TexelToScatterPoint(ih, imu, imus, inu);
    float4 k = AtmCoefficients[HeroOf(l)];
    float rb = GroundRadius();
    float r0 = rb + p.Height;
    float pathLength = RayLength(p);
    float transmittance = 1.0, rayleigh = 0.0, mie = 0.0;
    for (uint s = 0u; s < SCATTER_STEPS; s++)
    {
        float4 step4 = MarchStep(pathLength, s, SCATTER_STEPS, p.Ground, p.Height, p.Mu);
        float t = step4.x, dt = step4.y, hg = step4.z;
        float rp = rb + hg;
        float muSp = (r0 * p.MuS + t * p.Nu) / rp;
        float altitude = GroundAltitude + hg;
        float4 medium = MediumWith(k, altitude, step4.w);
        float sunMu;
        float visible = SunVisibility(hg, muSp, sunMu);
        float sun = visible > 0.0 ? visible * exp(-OpticalDepth(k, SampleColumns(altitude, sunMu))) : 0.0;
        float step = transmittance * StepIntegral(medium.y, dt);
        rayleigh += step * sun * medium.z;
        mie += step * sun * medium.w;
        transmittance *= exp(-medium.y * dt);
    }
    LutOut[item * 2u] = rayleigh;
    LutOut[item * 2u + 1u] = mie;
}

[numthreads(64, 1, 1)]
void CSScatteringDensity(uint3 tid : SV_DispatchThreadID)
{
    uint item = FirstItem + tid.x;
    if (item >= ItemCount)
        return;
    uint l, ih, imu, imus, inu;
    ScatterItem(item, l, ih, imu, imus, inu);
    ScatterPoint p = TexelToScatterPoint(ih, imu, imus, inu);
    float4 k = AtmCoefficients[HeroOf(l)];
    float rb = GroundRadius();
    float r = rb + p.Height;

    // DensityTexel's frame: the view along +X tilted by μ, the sun from ν and μ_s.
    float sinMu = sqrt(max(1.0 - p.Mu * p.Mu, 0.0));
    float3 w = float3(sinMu, p.Mu, 0.0);
    float sx = sinMu > 1.0e-6 ? (p.Nu - p.Mu * p.MuS) / sinMu : 0.0;
    float3 sun = float3(sx, p.MuS, sqrt(max(1.0 - sx * sx - p.MuS * p.MuS, 0.0)));

    float horizon = Horizon(rb, p.Height).x;
    float2 scales = DensityScales(horizon);
    float dPhi = 2.0 * ATM_PI / float(DENSITY_PHI);
    ScatterCell c;
    CellHeight(p.Height, c.H0, c.Fh);
    CellMuS(p.MuS, c.MuS0, c.Fmus);
    float2 sum = 0.0;
    for (uint i = 0u; i < DENSITY_THETA; i++)
    {
        float theta, dTheta;
        bool ground;
        DensityPolar(i, horizon, theta, dTheta, ground);
        dTheta *= ground ? scales.y : scales.x;
        float cosTheta = cos(theta), sinTheta = sin(theta);
        CellMu(p.Height, cosTheta, ground, c.Mu0, c.Fmu);
        float groundTransmittance = 0.0;
        float groundDistance = 0.0;
        if (ground)
        {
            groundDistance = DistanceToSphereBelow(rb, p.Height, cosTheta);
            float muGround = -(r * cosTheta + groundDistance) / rb;
            float depth = OpticalDepth(k, SampleColumns(GroundAltitude, muGround)) - OpticalDepth(k, SampleColumns(GroundAltitude + p.Height, -cosTheta));
            groundTransmittance = exp(-max(depth, 0.0));
        }
        for (uint j = 0u; j < DENSITY_PHI; j++)
        {
            float phi = (float(j) + 0.5) * dPhi;
            float3 o = float3(sinTheta * cos(phi), cosTheta, sinTheta * sin(phi));
            float nu1 = dot(o, sun);
            CellNu(ClampNu(cosTheta, p.MuS, nu1), c.Nu0, c.Fnu);
            float incident;
            if (Order == 2u)
            {
                float2 single = Lookup2(ScatterSingle, c, l);
                incident = single.x * RayleighPhase(nu1) + single.y * HenyeyGreenstein(nu1, MieG);
            }
            else
            {
                incident = Lookup1(ScatterOrder, c, l);
            }
            if (ground)
            {
                float muSg = (r * p.MuS + groundDistance * nu1) / rb;
                incident += groundTransmittance * GroundAlbedo / ATM_PI * SampleIrradianceTable(IrradianceOrder, SCATTER_MS_WAVELENGTHS, l, muSg);
            }
            float nu2 = dot(o, w);
            float weight = incident * sinTheta * dTheta * dPhi;
            sum += weight * float2(RayleighPhase(nu2), HenyeyGreenstein(nu2, MieG));
        }
    }
    LutOut[item * 2u] = sum.x;
    LutOut[item * 2u + 1u] = sum.y;
    float2 before = Order == 2u ? float2(0.0, 0.0) : float2(LutSum[item * 2u], LutSum[item * 2u + 1u]);
    LutSum[item * 2u] = before.x + sum.x;
    LutSum[item * 2u + 1u] = before.y + sum.y;
}

[numthreads(64, 1, 1)]
void CSMultipleScattering(uint3 tid : SV_DispatchThreadID)
{
    uint item = FirstItem + tid.x;
    if (item >= ItemCount)
        return;
    uint l, ih, imu, imus, inu;
    ScatterItem(item, l, ih, imu, imus, inu);
    ScatterPoint p = TexelToScatterPoint(ih, imu, imus, inu);
    float4 k = AtmCoefficients[HeroOf(l)];
    float rb = GroundRadius();
    float r0 = rb + p.Height;
    float pathLength = RayLength(p);
    float transmittance = 1.0, radiance = 0.0;
    for (uint s = 0u; s < SCATTER_STEPS; s++)
    {
        float4 step4 = MarchStep(pathLength, s, SCATTER_STEPS, p.Ground, p.Height, p.Mu);
        float t = step4.x, dt = step4.y, hg = step4.z;
        float rp = rb + hg;
        ScatterPoint q;
        q.Height = hg;
        q.Mu = (r0 * p.Mu + t) / rp;
        q.MuS = (r0 * p.MuS + t * p.Nu) / rp;
        q.Nu = ClampNu(q.Mu, q.MuS, p.Nu);
        q.Ground = p.Ground;
        float extinction = MediumWith(k, GroundAltitude + hg, step4.w).y;
        radiance += transmittance * ScatterDensityWith(ScatterWeighted, k, float(l), q, step4.w) * StepIntegral(extinction, dt);
        transmittance *= exp(-extinction * dt);
    }
    LutOut[item] = radiance;
}

// ── Sky march (ground irradiance and sky view) ────────────────────────────────────────────────────

// (light without the haze phase, haze single scattering without its phase) along one ray.
float2 SkyMarch(float4 k, uint lambda, float height, float mu, float muS, float nu, bool toGround)
{
    float groundRadius = GroundRadius();
    float top = SeaRadius + TopAltitude - groundRadius;
    float r0 = groundRadius + height;
    float pathLength = toGround ? DistanceToSphereBelow(groundRadius, height, mu) : DistanceToShellAbove(groundRadius, height, top, mu);
    float transmittance = 1.0, rayleigh = 0.0, mie = 0.0, multiple = 0.0;
    for (uint s = 0u; s < SKY_STEPS; s++)
    {
        float4 step4 = MarchStep(pathLength, s, SKY_STEPS, toGround, height, mu);
        float t = step4.x, dt = step4.y, hg = step4.z;
        float rp = groundRadius + hg;
        float muP = (r0 * mu + t) / rp;
        float muSp = (r0 * muS + t * nu) / rp;
        float altitude = GroundAltitude + hg;
        float4 medium = MediumWith(k, altitude, step4.w);
        float sunMu;
        float visible = SunVisibility(hg, muSp, sunMu);
        float sun = visible > 0.0 ? visible * exp(-OpticalDepth(k, SampleColumns(altitude, sunMu))) : 0.0;
        ScatterPoint q;
        q.Height = hg;
        q.Mu = muP;
        q.MuS = muSp;
        q.Nu = ClampNu(muP, muSp, nu);
        q.Ground = toGround;
        float step = transmittance * StepIntegral(medium.y, dt);
        rayleigh += step * sun * medium.z;
        mie += step * sun * medium.w;
#ifndef SINGLE_SCATTERING_ONLY
        multiple += step * ScatterDensityWith(ScatterWeighted, k, AtmSun[lambda].w, q, step4.w);
#endif
        transmittance *= exp(-medium.y * dt);
    }
    float smooth = RayleighPhase(nu) * rayleigh + multiple;
    if (toGround)
    {
        float muSg = (r0 * muS + pathLength * nu) / groundRadius;
        float direct = muSg > 0.0 ? muSg * exp(-OpticalDepth(k, SampleColumns(GroundAltitude, muSg))) : 0.0;
        smooth += transmittance * GroundAlbedo / ATM_PI * (direct + SampleGroundIrradiance(lambda, muSg));
    }
    return float2(smooth, mie);
}

[numthreads(64, 1, 1)]
void CSGroundIrradiance(uint3 tid : SV_DispatchThreadID)
{
    uint item = FirstItem + tid.x;
    if (item >= ItemCount)
        return;
    uint lambda = item % ATM_WAVELENGTHS;
    uint entry = item / ATM_WAVELENGTHS;
    float muS = GroundIrradianceMuS(entry);
    float sinS = sqrt(max(1.0 - muS * muS, 0.0));
    float4 k = AtmCoefficients[lambda];
    float sum = 0.0;
    for (uint a = 0u; a < GROUND_DIRECTIONS_PER_SIDE; a++)
    {
        for (uint b = 0u; b < GROUND_DIRECTIONS_PER_SIDE; b++)
        {
            float u1 = (float(a) + 0.5) / float(GROUND_DIRECTIONS_PER_SIDE);
            float u2 = (float(b) + 0.5) / float(GROUND_DIRECTIONS_PER_SIDE);
            float radial = sqrt(u1);
            float phi = 2.0 * ATM_PI * u2;
            float cosTheta = sqrt(1.0 - u1);
            float nu = radial * cos(phi) * sinS + cosTheta * muS;
            float2 sky = SkyMarch(k, lambda, SCATTER_MIN_HEIGHT, cosTheta, muS, nu, false);
            sum += sky.x + HenyeyGreenstein(nu, MieG) * sky.y;
        }
    }
    LutOut[item] = ATM_PI * sum / float(GROUND_DIRECTIONS_PER_SIDE * GROUND_DIRECTIONS_PER_SIDE);
}

[numthreads(64, 1, 1)]
void CSSkyView(uint3 tid : SV_DispatchThreadID)
{
    uint item = FirstItem + tid.x;
    if (item >= ItemCount)
        return;
    uint lambda = item % ATM_WAVELENGTHS;
    uint texel = item / ATM_WAVELENGTHS;
    uint column = texel % SKY_AZIMUTH_SIZE;
    uint row = (texel / SKY_AZIMUTH_SIZE) % SKY_ROWS;
    uint slice = texel / (SKY_AZIMUTH_SIZE * SKY_ROWS);

    float height = SliceAltitude(slice) - GroundAltitude;
    float2 horizon = Horizon(GroundRadius(), height);
    float zenith;
    bool toGround;
    if (row < SKY_ROWS_ABOVE)
    {
        float v = float(row) / float(SKY_ROWS_ABOVE - 1u);
        zenith = horizon.x * (1.0 - (1.0 - v) * (1.0 - v));
        toGround = false;
    }
    else
    {
        float w = float(row - SKY_ROWS_ABOVE) / float(SKY_ROWS_BELOW - 1u);
        zenith = horizon.x + horizon.y * w * w;
        toGround = true;
    }
    float phi = ATM_PI * float(column) / float(SKY_AZIMUTH_SIZE - 1u);
    float muS = clamp(AtmToSun.y, -1.0, 1.0);
    float sinS = sqrt(max(1.0 - muS * muS, 0.0));
    float mu = cos(zenith);
    float nu = sin(zenith) * cos(phi) * sinS + mu * muS;
    float2 sky = SkyMarch(AtmCoefficients[lambda], lambda, height, mu, muS, nu, toGround);
    LutOut[item * 2u] = sky.x;
    LutOut[item * 2u + 1u] = sky.y;
}
