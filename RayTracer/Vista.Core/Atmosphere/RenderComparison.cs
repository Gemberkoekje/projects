using System.Globalization;

namespace Vista.Atmosphere;

/// <summary>
/// One render against another of the same view, by distance (vista-plan §10 M1.4 verify: "the error against the
/// reference on a layered-ridges view and on a shadowed valley at golden hour"). <c>Vista.App --atmosphere-compare</c>
/// renders a view with the LUTs and with the reference atmosphere; this class holds the pure part: pixels binned by the
/// distance to what their first sample hit, and per bin the mean luminance of each render with the noise of those means.
/// Means over many pixels average the path tracer's noise away, so even a reference render shows its bias per band of
/// distance — the layered ridges, the sky.
/// </summary>
public static class RenderComparison
{
    /// <summary>The distance bins, m: [low, high). Sky pixels (no hit) form their own last bin.</summary>
    public static IReadOnlyList<(double Low, double High)> DepthBins { get; } =
        [(0.0, 1000.0), (1000.0, 2000.0), (2000.0, 4000.0), (4000.0, 8000.0), (8000.0, 16_000.0), (16_000.0, double.PositiveInfinity)];

    /// <summary>
    /// Compares <paramref name="candidate"/> with <paramref name="reference"/> (mean CIE XYZ per pixel, 3 floats each) by
    /// distance bin, the distances from <paramref name="depth"/> (+∞ for sky). The variances (of each pixel's mean
    /// luminance, <c>Aov.Variance</c>) give each bin's noise.
    /// </summary>
    public static IReadOnlyList<ComparisonBin> Compare(ReadOnlySpan<float> depth, ReadOnlySpan<float> candidate, ReadOnlySpan<float> candidateVariance,
        ReadOnlySpan<float> reference, ReadOnlySpan<float> referenceVariance)
    {
        int pixels = depth.Length;
        if (candidate.Length != pixels * 3 || reference.Length != pixels * 3 || candidateVariance.Length != pixels || referenceVariance.Length != pixels)
            throw new ArgumentException("Every buffer must cover the same pixels.");
        int bins = DepthBins.Count + 1;
        var count = new int[bins];
        var sumC = new double[bins];
        var sumR = new double[bins];
        var varC = new double[bins];
        var varR = new double[bins];
        for (int p = 0; p < pixels; p++)
        {
            int bin = BinOf(depth[p]);
            count[bin]++;
            sumC[bin] += candidate[p * 3 + 1];
            sumR[bin] += reference[p * 3 + 1];
            varC[bin] += candidateVariance[p];
            varR[bin] += referenceVariance[p];
        }
        var result = new List<ComparisonBin>(bins);
        for (int b = 0; b < bins; b++)
        {
            if (count[b] == 0)
                continue;
            double n = count[b];
            result.Add(new ComparisonBin(
                b < DepthBins.Count ? Name(DepthBins[b]) : "sky",
                count[b],
                sumC[b] / n,
                sumR[b] / n,
                Math.Sqrt(varC[b]) / n,
                Math.Sqrt(varR[b]) / n));
        }
        return result;
    }

    /// <summary>The bin of a pixel whose first hit is <paramref name="distance"/> metres away (the last bin for sky).</summary>
    public static int BinOf(float distance)
    {
        if (!float.IsFinite(distance))
            return DepthBins.Count;
        for (int b = 0; b < DepthBins.Count; b++)
        {
            if (distance < DepthBins[b].High)
                return b;
        }
        return DepthBins.Count - 1;
    }

    private static string Name((double Low, double High) bin) => double.IsPositiveInfinity(bin.High)
        ? string.Create(CultureInfo.InvariantCulture, $"{bin.Low / 1000.0:0}+ km")
        : string.Create(CultureInfo.InvariantCulture, $"{bin.Low / 1000.0:0}–{bin.High / 1000.0:0} km");
}

/// <summary>One distance bin of a comparison: mean luminance (ȳ-weighted spectral radiance) of each render and its noise.</summary>
/// <param name="Bin">The distances, e.g. "2–4 km", or "sky".</param>
/// <param name="Pixels">Pixels in the bin.</param>
/// <param name="CandidateLuminance">Mean luminance of the render being checked.</param>
/// <param name="ReferenceLuminance">Mean luminance of the reference.</param>
/// <param name="CandidateStandardError">Standard error of the candidate's mean.</param>
/// <param name="ReferenceStandardError">Standard error of the reference's mean.</param>
public sealed record ComparisonBin(string Bin, int Pixels, double CandidateLuminance, double ReferenceLuminance, double CandidateStandardError, double ReferenceStandardError)
{
    /// <summary>Candidate ÷ reference − 1.</summary>
    public double Error => ReferenceLuminance > 0.0 ? CandidateLuminance / ReferenceLuminance - 1.0 : 0.0;

    /// <summary>The error's own noise (both means' standard errors, relative to the reference).</summary>
    public double ErrorNoise => ReferenceLuminance > 0.0
        ? Math.Sqrt(CandidateStandardError * CandidateStandardError + ReferenceStandardError * ReferenceStandardError) / ReferenceLuminance
        : 0.0;
}
