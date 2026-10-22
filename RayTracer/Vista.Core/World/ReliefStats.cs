namespace Vista.World;

/// <summary>
/// The measures that decide M1.11d (vista-plan §10): whether a world's ranges have hierarchy, and whether its slopes vary. The
/// <b>local relief</b> at a scale is the height range (highest minus lowest) inside a square of that side, taken around points
/// <see cref="ReliefSpacingMetres"/> apart that keep the whole square on the grid; a uniform field of similar peaks has much the
/// same relief everywhere, while massifs beside foothills spread it, so the measure is its spread (the 10th, 50th and 90th
/// percentiles). The <b>summits</b> are the highest point of each 1 km square: a field of similar peaks has them at much the same
/// height, a range with massifs and foothills spreads them. The <b>slopes</b> are each cell's gradient angle (central differences) on the grid sampled every
/// <c>stride</c> cells; a terrain of talus facets piles them up within a few degrees of one angle, so the measure is the share
/// of slopes in the most crowded window of ±<see cref="FacetHalfWidthDegrees"/>° from 20° up (where talus angles lie), and
/// where it is.
/// </summary>
public sealed record ReliefStats
{
    /// <summary>The spacing of the points the local relief is taken around, in metres.</summary>
    public const double ReliefSpacingMetres = 250.0;

    /// <summary>Half the width of the slope window the facet measure counts, in degrees.</summary>
    public const double FacetHalfWidthDegrees = 2.0;

    /// <summary>The local relief at 1 km: its 10th, 50th and 90th percentiles in metres.</summary>
    public required Percentiles Relief1Km { get; init; }

    /// <summary>The local relief at 5 km: its 10th, 50th and 90th percentiles in metres.</summary>
    public required Percentiles Relief5Km { get; init; }

    /// <summary>The highest point of each 1 km square: its 10th, 50th and 90th percentiles in metres.</summary>
    public required Percentiles Summits1Km { get; init; }

    /// <summary>The slopes on the grid's own cells.</summary>
    public required SlopeStats Slopes { get; init; }

    /// <summary>The slopes on every eighth sample (64 m on the world grid: the incision's cells, where its facets are planes).</summary>
    public required SlopeStats CoarseSlopes { get; init; }

    /// <summary>The 10th, 50th and 90th percentiles of a measure.</summary>
    /// <param name="P10">The 10th percentile.</param>
    /// <param name="P50">The median.</param>
    /// <param name="P90">The 90th percentile.</param>
    public sealed record Percentiles(double P10, double P50, double P90)
    {
        /// <summary><see cref="P90"/> over <see cref="P10"/>: 1 for a measure that is the same everywhere.</summary>
        public double Spread => P10 > 0.0 ? P90 / P10 : double.PositiveInfinity;
    }

    /// <summary>A world's slopes on one sampling of its grid.</summary>
    /// <param name="SpacingMetres">The sample spacing the gradients were taken at.</param>
    /// <param name="MeanDegrees">The mean slope angle.</param>
    /// <param name="Over40">The share of slopes steeper than 40°.</param>
    /// <param name="FacetDegrees">The centre of the most crowded ±2° window from 20° up.</param>
    /// <param name="FacetShare">The share of all slopes in that window.</param>
    /// <param name="NearTalus">
    /// The share of slopes within ±2° of each cell's talus angle when one was given (<see cref="Measure"/>'s <c>talus</c>), else NaN.
    /// </param>
    public sealed record SlopeStats(double SpacingMetres, double MeanDegrees, double Over40, double FacetDegrees, double FacetShare, double NearTalus);

    /// <summary>
    /// Measures <paramref name="field"/>. <paramref name="talusDegrees"/>, when given, is each cell's talus angle (row-major on the
    /// field's grid), for <see cref="SlopeStats.NearTalus"/>.
    /// </summary>
    public static ReliefStats Measure(HeightField field, Func<int, double>? talusDegrees = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        (Percentiles relief1, Percentiles summits1) = Windows(field, 1000.0);
        return new ReliefStats
        {
            Relief1Km = relief1,
            Relief5Km = Windows(field, 5000.0).Relief,
            Summits1Km = summits1,
            Slopes = MeasureSlopes(field, 1, talusDegrees),
            CoarseSlopes = MeasureSlopes(field, 8, talusDegrees),
        };
    }

    /// <summary>
    /// The percentiles of the local relief (highest minus lowest) and of the highest point in squares <paramref name="windowMetres"/>
    /// a side (rounded to whole cells), around points <see cref="ReliefSpacingMetres"/> apart that keep the square on the grid.
    /// Zeros when the grid is smaller than the square.
    /// </summary>
    public static (Percentiles Relief, Percentiles Summits) Windows(HeightField field, double windowMetres)
    {
        ArgumentNullException.ThrowIfNull(field);
        int n = field.Size;
        int half = Math.Max(1, (int)Math.Round(0.5 * windowMetres / field.CellSize));
        int step = Math.Max(1, (int)Math.Round(ReliefSpacingMetres / field.CellSize));
        if (2 * half >= n)
            return (new Percentiles(0.0, 0.0, 0.0), new Percentiles(0.0, 0.0, 0.0));
        // Row by row, the range of each cell's run of 2·half + 1 columns; then, per point, the range of those over its rows.
        var centres = new List<int>();
        for (int c = half; c < n - half; c += step)
            centres.Add(c);
        var rowMin = new float[n * centres.Count];
        var rowMax = new float[n * centres.Count];
        Parallel.For(0, n, j =>
        {
            for (int k = 0; k < centres.Count; k++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = centres[k] - half; i <= centres[k] + half; i++)
                {
                    float h = field[i, j];
                    lo = Math.Min(lo, h);
                    hi = Math.Max(hi, h);
                }
                rowMin[j * centres.Count + k] = lo;
                rowMax[j * centres.Count + k] = hi;
            }
        });
        var relief = new double[centres.Count * centres.Count];
        var summits = new double[centres.Count * centres.Count];
        Parallel.For(0, centres.Count, b =>
        {
            int cj = centres[b];
            for (int k = 0; k < centres.Count; k++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int j = cj - half; j <= cj + half; j++)
                {
                    lo = Math.Min(lo, rowMin[j * centres.Count + k]);
                    hi = Math.Max(hi, rowMax[j * centres.Count + k]);
                }
                relief[b * centres.Count + k] = hi - lo;
                summits[b * centres.Count + k] = hi;
            }
        });
        Array.Sort(relief);
        Array.Sort(summits);
        return (new Percentiles(Quantile(relief, 0.1), Quantile(relief, 0.5), Quantile(relief, 0.9)),
            new Percentiles(Quantile(summits, 0.1), Quantile(summits, 0.5), Quantile(summits, 0.9)));
    }

    /// <summary>The slopes of <paramref name="field"/> sampled every <paramref name="stride"/> cells (central differences inside).</summary>
    public static SlopeStats MeasureSlopes(HeightField field, int stride, Func<int, double>? talusDegrees = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, 1);
        int n = field.Size;
        double spacing = stride * field.CellSize;
        // Tenths of a degree, so the windows can be counted at any centre.
        var bins = new long[901];
        long count = 0, over40 = 0, nearTalus = 0;
        double sum = 0.0;
        var gate = new object();
        Parallel.For(0, (n - 1) / stride - 1, b =>
        {
            int j = (b + 1) * stride;
            var local = new long[901];
            long localCount = 0, localOver = 0, localNear = 0;
            double localSum = 0.0;
            for (int i = stride; i + stride < n; i += stride)
            {
                double gx = (field[i + stride, j] - (double)field[i - stride, j]) / (2.0 * spacing);
                double gz = (field[i, j + stride] - (double)field[i, j - stride]) / (2.0 * spacing);
                double degrees = Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180.0 / Math.PI;
                local[Math.Min(900, (int)(degrees * 10.0))]++;
                localCount++;
                localSum += degrees;
                if (degrees > 40.0)
                    localOver++;
                if (talusDegrees is not null && Math.Abs(degrees - talusDegrees(j * n + i)) <= FacetHalfWidthDegrees)
                    localNear++;
            }
            lock (gate)
            {
                for (int k = 0; k < bins.Length; k++)
                    bins[k] += local[k];
                count += localCount;
                over40 += localOver;
                nearTalus += localNear;
                sum += localSum;
            }
        });
        if (count == 0)
            return new SlopeStats(spacing, 0.0, 0.0, 0.0, 0.0, double.NaN);
        int width = (int)(FacetHalfWidthDegrees * 10.0);
        long best = -1;
        int bestCentre = 0;
        for (int centre = 200; centre + width < bins.Length; centre++)
        {
            long inWindow = 0;
            for (int k = centre - width; k < centre + width; k++)
                inWindow += bins[k];
            if (inWindow > best)
            {
                best = inWindow;
                bestCentre = centre;
            }
        }
        return new SlopeStats(spacing, sum / count, (double)over40 / count, bestCentre / 10.0, (double)best / count,
            talusDegrees is null ? double.NaN : (double)nearTalus / count);
    }

    private static double Quantile(double[] sorted, double q) =>
        sorted.Length == 0 ? 0.0 : sorted[Math.Clamp((int)Math.Round(q * (sorted.Length - 1)), 0, sorted.Length - 1)];
}
