namespace Vista.Composition;

/// <summary>
/// The scores read from a rendered image's values (vista-plan §10 M1.14), for the finalists only: the image's display-encoded luminance,
/// 0 (black) to 1 (white), as the eye reads the PNG.
/// <list type="bullet">
///   <item><b>The squint test</b> (<see cref="Squint"/>): a painter half-closes their eyes to see a picture's value structure; a
///   downscaled, blurred thumbnail does the same. A strong picture falls into a few distinct groups of value (a light sky and background,
///   a mid middle ground, a dark foreground), a weak one into muddy mid-tones.</item>
///   <item><b>The values by layer</b> (<see cref="Layers"/>): the user's paintings' depth, a light background range, a darker middle
///   ground and a dark foreground, which aerial perspective gives a real landscape and the shadowed near ground deepens.</item>
/// </list>
/// </summary>
public static class ValueStructure
{
    /// <summary>The squint thumbnail's width, pixels.</summary>
    public const int ThumbnailWidth = 32;

    /// <summary>The groups' separation that scores nothing: a little below an even spread of values (0.89).</summary>
    public const double SeparationFloor = 0.85;

    /// <summary>The groups' separation that scores fully: three tight groups.</summary>
    public const double SeparationFull = 0.97;

    /// <summary>The spread between the darkest and the lightest group that scores fully, a share of the value range.</summary>
    public const double SpreadFull = 0.5;

    /// <summary>
    /// The squint test of <paramref name="values"/> (<paramref name="width"/>×<paramref name="height"/>, rows from the top): its thumbnail
    /// (box-averaged to <see cref="ThumbnailWidth"/> across, then blurred by a 3×3 binomial) split into three groups of value
    /// (<see cref="CompositionScorer.KMeans3"/>): the score rises with how well they separate (from <see cref="SeparationFloor"/> to
    /// <see cref="SeparationFull"/>) and with the spread between the darkest and the lightest group (to <see cref="SpreadFull"/>), and
    /// their product is the score.
    /// </summary>
    public static (double Score, double Separation, double Spread) Squint(float[] values, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != width * height)
            throw new ArgumentException($"Expected {width * height} values, got {values.Length}.", nameof(values));
        int tw = Math.Min(ThumbnailWidth, width);
        int th = Math.Max(2, (int)Math.Round((double)tw * height / width));
        var thumb = new double[tw * th];
        for (int ty = 0; ty < th; ty++)
        {
            int y0 = ty * height / th, y1 = Math.Max(y0 + 1, (ty + 1) * height / th);
            for (int tx = 0; tx < tw; tx++)
            {
                int x0 = tx * width / tw, x1 = Math.Max(x0 + 1, (tx + 1) * width / tw);
                double sum = 0.0;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                        sum += values[y * width + x];
                }
                thumb[ty * tw + tx] = sum / ((y1 - y0) * (x1 - x0));
            }
        }
        var blurred = new double[tw * th];
        for (int ty = 0; ty < th; ty++)
        {
            for (int tx = 0; tx < tw; tx++)
            {
                double sum = 0.0, weight = 0.0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = tx + dx, y = ty + dy;
                        if (x < 0 || y < 0 || x >= tw || y >= th)
                            continue;
                        double k = (dx == 0 ? 2.0 : 1.0) * (dy == 0 ? 2.0 : 1.0);
                        sum += k * thumb[y * tw + x];
                        weight += k;
                    }
                }
                blurred[ty * tw + tx] = sum / weight;
            }
        }
        (double[] centres, _, double separation) = CompositionScorer.KMeans3(blurred);
        double spread = centres[2] - centres[0];
        double score = CompositionScorer.Clamp01((separation - SeparationFloor) / (SeparationFull - SeparationFloor)) * CompositionScorer.Clamp01(spread / SpreadFull);
        return (score, separation, spread);
    }

    /// <summary>
    /// The values by depth layer of <paramref name="values"/> over <paramref name="buffer"/> (the same frame, its ground in three groups
    /// by depth, <see cref="CompositionScorer.DepthLayers"/>): the background lighter than the middle ground and the middle ground
    /// lighter than the foreground, each step scoring fully at 0.1 of the value range; and the mean values of the three, nearest first.
    /// </summary>
    public static (double Score, double[] Means) Layers(float[] values, CompositionBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(buffer);
        if (values.Length != buffer.Width * buffer.Height)
            throw new ArgumentException("The values and the buffer are not the same frame.", nameof(values));
        (_, int[] cluster) = CompositionScorer.DepthLayers(buffer);
        var sums = new double[3];
        var counts = new int[3];
        for (int p = 0; p < cluster.Length; p++)
        {
            if (cluster[p] < 0)
                continue;
            sums[cluster[p]] += values[p];
            counts[cluster[p]]++;
        }
        double[] means = [.. Enumerable.Range(0, 3).Select(k => counts[k] > 0 ? sums[k] / counts[k] : double.NaN)];
        if (means.Any(double.IsNaN))
            return (0.0, means);
        double score = 0.5 * CompositionScorer.Clamp01((means[2] - means[1]) / 0.1) + 0.5 * CompositionScorer.Clamp01((means[1] - means[0]) / 0.1);
        return (score, means);
    }
}
