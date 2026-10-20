using System.Globalization;
using System.Numerics;
using Vista.Rendering;

namespace Vista.Atmosphere;

/// <summary>
/// The LUT sky against the reference atmosphere, wavelength band by band (vista-plan §10 M1.3 verify: "the error
/// per wavelength band recorded; target: a few percent"). <c>Vista.App --atmosphere-check</c> runs it on the GPU:
/// for each sun elevation, each direction of <see cref="Directions"/> and each of the 50 wavelengths, the LUT's
/// radiance (exactly what a camera ray reads) and the mean of many reference paths (delta tracking through the
/// same air). This class holds the pure part: the directions, the bands, and the aggregation.
/// </summary>
public static class SkyCheck
{
    /// <summary>The sun elevations (degrees, as rendered) the plan asks for.</summary>
    public static IReadOnlyList<double> DefaultSunElevations { get; } = [30.0, 5.0, -2.0, -6.0];

    /// <summary>View elevations above the horizontal, degrees.</summary>
    public static IReadOnlyList<double> ViewElevations { get; } = [0.5, 1.0, 2.0, 4.0, 8.0, 15.0, 30.0, 50.0, 75.0];

    /// <summary>
    /// View elevations below the horizontal, degrees, for <c>--below-horizon</c>: from a high observer the first few
    /// still see sky above the dipped horizon, the rest the land beyond the tile through a long path of air.
    /// </summary>
    public static IReadOnlyList<double> BelowElevations { get; } = [-1.0, -2.5, -4.0, -8.0, -15.0, -30.0];

    /// <summary>View azimuths relative to the sun, degrees (0 toward it).</summary>
    public static IReadOnlyList<double> ViewAzimuths { get; } = [0.0, 15.0, 45.0, 90.0, 135.0, 180.0];

    /// <summary>The wavelength bands errors are reported in, nm: [low, high).</summary>
    public static IReadOnlyList<(double Low, double High)> Bands { get; } =
        [(400.0, 460.0), (460.0, 510.0), (510.0, 560.0), (560.0, 600.0), (600.0, 660.0)];

    /// <summary>The sun direction of a check at <paramref name="elevationDegrees"/>: due north (+Z), so azimuths are relative to it.</summary>
    public static Vector3 SunDirection(double elevationDegrees)
    {
        double e = elevationDegrees * Math.PI / 180.0;
        return new Vector3(0f, (float)Math.Sin(e), (float)Math.Cos(e));
    }

    /// <summary>Every (view elevation, relative azimuth) direction, elevation-major; <paramref name="belowHorizon"/> uses <see cref="BelowElevations"/>.</summary>
    public static IReadOnlyList<(double Elevation, double Azimuth, Vector3 Direction)> Directions(bool belowHorizon = false)
    {
        var list = new List<(double, double, Vector3)>();
        foreach (double e in belowHorizon ? BelowElevations : ViewElevations)
        {
            foreach (double a in ViewAzimuths)
            {
                double er = e * Math.PI / 180.0, ar = a * Math.PI / 180.0;
                list.Add((e, a, new Vector3((float)(Math.Sin(ar) * Math.Cos(er)), (float)Math.Sin(er), (float)(Math.Cos(ar) * Math.Cos(er)))));
            }
        }
        return list;
    }

    /// <summary>
    /// Aggregates one elevation's raw sums: <paramref name="sums"/> holds 4 floats per (direction, wavelength,
    /// chunk) — Σ L, Σ L², the LUT's L, 0 — over <paramref name="chunks"/> chunks of <paramref name="pathsPerChunk"/>
    /// paths; <paramref name="wavelengthsNm"/> and <paramref name="luminanceWeights"/> (the CIE ȳ of each entry) give
    /// the bands and the luminance.
    /// </summary>
    public static SkyCheckElevation Aggregate(double sunElevation, ReadOnlySpan<float> sums, int chunks, int pathsPerChunk,
        IReadOnlyList<float> wavelengthsNm, IReadOnlyList<float> luminanceWeights, bool belowHorizon = false)
    {
        ArgumentNullException.ThrowIfNull(wavelengthsNm);
        ArgumentNullException.ThrowIfNull(luminanceWeights);
        IReadOnlyList<(double Elevation, double Azimuth, Vector3 Direction)> directions = Directions(belowHorizon);
        int n = wavelengthsNm.Count;
        if (sums.Length != directions.Count * n * chunks * 4)
            throw new ArgumentException($"Expected {directions.Count * n * chunks * 4} sums, got {sums.Length}.", nameof(sums));
        double paths = (double)chunks * pathsPerChunk;

        var lut = new double[directions.Count, n];
        var reference = new double[directions.Count, n];
        var variance = new double[directions.Count, n];
        for (int d = 0; d < directions.Count; d++)
        {
            for (int l = 0; l < n; l++)
            {
                double s = 0.0, s2 = 0.0;
                for (int c = 0; c < chunks; c++)
                {
                    int at = ((d * n + l) * chunks + c) * 4;
                    s += sums[at];
                    s2 += sums[at + 1];
                }
                lut[d, l] = sums[((d * n + l) * chunks) * 4 + 2];
                double mean = s / paths;
                reference[d, l] = mean;
                variance[d, l] = Math.Max(s2 / paths - mean * mean, 0.0) / paths; // of the mean
            }
        }

        var bands = new List<SkyCheckBand>();
        foreach ((double low, double high) in Bands)
        {
            int[] members = Enumerable.Range(0, n).Where(l => wavelengthsNm[l] >= low && wavelengthsNm[l] < high).ToArray();
            if (members.Length == 0)
                continue;
            var errors = new List<(double Error, double StandardError, int Direction)>();
            for (int d = 0; d < directions.Count; d++)
            {
                double l0 = members.Average(l => lut[d, l]);
                double r0 = members.Average(l => reference[d, l]);
                double se = Math.Sqrt(members.Sum(l => variance[d, l])) / members.Length;
                if (r0 > 0.0)
                    errors.Add((l0 / r0 - 1.0, se / r0, d));
            }
            if (errors.Count == 0)
                continue; // no light in this band anywhere (single scattering alone, in the Earth's shadow)
            (double worst, _, int at) = errors.MaxBy(e => Math.Abs(e.Error));
            bands.Add(new SkyCheckBand(
                string.Create(CultureInfo.InvariantCulture, $"{low:0}–{high:0} nm"),
                members.Length,
                errors.Average(e => Math.Abs(e.Error)),
                worst,
                directions[at].Elevation,
                directions[at].Azimuth,
                errors.Average(e => e.StandardError)));
        }

        var samples = new List<SkyCheckDirection>();
        for (int d = 0; d < directions.Count; d++)
        {
            double weight = 0.0, yLut = 0.0, yRef = 0.0, yVar = 0.0;
            for (int l = 0; l < n; l++)
            {
                double w = luminanceWeights[l];
                weight += w;
                yLut += w * lut[d, l];
                yRef += w * reference[d, l];
                yVar += w * w * variance[d, l];
            }
            samples.Add(new SkyCheckDirection(directions[d].Elevation, directions[d].Azimuth,
                yLut / weight, yRef / weight, Math.Sqrt(yVar) / weight));
        }

        double[] luminanceErrors = samples.Where(s => s.ReferenceLuminance > 0.0).Select(s => s.LutLuminance / s.ReferenceLuminance - 1.0).ToArray();
        return new SkyCheckElevation(
            sunElevation,
            (long)paths,
            bands,
            luminanceErrors.Length == 0 ? 0.0 : luminanceErrors.Average(Math.Abs),
            luminanceErrors.Length == 0 ? 0.0 : luminanceErrors.MaxBy(Math.Abs),
            samples);
    }

    /// <summary>The CIE ȳ weight of each hero wavelength (<see cref="IntegratorTables.Cie"/>'s second channel).</summary>
    public static float[] LuminanceWeights(IntegratorTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        var weights = new float[tables.Count];
        for (int i = 0; i < tables.Count; i++)
            weights[i] = tables.Cie[i * 4 + 1];
        return weights;
    }
}

/// <summary>One sun elevation's comparison (of the sky, or of the air along segments of one length).</summary>
/// <param name="SunElevationDegrees">The sun's elevation, as rendered.</param>
/// <param name="PathsPerValue">Reference paths behind each (direction, wavelength) value.</param>
/// <param name="Bands">The error per wavelength band.</param>
/// <param name="MeanAbsoluteLuminanceError">Mean |LUT/reference − 1| of the luminance over the directions.</param>
/// <param name="WorstLuminanceError">The luminance error of largest magnitude.</param>
/// <param name="Directions">Every direction's luminance, LUT and reference.</param>
public sealed record SkyCheckElevation(
    double SunElevationDegrees,
    long PathsPerValue,
    IReadOnlyList<SkyCheckBand> Bands,
    double MeanAbsoluteLuminanceError,
    double WorstLuminanceError,
    IReadOnlyList<SkyCheckDirection> Directions)
{
    /// <summary>
    /// The length of the segments compared, m (vista-plan §10 M1.4: the light the air scatters toward the observer along
    /// a segment that ends on a surface, or on the ground sphere if it gets there first); 0 for the whole sky.
    /// </summary>
    public double SegmentLengthMetres { get; init; }
}

/// <summary>The LUT's error in one wavelength band, over every direction.</summary>
/// <param name="Band">The band, e.g. "400–460 nm".</param>
/// <param name="Wavelengths">How many of the 50 hero wavelengths fall in it.</param>
/// <param name="MeanAbsoluteError">Mean |LUT/reference − 1|.</param>
/// <param name="WorstError">The error of largest magnitude, signed.</param>
/// <param name="WorstElevation">Its view elevation, degrees.</param>
/// <param name="WorstAzimuth">Its azimuth from the sun, degrees.</param>
/// <param name="MeanStandardError">The reference's own mean relative standard error (the noise floor of the comparison).</param>
public sealed record SkyCheckBand(string Band, int Wavelengths, double MeanAbsoluteError, double WorstError, double WorstElevation, double WorstAzimuth, double MeanStandardError);

/// <summary>One direction's luminance (ȳ-weighted spectral radiance, W·m⁻²·sr⁻¹·nm⁻¹).</summary>
/// <param name="ElevationDegrees">View elevation.</param>
/// <param name="AzimuthDegrees">Azimuth from the sun.</param>
/// <param name="LutLuminance">From the LUTs.</param>
/// <param name="ReferenceLuminance">From the reference.</param>
/// <param name="ReferenceStandardError">The reference's standard error.</param>
public sealed record SkyCheckDirection(double ElevationDegrees, double AzimuthDegrees, double LutLuminance, double ReferenceLuminance, double ReferenceStandardError);
