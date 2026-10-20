using System.Numerics;
using Vista.Atmosphere;
using Vista.Scene;

namespace Vista.Rendering;

/// <summary>
/// The sky as a light (vista-plan §5.1, §10 M1.5): a piecewise-constant distribution of directions over the whole
/// sphere, proportional to the sky's luminance, so next-event estimation can aim shadow rays where the sky is bright —
/// the aureole round a low sun, the glowing horizon at golden hour and in twilight — instead of leaving the sky to the
/// cosine-weighted bounce alone. The integrator samples it and evaluates its pdf (<c>Shaders/VistaSkyLight.hlsli</c>);
/// this class builds it, and is the C# reference of both.
///
/// <para>Cells: <see cref="ZenithBands"/> bands evenly spaced in zenith angle (the horizon is a band edge), each cut into
/// <see cref="AzimuthBands"/> columns of azimuth relative to the sun, 0 (toward it) to π. The sky is mirror-symmetric
/// about the sun's vertical plane (the sky-view LUT keeps only that half), so a cell stands for both mirror halves and a
/// sample picks one at random. Within a cell directions are uniform in solid angle (uniform in cos θ and in φ). A cell's
/// weight is its mean luminance — measured at <see cref="SubSamples"/>² points by the GPU (<c>CSSkyLuminance</c>,
/// reference: <see cref="CellLuminance"/>) and averaged over the sky-view LUT's terrain slices, the sky bounce rays see —
/// times its solid angle, and times <see cref="BelowHorizonWeight"/> under the horizon; <see cref="UniformFraction"/> of the
/// probability is spread uniformly over the sphere, so every direction the sky lights has a density even where the
/// measurement saw none (a surface with no bounce after it relies on the sky's samples alone).</para>
///
/// <para>Sampling uses three random numbers: the first picks the band (marginal CDF) and, rescaled, cos θ within it; the
/// second the column (that band's conditional CDF) and φ within it; the third the mirror half. One number per CDF keeps
/// the chance of each cell well above the 2⁻²⁴ steps of the shader's random floats, so the probability a cell is chosen is
/// the CDF difference the pdf uses. CDFs are stored as floats and the pdf is computed from the stored values, so sampling
/// and pdf agree exactly in the shader.</para>
///
/// <para>Directions are in the camera's frame (+Y up), the frame the sky view is read in.</para>
/// </summary>
public sealed class SkyDistribution
{
    /// <summary>Bands of zenith angle, evenly spaced from the zenith (0) to the nadir (π): 1.41° each.</summary>
    public const int ZenithBands = 128;

    /// <summary>Columns of azimuth relative to the sun, evenly spaced from 0 to π: 2.81° each.</summary>
    public const int AzimuthBands = 64;

    /// <summary>Points per axis at which a cell's mean luminance is measured (stratified midpoints).</summary>
    public const int SubSamples = 4;

    /// <summary>Cells in the distribution.</summary>
    public const int Cells = ZenithBands * AzimuthBands;

    /// <summary>The part of the probability spread uniformly over the sphere (the floor under every direction).</summary>
    public const double UniformFraction = 0.02;

    /// <summary>
    /// What a cell below the horizon weighs, relative to its luminance: there the sky view holds the land beyond the
    /// terrain tile, which a surface on the terrain mostly cannot see past the terrain itself. Measured (vista-plan §10
    /// M1.5): 1 wasted shadow rays at dawn and golden hour, 0 starved the daylit land seen from high up; ¼ took most of
    /// 0's gain at golden hour and kept the day's variance where 1 had it.
    /// </summary>
    public const double BelowHorizonWeight = 0.25;

    /// <summary>Floats before the marginal CDF: the cosines of the band edges.</summary>
    public const int MarginalOffset = ZenithBands + 1;

    /// <summary>Floats before the first conditional CDF.</summary>
    public const int ConditionalOffset = MarginalOffset + ZenithBands + 1;

    /// <summary>Floats in <see cref="Floats"/>: band edges, the marginal CDF, one conditional CDF per band.</summary>
    public const int FloatCount = ConditionalOffset + ZenithBands * (AzimuthBands + 1);

    /// <summary>Width of an azimuth column, radians.</summary>
    public const double ColumnWidth = Math.PI / AzimuthBands;

    private SkyDistribution(float[] floats, bool hasLight, double luminance)
    {
        Floats = floats;
        HasLight = hasLight;
        MeanLuminance = luminance;
    }

    /// <summary>
    /// The table the shader reads: <see cref="ZenithBands"/> + 1 band-edge cosines (1 at the zenith down to −1 at the
    /// nadir), the marginal CDF over bands (<see cref="ZenithBands"/> + 1 values from 0 to 1), then per band its
    /// conditional CDF over columns (<see cref="AzimuthBands"/> + 1 values from 0 to 1).
    /// </summary>
    public float[] Floats { get; }

    /// <summary>
    /// True when the measured sky has any luminance; false at night (and then the distribution is uniform, and the
    /// integrator leaves the sky to the bounce alone).
    /// </summary>
    public bool HasLight { get; }

    /// <summary>The measured sky's mean luminance over the sphere (the weights' units), for the record.</summary>
    public double MeanLuminance { get; }

    /// <summary>The cosine of band edge <paramref name="edge"/> (0 the zenith, <see cref="ZenithBands"/> the nadir), exact at the poles and the horizon.</summary>
    public static double EdgeCos(int edge)
    {
        if (edge < 0 || edge > ZenithBands)
            throw new ArgumentOutOfRangeException(nameof(edge), edge, "No such band edge.");
        if (edge == 0)
            return 1.0;
        if (edge == ZenithBands)
            return -1.0;
        if (2 * edge == ZenithBands)
            return 0.0;
        return Math.Cos(Math.PI * edge / ZenithBands);
    }

    /// <summary>
    /// The direction (cos θ, relative azimuth φ) of sub-sample (<paramref name="a"/>, <paramref name="b"/>) of cell
    /// (<paramref name="band"/>, <paramref name="column"/>): the midpoints of a <see cref="SubSamples"/>² grid, uniform in
    /// solid angle, so their mean is the cell's mean.
    /// </summary>
    public static (double CosTheta, double Phi) CellPoint(int band, int column, int a, int b)
    {
        double c0 = EdgeCos(band), c1 = EdgeCos(band + 1);
        double cosTheta = c0 + (a + 0.5) / SubSamples * (c1 - c0);
        double phi = (column + (b + 0.5) / SubSamples) * ColumnWidth;
        return (cosTheta, phi);
    }

    /// <summary>
    /// The luminance weight of hero wavelength <paramref name="lambda"/>: its white-balanced ȳ times the solar irradiance
    /// above the air, so that Σ weight · (sky-view value) is the sky's luminance in the integrator's Y units.
    /// </summary>
    public static double LuminanceWeight(IntegratorTables integrator, AtmosphereTables atmosphere, int lambda)
    {
        ArgumentNullException.ThrowIfNull(integrator);
        ArgumentNullException.ThrowIfNull(atmosphere);
        return integrator.Cie[lambda * 4 + 1] * atmosphere.Sun[lambda * 4 + 1];
    }

    /// <summary>
    /// The C# reference of <c>CSSkyLuminance</c>: the mean over a cell's <see cref="SubSamples"/>² points of
    /// Σ_λ weight_λ · (smooth + HG(ν) · haze) read from <paramref name="skyView"/> in slice <paramref name="slice"/>, with
    /// <paramref name="weights"/> from <see cref="LuminanceWeight"/> (as floats, as uploaded).
    /// </summary>
    public static double CellLuminance(ReadOnlySpan<float> skyView, AtmosphereFrame frame, ReadOnlySpan<float> weights, int slice, int band, int column)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (weights.Length != AtmosphereLuts.Wavelengths)
            throw new ArgumentException($"Expected {AtmosphereLuts.Wavelengths} weights.", nameof(weights));
        double muS = frame.SunCosZenith;
        double sinS = Math.Sqrt(Math.Max(1.0 - muS * muS, 0.0));
        double g = frame.Model.AerosolAsymmetry;
        double sum = 0.0;
        for (int a = 0; a < SubSamples; a++)
        {
            for (int b = 0; b < SubSamples; b++)
            {
                (double mu, double phi) = CellPoint(band, column, a, b);
                double sinTheta = Math.Sqrt(Math.Max(1.0 - mu * mu, 0.0));
                double phase = AirOptics.HenyeyGreenstein(sinTheta * Math.Cos(phi) * sinS + mu * muS, g);
                for (int lambda = 0; lambda < AtmosphereLuts.Wavelengths; lambda++)
                {
                    (double smooth, double mie) = AtmosphereLuts.SampleSkyView(skyView, frame, lambda, slice, mu, phi);
                    sum += weights[lambda] * (smooth + phase * mie);
                }
            }
        }
        return sum / (SubSamples * SubSamples);
    }

    /// <summary>
    /// Builds the distribution from each cell's mean luminance (<see cref="Cells"/> values, band-major: cell
    /// <c>band · AzimuthBands + column</c>). Negative or non-finite luminance is an error.
    /// </summary>
    public static SkyDistribution Build(ReadOnlySpan<double> cellLuminance)
    {
        if (cellLuminance.Length != Cells)
            throw new ArgumentException($"Expected {Cells} cell luminances, got {cellLuminance.Length}.", nameof(cellLuminance));

        // Weight = mean luminance × solid angle (× BelowHorizonWeight under the horizon); Δφ is the same for every cell,
        // so Δcos stands for the solid angle.
        var deltaCos = new double[ZenithBands];
        for (int i = 0; i < ZenithBands; i++)
            deltaCos[i] = EdgeCos(i) - EdgeCos(i + 1);
        var weight = new double[Cells];
        double total = 0.0, light = 0.0;
        for (int cell = 0; cell < Cells; cell++)
        {
            double l = cellLuminance[cell];
            if (!double.IsFinite(l) || l < 0.0)
                throw new ArgumentException($"Cell {cell} has luminance {l}.", nameof(cellLuminance));
            int band = cell / AzimuthBands;
            weight[cell] = l * deltaCos[band] * (band >= ZenithBands / 2 ? BelowHorizonWeight : 1.0);
            total += weight[cell];
            light += l * deltaCos[band];
        }
        bool hasLight = total > 0.0;
        double lit = hasLight ? 1.0 - UniformFraction : 0.0;

        // The probability of a cell (both mirror halves): its share of the light, plus its share of the uniform floor
        // (a cell's solid angle over the sphere: 2·Δcos·Δφ / 4π = Δcos / 2A).
        var probability = new double[Cells];
        for (int cell = 0; cell < Cells; cell++)
        {
            double floor = deltaCos[cell / AzimuthBands] / (2.0 * AzimuthBands);
            double share = hasLight ? weight[cell] / total : 0.0;
            probability[cell] = lit * share + (1.0 - lit) * floor;
        }

        var floats = new float[FloatCount];
        for (int e = 0; e <= ZenithBands; e++)
            floats[e] = (float)EdgeCos(e);
        double marginal = 0.0;
        for (int i = 0; i < ZenithBands; i++)
        {
            floats[MarginalOffset + i] = (float)marginal;
            double row = 0.0;
            for (int j = 0; j < AzimuthBands; j++)
                row += probability[i * AzimuthBands + j];
            double conditional = 0.0;
            int at = ConditionalOffset + i * (AzimuthBands + 1);
            for (int j = 0; j < AzimuthBands; j++)
            {
                floats[at + j] = (float)(conditional / row);
                conditional += probability[i * AzimuthBands + j];
            }
            floats[at + AzimuthBands] = 1f;
            marginal += row;
        }
        floats[MarginalOffset + ZenithBands] = 1f;
        // 4π · mean radiance = Σ L̄·Ω = 2Δφ · Σ L̄·Δcos.
        return new SkyDistribution(floats, hasLight, light * 2.0 * ColumnWidth / (4.0 * Math.PI));
    }

    /// <summary>
    /// The horizontal axes of the sun's frame: <c>a</c> toward the sun's azimuth (+X when the sun stands at the zenith),
    /// <c>b</c> a quarter turn from it; relative azimuth φ is measured from <c>a</c>, either way round.
    /// </summary>
    public static (double AX, double AZ, double BX, double BZ) SunAxes(Vector3 toSun)
    {
        double x = toSun.X, z = toSun.Z;
        double length = Math.Sqrt(x * x + z * z);
        (double ax, double az) = length > 1e-6 ? (x / length, z / length) : (1.0, 0.0);
        return (ax, az, -az, ax);
    }

    /// <summary>
    /// The direction for random numbers <paramref name="u1"/>, <paramref name="u2"/>, <paramref name="u3"/> in [0, 1),
    /// with its pdf in solid angle — the C# reference of the shader's <c>SampleSky</c>, on the stored floats.
    /// </summary>
    public (Vector3D Direction, double Pdf) Sample(double u1, double u2, double u3, Vector3 toSun)
    {
        int band = Find(MarginalOffset, ZenithBands, u1);
        double m0 = Floats[MarginalOffset + band], m1 = Floats[MarginalOffset + band + 1];
        double v1 = (u1 - m0) / (m1 - m0);
        int at = ConditionalOffset + band * (AzimuthBands + 1);
        int column = Find(at, AzimuthBands, u2);
        double c0 = Floats[at + column], c1 = Floats[at + column + 1];
        double v2 = (u2 - c0) / (c1 - c0);

        double e0 = Floats[band], e1 = Floats[band + 1];
        double cosTheta = e0 + v1 * (e1 - e0);
        double phi = (column + v2) * ColumnWidth;
        double sign = u3 < 0.5 ? 1.0 : -1.0;
        double sinTheta = Math.Sqrt(Math.Max(1.0 - cosTheta * cosTheta, 0.0));
        (double ax, double az, double bx, double bz) = SunAxes(toSun);
        double cx = Math.Cos(phi), sx = sign * Math.Sin(phi);
        var direction = new Vector3D(sinTheta * (cx * ax + sx * bx), cosTheta, sinTheta * (cx * az + sx * bz));
        return (direction, CellPdf(band, column));
    }

    /// <summary>The pdf in solid angle of <paramref name="direction"/> (unit) — the C# reference of the shader's <c>SkyPdf</c>.</summary>
    public double Pdf(Vector3D direction, Vector3 toSun)
    {
        (int band, int column) = CellOf(direction, toSun);
        return CellPdf(band, column);
    }

    /// <summary>The cell <paramref name="direction"/> (unit) falls in.</summary>
    public (int Band, int Column) CellOf(Vector3D direction, Vector3 toSun)
    {
        double y = Math.Clamp(direction.Y, -1.0, 1.0);
        // acos lands in the right band or, at an edge, one off; the stored edges decide, as the sampler's do.
        int band = Math.Clamp((int)(Math.Acos(y) / Math.PI * ZenithBands), 0, ZenithBands - 1);
        if (band > 0 && y > Floats[band])
            band--;
        else if (band < ZenithBands - 1 && y < Floats[band + 1])
            band++;
        (double ax, double az, double bx, double bz) = SunAxes(toSun);
        double along = direction.X * ax + direction.Z * az;
        double across = Math.Abs(direction.X * bx + direction.Z * bz);
        double phi = Math.Atan2(across, along);
        int column = Math.Clamp((int)(phi / ColumnWidth), 0, AzimuthBands - 1);
        return (band, column);
    }

    /// <summary>
    /// The pdf in solid angle of any direction in cell (<paramref name="band"/>, <paramref name="column"/>): its
    /// probability (from the stored CDFs) over the solid angle of both mirror halves.
    /// </summary>
    public double CellPdf(int band, int column)
    {
        double p = CellProbability(band, column);
        return p / (2.0 * ColumnWidth * (Floats[band] - Floats[band + 1]));
    }

    /// <summary>The probability that a sample lands in cell (<paramref name="band"/>, <paramref name="column"/>), as the stored floats give it.</summary>
    public double CellProbability(int band, int column)
    {
        float marginal = Floats[MarginalOffset + band + 1] - Floats[MarginalOffset + band];
        int at = ConditionalOffset + band * (AzimuthBands + 1);
        float conditional = Floats[at + column + 1] - Floats[at + column];
        return (double)marginal * conditional;
    }

    /// <summary>The largest index i in [0, count) with cdf[i] ≤ u (cdf at <paramref name="offset"/>, count + 1 values from 0 to 1).</summary>
    private int Find(int offset, int count, double u)
    {
        int lo = 0, hi = count;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (Floats[offset + mid] <= u)
                lo = mid;
            else
                hi = mid;
        }
        return lo;
    }
}
