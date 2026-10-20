// The sky as a light (vista-plan §5.1, §10 M1.5), included by VistaIntegrator.hlsl after VistaAtmosphere.hlsli. The float
// twin of Vista.Rendering.SkyDistribution: a piecewise-constant distribution of directions over the sphere, proportional
// to the sky's luminance, built per render on the CPU from the luminance CSSkyLuminance measures per cell. Cells are
// SKY_DIST_BANDS bands of zenith angle (evenly spaced, the horizon an edge) by SKY_DIST_COLUMNS columns of azimuth
// relative to the sun (0 toward it to π); a cell stands for both mirror halves of the sky about the sun's vertical plane.
// Directions are in the camera's frame (+Y up), the sky view's.
//
// SkyDist layout: SKY_DIST_BANDS + 1 band-edge cosines (1 … −1), the marginal CDF over bands (SKY_DIST_BANDS + 1 values,
// 0 … 1), then per band its conditional CDF over columns (SKY_DIST_COLUMNS + 1 values, 0 … 1). The pdf is computed from
// the same stored floats the sampler searches, so the two agree exactly.

#define SKY_DIST_BANDS        128u
#define SKY_DIST_COLUMNS      64u
#define SKY_DIST_SUB          4u
#define SKY_DIST_MARGINAL     (SKY_DIST_BANDS + 1u)
#define SKY_DIST_CONDITIONAL  (2u * SKY_DIST_BANDS + 2u)
#define SKY_DIST_COLUMN_WIDTH (ATM_PI / float(SKY_DIST_COLUMNS))

StructuredBuffer<float> SkyDist : register(t18);

// The power heuristic (β = 2) of a sample drawn with pdf `a` against another strategy's pdf `b` for the same direction.
float PowerHeuristic(float a, float b)
{
    float a2 = a * a;
    return a2 > 0.0 ? a2 / (a2 + b * b) : 0.0;
}

// The sun's horizontal frame: `a` toward its azimuth (+X when it stands at the zenith), `b` a quarter turn from it.
void SkySunAxes(out float2 a, out float2 b)
{
    float length2 = dot(AtmToSun.xz, AtmToSun.xz);
    a = length2 > 1.0e-12 ? AtmToSun.xz * rsqrt(length2) : float2(1.0, 0.0);
    b = float2(-a.y, a.x);
}

// The largest index i in [0, count) with SkyDist[offset + i] ≤ u.
uint SkyFind(uint offset, uint count, float u)
{
    uint lo = 0u, hi = count;
    while (hi - lo > 1u)
    {
        uint mid = (lo + hi) / 2u;
        if (SkyDist[offset + mid] <= u)
            lo = mid;
        else
            hi = mid;
    }
    return lo;
}

// The pdf in solid angle of every direction in cell (band, column): its probability over both mirror halves' solid angle.
float SkyCellPdf(uint band, uint column)
{
    float marginal = SkyDist[SKY_DIST_MARGINAL + band + 1u] - SkyDist[SKY_DIST_MARGINAL + band];
    uint at = SKY_DIST_CONDITIONAL + band * (SKY_DIST_COLUMNS + 1u);
    float conditional = SkyDist[at + column + 1u] - SkyDist[at + column];
    return marginal * conditional / (2.0 * SKY_DIST_COLUMN_WIDTH * (SkyDist[band] - SkyDist[band + 1u]));
}

// A direction from the sky distribution for three random numbers in [0, 1), and its pdf. C# reference: SkyDistribution.Sample.
float3 SampleSky(float u1, float u2, float u3, out float pdf)
{
    uint band = SkyFind(SKY_DIST_MARGINAL, SKY_DIST_BANDS, u1);
    float m0 = SkyDist[SKY_DIST_MARGINAL + band], m1 = SkyDist[SKY_DIST_MARGINAL + band + 1u];
    float v1 = (u1 - m0) / (m1 - m0);
    uint at = SKY_DIST_CONDITIONAL + band * (SKY_DIST_COLUMNS + 1u);
    uint column = SkyFind(at, SKY_DIST_COLUMNS, u2);
    float c0 = SkyDist[at + column], c1 = SkyDist[at + column + 1u];
    float v2 = (u2 - c0) / (c1 - c0);

    float e0 = SkyDist[band], e1 = SkyDist[band + 1u];
    float cosTheta = e0 + v1 * (e1 - e0);
    float phi = (float(column) + v2) * SKY_DIST_COLUMN_WIDTH;
    float sinTheta = sqrt(max(1.0 - cosTheta * cosTheta, 0.0));
    float2 a, b;
    SkySunAxes(a, b);
    float2 horizontal = cos(phi) * a + (u3 < 0.5 ? 1.0 : -1.0) * sin(phi) * b;
    pdf = (m1 - m0) * (c1 - c0) / (2.0 * SKY_DIST_COLUMN_WIDTH * (e0 - e1));
    return float3(sinTheta * horizontal.x, cosTheta, sinTheta * horizontal.y);
}

// The cell a unit direction falls in. C# reference: SkyDistribution.CellOf.
void SkyCellOf(float3 dir, out uint band, out uint column)
{
    float y = clamp(dir.y, -1.0, 1.0);
    band = min(uint(acos(y) * (float(SKY_DIST_BANDS) / ATM_PI)), SKY_DIST_BANDS - 1u);
    if (band > 0u && y > SkyDist[band])
        band--;
    else if (band < SKY_DIST_BANDS - 1u && y < SkyDist[band + 1u])
        band++;
    float2 a, b;
    SkySunAxes(a, b);
    float phi = atan2(abs(dot(dir.xz, b)), dot(dir.xz, a));
    column = min(uint(max(phi, 0.0) / SKY_DIST_COLUMN_WIDTH), SKY_DIST_COLUMNS - 1u);
}

// The pdf in solid angle with which SampleSky draws the unit direction `dir`. C# reference: SkyDistribution.Pdf.
float SkyPdf(float3 dir)
{
    uint band, column;
    SkyCellOf(dir, band, column);
    return SkyCellPdf(band, column);
}

// The cosine of band edge `edge` (0 the zenith, SKY_DIST_BANDS the nadir), exact at the poles and the horizon: what the
// distribution stores, computed here because the luminance is measured before the distribution exists.
// C# reference: SkyDistribution.EdgeCos.
float SkyEdgeCos(uint edge)
{
    if (edge == 0u)
        return 1.0;
    if (edge == SKY_DIST_BANDS)
        return -1.0;
    if (2u * edge == SKY_DIST_BANDS)
        return 0.0;
    return cos(ATM_PI * float(edge) / float(SKY_DIST_BANDS));
}

// Sub-sample (a, b) of a cell: (cos θ, relative azimuth), the midpoints of a SKY_DIST_SUB² grid uniform in solid angle.
// C# reference: SkyDistribution.CellPoint.
float2 SkyCellPoint(uint band, uint column, uint a, uint b)
{
    float c0 = SkyEdgeCos(band), c1 = SkyEdgeCos(band + 1u);
    return float2(c0 + (float(a) + 0.5) / float(SKY_DIST_SUB) * (c1 - c0),
                  (float(column) + (float(b) + 0.5) / float(SKY_DIST_SUB)) * SKY_DIST_COLUMN_WIDTH);
}
