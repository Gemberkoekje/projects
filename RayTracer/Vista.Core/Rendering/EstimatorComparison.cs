namespace Vista.Rendering;

/// <summary>
/// Two estimators of the same image, pixel by pixel (vista-plan §10 M1.5 verify: "at 4096 spp the new estimator matches the
/// old one within the per-pixel standard error; variance at equal spp … drops by a measured factor").
/// <c>Vista.App --sampling-compare</c> renders one view several times with each estimator, every render on its own random
/// stream (<see cref="RenderSettings.RandomStream"/>), and collects them in a <see cref="ReplicateStats"/> each; this class
/// holds the pure part.
///
/// <para>Why replicates and not the variance output: a render's per-pixel variance (<c>Aov.Variance</c>) treats every sample
/// as independent, but the hero wavelength follows a fixed cycle, the same in every render, so the spread between
/// wavelengths is counted as noise when it is in fact stratified away. At 4096 spp it overstated the noise of a pixel's
/// mean so much that z-scores came out with an RMS of 0.63, and it hid most of the difference between estimators under a
/// term both share. The spread between independent renders is the noise an image of that many samples really has.</para>
///
/// <para>Unbiased: each pixel's difference in mean luminance over its standard error, <c>z = (Y_c − Y_b) / √(v_c + v_b)</c>.
/// If both estimators have the same mean, z is close to a standard normal (a Student t with about twice the replicates'
/// degrees of freedom): its mean near 0, its RMS near 1, and |z| &gt; 3 in about 0.3% of the pixels. A bias shows as a mean
/// z away from 0 over many pixels, or an excess in the tails; the whole image's mean luminance is compared too.</para>
///
/// <para>Better: the variance of one render (of the replicates' sample count) summed over the pixels that see the terrain
/// (sky pixels are the camera ray alone, the same in both), baseline over candidate — how many times fewer samples the
/// candidate needs for the same noise — and the same for the relative variance (each pixel's variance over its mean
/// squared), which counts a dark shadow's noise as much as a bright slope's. With each estimator's time per sample it gives
/// the gain in efficiency, 1 / (variance × time).</para>
/// </summary>
public static class EstimatorComparison
{
    /// <summary>
    /// The pixels whose first hit is terrain and whose eight neighbours' are too (<paramref name="depth"/>: sample 0's
    /// distance, +∞ for sky). A pixel on the skyline mixes bright sky and dark rock under its jitter: that noise is the same
    /// for any light sampling and, the sky being far brighter, it would swamp the sum the comparison is about.
    /// </summary>
    public static bool[] InteriorTerrain(ReadOnlySpan<float> depth, int width, int height)
    {
        if (width <= 0 || height <= 0 || depth.Length != width * height)
            throw new ArgumentException("The depth buffer must be width × height.", nameof(depth));
        var mask = new bool[depth.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool inside = true;
                for (int dy = -1; dy <= 1 && inside; dy++)
                {
                    for (int dx = -1; dx <= 1 && inside; dx++)
                    {
                        int nx = Math.Clamp(x + dx, 0, width - 1), ny = Math.Clamp(y + dy, 0, height - 1);
                        inside = float.IsFinite(depth[ny * width + nx]);
                    }
                }
                mask[y * width + x] = inside;
            }
        }
        return mask;
    }

    /// <summary>
    /// Compares <paramref name="candidate"/> with <paramref name="baseline"/>; <paramref name="terrain"/> marks the pixels
    /// whose first hit is terrain.
    /// </summary>
    public static EstimatorAgreement Compare(ReplicateStats candidate, ReplicateStats baseline, ReadOnlySpan<bool> terrain)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(baseline);
        int pixels = terrain.Length;
        if (candidate.Pixels != pixels || baseline.Pixels != pixels)
            throw new ArgumentException("Both estimators must cover the same pixels.");
        if (candidate.Count < 2 || baseline.Count < 2)
            throw new ArgumentException("Each estimator needs at least two replicates.");

        int compared = 0, beyond3 = 0, beyond4 = 0, terrainPixels = 0;
        double sumZ = 0.0, sumZ2 = 0.0, maxZ = 0.0;
        double sumC = 0.0, sumB = 0.0, noiseC = 0.0, noiseB = 0.0;
        double varC = 0.0, varB = 0.0, relC = 0.0, relB = 0.0;
        var ratios = new List<double>();
        for (int p = 0; p < pixels; p++)
        {
            double yc = candidate.Mean(p), yb = baseline.Mean(p);
            double vc = candidate.VarianceOfMean(p), vb = baseline.VarianceOfMean(p);
            sumC += yc;
            sumB += yb;
            noiseC += vc;
            noiseB += vb;
            if (terrain[p])
            {
                terrainPixels++;
                double rc = candidate.Variance(p), rb = baseline.Variance(p);
                varC += rc;
                varB += rb;
                double scale = 0.5 * (yc + yb);
                if (scale > 0.0)
                {
                    relC += rc / (scale * scale);
                    relB += rb / (scale * scale);
                }
                if (rc > 0.0)
                    ratios.Add(rb / rc);
            }
            double noise = vc + vb;
            if (noise <= 0.0)
                continue;
            double z = (yc - yb) / Math.Sqrt(noise);
            compared++;
            sumZ += z;
            sumZ2 += z * z;
            maxZ = Math.Max(maxZ, Math.Abs(z));
            if (Math.Abs(z) > 3.0)
                beyond3++;
            if (Math.Abs(z) > 4.0)
                beyond4++;
        }
        ratios.Sort();
        double median = ratios.Count == 0 ? 0.0
            : ratios.Count % 2 == 1 ? ratios[ratios.Count / 2] : 0.5 * (ratios[ratios.Count / 2 - 1] + ratios[ratios.Count / 2]);
        return new EstimatorAgreement(
            compared,
            compared > 0 ? sumZ / compared : 0.0,
            compared > 0 ? Math.Sqrt(sumZ2 / compared) : 0.0,
            maxZ,
            compared > 0 ? (double)beyond3 / compared : 0.0,
            compared > 0 ? (double)beyond4 / compared : 0.0,
            sumB > 0.0 ? sumC / sumB - 1.0 : 0.0,
            sumB > 0.0 ? Math.Sqrt(noiseC + noiseB) / sumB : 0.0,
            terrainPixels,
            varC > 0.0 ? varB / varC : 0.0,
            relC > 0.0 ? relB / relC : 0.0,
            median);
    }
}

/// <summary>
/// Per-pixel luminance statistics over independent renders of one view (replicates): each pixel's mean and the variance
/// between renders, in double precision.
/// </summary>
public sealed class ReplicateStats
{
    private readonly double[] _sum;
    private readonly double[] _sumSquares;

    /// <summary>Statistics for images of <paramref name="pixels"/> pixels.</summary>
    public ReplicateStats(int pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixels);
        _sum = new double[pixels];
        _sumSquares = new double[pixels];
    }

    /// <summary>Pixels per image.</summary>
    public int Pixels => _sum.Length;

    /// <summary>Renders added.</summary>
    public int Count { get; private set; }

    /// <summary>Adds one render's mean CIE XYZ per pixel (3 floats each); its Y counts. A non-finite Y counts as 0.</summary>
    public void Add(ReadOnlySpan<float> xyz)
    {
        if (xyz.Length != Pixels * 3)
            throw new ArgumentException($"Expected {Pixels * 3} floats, got {xyz.Length}.", nameof(xyz));
        for (int p = 0; p < Pixels; p++)
        {
            double y = float.IsFinite(xyz[p * 3 + 1]) ? xyz[p * 3 + 1] : 0.0;
            _sum[p] += y;
            _sumSquares[p] += y * y;
        }
        Count++;
    }

    /// <summary>The mean luminance of pixel <paramref name="p"/> over every render.</summary>
    public double Mean(int p) => Count > 0 ? _sum[p] / Count : 0.0;

    /// <summary>The variance of one render's luminance at pixel <paramref name="p"/> (between renders, unbiased).</summary>
    public double Variance(int p)
    {
        if (Count < 2)
            return 0.0;
        double mean = _sum[p] / Count;
        return Math.Max(_sumSquares[p] - Count * mean * mean, 0.0) / (Count - 1);
    }

    /// <summary>The variance of <see cref="Mean"/> at pixel <paramref name="p"/>.</summary>
    public double VarianceOfMean(int p) => Count > 0 ? Variance(p) / Count : 0.0;
}

/// <summary>How two estimators of one image agree, and how their noise compares (<see cref="EstimatorComparison"/>).</summary>
/// <param name="Pixels">Pixels with a z-score (some noise in at least one estimator).</param>
/// <param name="MeanZ">Mean z over those pixels: near 0 when both estimators have the same mean (its own noise ≈ 1/√Pixels).</param>
/// <param name="RmsZ">RMS z: near 1 when the means agree and the noise is measured right.</param>
/// <param name="MaxAbsZ">The largest |z|.</param>
/// <param name="FractionBeyond3">Fraction of pixels with |z| &gt; 3 (a standard normal: 0.27%).</param>
/// <param name="FractionBeyond4">Fraction with |z| &gt; 4 (a standard normal: 0.006%).</param>
/// <param name="MeanLuminanceError">The whole image's mean luminance, candidate ÷ baseline − 1.</param>
/// <param name="MeanLuminanceNoise">That error's own noise (one standard error).</param>
/// <param name="TerrainPixels">Pixels whose first hit is terrain.</param>
/// <param name="VarianceReduction">Σ baseline variance ÷ Σ candidate variance over terrain pixels, at equal samples.</param>
/// <param name="RelativeVarianceReduction">The same for each pixel's variance over its mean squared.</param>
/// <param name="MedianPixelVarianceReduction">The median over terrain pixels of baseline ÷ candidate variance.</param>
public sealed record EstimatorAgreement(
    int Pixels,
    double MeanZ,
    double RmsZ,
    double MaxAbsZ,
    double FractionBeyond3,
    double FractionBeyond4,
    double MeanLuminanceError,
    double MeanLuminanceNoise,
    int TerrainPixels,
    double VarianceReduction,
    double RelativeVarianceReduction,
    double MedianPixelVarianceReduction);
