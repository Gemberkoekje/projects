namespace Vista.World;

/// <summary>
/// The contour-line measures (vista-plan §10 M1.12c): how much of a world's rock lines run along the local contour. Level beds cut by
/// a hillside make benches and bands at one height all round it, like a map's contour lines; tilted and folded beds cross the
/// contours. Of the rock cells on slopes of at least <see cref="MinSlopeDegrees"/>, the <see cref="LedgeShare"/> where a quantity
/// changes most count as its lines, a cell's length each, and a line runs along the contour when the quantity's direction of change
/// points down the slope. The angle is the one on the slope, as someone facing it sees it (from above, a steep face is foreshortened
/// down its fall line, and every line on it looks closer to the contour than it is). The measure is the share of line within
/// <see cref="AlongContourDegrees"/> of the contour: level beds drive it toward 1. Lines in random directions on the slope would
/// put 1/9 there, but beds are not random to the slope: a bed's outcrop changes with the height as well as with its dip, so beds
/// that dip less steeply than the slopes still lean toward the contour.
/// <list type="bullet">
///   <item><see cref="Bands"/>, <b>the measure</b>: the beds' outcrop lines, where the rock's hardness at the surface changes fastest.
///   They are where the hard beds' ledges stand and where the rock's colour changes from bed to bed; level beds put every one on a
///   contour.</item>
///   <item><see cref="Ledges"/>: the lines of high curvature (the larger principal curvature of the height field, by magnitude). On
///   the eroded worlds the sharpest-curved rock is the erosion's gullies and ribs, which run down the slope, so this one stays below
///   1/9 whatever the beds do (M1.12b's seed 42: 4.9%); kept to show it.</item>
/// </list>
/// </summary>
/// <param name="Kind">"bands" or "ledges".</param>
/// <param name="SpacingMetres">The grid spacing the derivatives were taken at.</param>
/// <param name="RockCells">The rock cells steep enough to have a contour.</param>
/// <param name="LineCells">The line cells among them.</param>
/// <param name="AlongContour">The share of line within <see cref="AlongContourDegrees"/> of the local contour.</param>
/// <param name="AngleShares">The lines' angle to the contour in nine 10° bins from 0° to 90°, each a share of the line.</param>
public sealed record LedgeStats(string Kind, double SpacingMetres, long RockCells, long LineCells, double AlongContour, double[] AngleShares)
{
    /// <summary>A line this close to the contour runs along it, degrees.</summary>
    public const double AlongContourDegrees = 10.0;

    /// <summary>The share of the steep rock cells, where the quantity changes most, that count as line.</summary>
    public const double LedgeShare = 0.25;

    /// <summary>The gentlest slope a line cell may have, degrees: below it the contour's direction is ill defined.</summary>
    public const double MinSlopeDegrees = 20.0;

    /// <summary>
    /// The beds' outcrop lines of <paramref name="surface"/> (the measure): where <paramref name="hardness"/>, the rock's hardness at
    /// the surface (row-major on its grid), changes fastest on the samples <paramref name="rock"/> says are rock, with the
    /// derivatives taken over every <paramref name="stride"/>-th sample.
    /// </summary>
    public static LedgeStats Bands(HeightField surface, float[] hardness, Func<int, bool> rock, int stride = 1)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(hardness);
        if (hardness.Length != surface.Heights.Length)
            throw new ArgumentException("The hardness is not on the surface's grid.", nameof(hardness));
        int n = surface.Size;
        return Lines("bands", surface, rock, stride, (i, j, d, _) =>
        {
            double gx = hardness[j * n + i + d] - hardness[j * n + i - d], gz = hardness[(j + d) * n + i] - hardness[(j - d) * n + i];
            return (Math.Sqrt(gx * gx + gz * gz), Math.Atan2(gz, gx));
        });
    }

    /// <summary>The lines of high curvature of <paramref name="surface"/> on rock, as <see cref="Bands"/>.</summary>
    public static LedgeStats Ledges(HeightField surface, Func<int, bool> rock, int stride = 1)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return Lines("ledges", surface, rock, stride, (i, j, d, h) =>
        {
            double c = surface[i, j];
            double hxx = (surface[i + d, j] - 2.0 * c + surface[i - d, j]) / (h * h);
            double hzz = (surface[i, j + d] - 2.0 * c + surface[i, j - d]) / (h * h);
            double hxz = (surface[i + d, j + d] - surface[i + d, j - d] - surface[i - d, j + d] + surface[i - d, j - d]) / (4.0 * h * h);
            // The eigenvalues of the Hessian and the direction of the larger by magnitude.
            double mean = 0.5 * (hxx + hzz), half = Math.Sqrt(0.25 * (hxx - hzz) * (hxx - hzz) + hxz * hxz);
            double theta = 0.5 * Math.Atan2(2.0 * hxz, hxx - hzz);
            return Math.Abs(mean + half) >= Math.Abs(mean - half) ? (Math.Abs(mean + half), theta) : (Math.Abs(mean - half), theta + 0.5 * Math.PI);
        });
    }

    /// <summary>
    /// The measure of the lines of a quantity whose rate of change and direction of change at sample <c>(i, j)</c>, over a step of
    /// <c>d</c> samples (<c>h</c> metres), <paramref name="change"/> gives.
    /// </summary>
    private static LedgeStats Lines(string kind, HeightField surface, Func<int, bool> rock, int stride,
        Func<int, int, int, double, (double Rate, double Direction)> change)
    {
        ArgumentNullException.ThrowIfNull(rock);
        if (stride < 1)
            throw new ArgumentOutOfRangeException(nameof(stride), stride, "The stride is at least 1.");
        int n = surface.Size;
        double h = surface.CellSize * stride;
        double minGradient = Math.Tan(MinSlopeDegrees * Math.PI / 180.0);
        int samples = (n - 1 - 2 * stride) / stride + 1;
        if (samples < 1)
            return new LedgeStats(kind, h, 0, 0, double.NaN, new double[9]);

        // Per sample on the strided grid: the rate of change and the angle between its direction and the gradient (folded to
        // 0–90°), or NaN where the sample is not steep rock.
        var rate = new float[samples * samples];
        var angle = new float[samples * samples];
        Parallel.For(0, samples, sj =>
        {
            int j = stride + sj * stride;
            for (int si = 0; si < samples; si++)
            {
                int i = stride + si * stride;
                int k = sj * samples + si;
                rate[k] = float.NaN;
                if (!rock(j * n + i))
                    continue;
                double gx = (surface[i + stride, j] - surface[i - stride, j]) / (2.0 * h);
                double gz = (surface[i, j + stride] - surface[i, j - stride]) / (2.0 * h);
                if (gx * gx + gz * gz < minGradient * minGradient)
                    continue;
                (double r, double direction) = change(i, j, stride, h);
                double between = Math.Abs(direction - Math.Atan2(gz, gx)) % Math.PI;
                // The map's angle, then the angle on the slope itself: seen from above a face of slope α is foreshortened down its
                // fall line by cos α, so tan(on the face) = tan(on the map) / cos α.
                double onMap = Math.Min(between, Math.PI - between);
                rate[k] = (float)r;
                angle[k] = (float)(Math.Atan(Math.Tan(onMap) * Math.Sqrt(1.0 + gx * gx + gz * gz)) * 180.0 / Math.PI);
            }
        });

        float[] steep = rate.Where(v => !float.IsNaN(v)).ToArray();
        if (steep.Length == 0)
            return new LedgeStats(kind, h, 0, 0, double.NaN, new double[9]);
        Array.Sort(steep);
        // The lines: the cells at or above the quantile, and never where the quantity does not change at all.
        float threshold = Math.Max(steep[(int)Math.Floor((1.0 - LedgeShare) * (steep.Length - 1))], float.Epsilon);
        var bins = new long[9];
        long lines = 0, along = 0;
        for (int k = 0; k < rate.Length; k++)
        {
            if (float.IsNaN(rate[k]) || rate[k] < threshold)
                continue;
            lines++;
            bins[Math.Min((int)(angle[k] / 10.0), 8)]++;
            if (angle[k] <= AlongContourDegrees)
                along++;
        }
        if (lines == 0)
            return new LedgeStats(kind, h, steep.Length, 0, double.NaN, new double[9]);
        return new LedgeStats(kind, h, steep.Length, lines, (double)along / lines, bins.Select(b => (double)b / lines).ToArray());
    }
}
