namespace Vista.World;

/// <summary>
/// The shape of the valleys glaciers carved (vista-plan §10 M1.13 verify): cross-sections of a carved surface and of the same world
/// incised without glaciers, at the same places and in the same directions, where glaciers would run on the uncarved ground, in its
/// mountain valleys: so every carving is measured at the same sections. Each section is cut across its valley (the direction, of
/// twelve, in which the uncarved trough is narrowest), and its shape is the trough's width at half of <see cref="TroughMetres"/> over
/// its width at the whole of it, both from the section's own floor: a V gives 0.5, a parabola 0.71, a U toward 1. The bottom of the
/// trough is what a valley glacier shapes; above it the slopes lead up to shoulders and ridges the ice never reached. The power of
/// the trough, <c>z − floor = a·|x|^b</c>, follows from the same two widths: b = ln 2 / ln(W / W½) (a V 1, a parabola 2; Harbor
/// 1992).
/// </summary>
public static class ValleyShape
{
    /// <summary>How far a section reaches to either side of its middle, m.</summary>
    public const double ReachMetres = 2500.0;

    /// <summary>The step along a section, m.</summary>
    public const double StepMetres = 16.0;

    /// <summary>How far from a section's middle its floor may lie, m.</summary>
    public const double FloorSearchMetres = 400.0;

    /// <summary>The depth of the trough's bottom the shape is measured over, m.</summary>
    public const double TroughMetres = 100.0;

    /// <summary>The least ice a section's middle must hold, m: a trough a glacier filled.</summary>
    public const double MinIceMetres = 120.0;

    /// <summary>The least distance between two sections' middles, m.</summary>
    public const double SpacingMetres = 2500.0;

    /// <summary>
    /// The least relief within <see cref="ReliefReachMetres"/> of a section's middle on the uncarved ground, m: a mountain valley, not
    /// the lowland an ice sheet spreads over.
    /// </summary>
    public const double MinReliefMetres = 300.0;

    /// <summary>The directions a section is tried in, over half a turn.</summary>
    public const int Directions = 12;

    /// <summary>How far around a section's middle its relief is taken, m.</summary>
    public const double ReliefReachMetres = 1000.0;

    /// <summary>
    /// Up to <paramref name="count"/> sections of <paramref name="carved"/> and <paramref name="incised"/> (on one grid) where
    /// <paramref name="ice"/> (glaciers on <paramref name="incised"/>) carries the most ice, at least
    /// <see cref="SpacingMetres"/> apart and a section's reach from the grid's edges.
    /// </summary>
    public static IReadOnlyList<ValleySection> Measure(HeightField carved, HeightField incised, IceField ice, int count = 12)
    {
        ArgumentNullException.ThrowIfNull(carved);
        ArgumentNullException.ThrowIfNull(incised);
        ArgumentNullException.ThrowIfNull(ice);
        if (incised.Size != carved.Size || incised.CellSize != carved.CellSize)
            throw new ArgumentException("The two surfaces are not on one grid.", nameof(incised));
        int n = carved.Size;
        double cell = carved.CellSize;
        int margin = (int)Math.Ceiling(ReachMetres / cell) + 2;
        int[] order = Enumerable.Range(0, n * n)
            .Where(c => ice.Thickness[c] >= MinIceMetres && c % n >= margin && c % n < n - margin && c / n >= margin && c / n < n - margin)
            .OrderByDescending(c => ice.Flux[c]).ThenBy(c => c).ToArray();
        var sections = new List<ValleySection>();
        foreach (int c in order)
        {
            int i = c % n, j = c / n;
            if (sections.Any(s => Math.Abs(s.I - i) * cell + Math.Abs(s.J - j) * cell < SpacingMetres))
                continue;
            if (Relief(incised, i, j) < MinReliefMetres)
                continue;
            // Across the valley: of twelve directions, the one in which the uncarved trough is narrowest at its bottom's depth (the
            // flow of the ice there may lean toward the walls it drains, and the drainage knows only eight directions).
            (double ux, double uz) = (0.0, 0.0);
            double narrowest = double.MaxValue;
            for (int k = 0; k < Directions; k++)
            {
                double angle = Math.PI * k / Directions;
                if (Profile(incised, i, j, Math.Cos(angle), Math.Sin(angle), TroughMetres) is { } p && p.WidthMetres < narrowest)
                    (narrowest, ux, uz) = (p.WidthMetres, Math.Cos(angle), Math.Sin(angle));
            }
            if (narrowest == double.MaxValue)
                continue;
            ValleyProfile? a = Profile(carved, i, j, ux, uz, TroughMetres), b = Profile(incised, i, j, ux, uz, TroughMetres);
            if (a is null || b is null)
                continue;
            sections.Add(new ValleySection(i, j, ice.Thickness[c], ice.Flux[c], a, b));
            if (sections.Count >= count)
                break;
        }
        return sections;
    }

    /// <summary>The highest less the lowest sample of <paramref name="field"/> within <see cref="ReliefReachMetres"/> of sample (i, j), m.</summary>
    private static double Relief(HeightField field, int i, int j)
    {
        int r = (int)(ReliefReachMetres / field.CellSize);
        float high = float.MinValue, low = float.MaxValue;
        for (int b = Math.Max(j - r, 0); b <= Math.Min(j + r, field.Size - 1); b++)
        {
            for (int a = Math.Max(i - r, 0); a <= Math.Min(i + r, field.Size - 1); a++)
            {
                high = Math.Max(high, field[a, b]);
                low = Math.Min(low, field[a, b]);
            }
        }
        return high - low;
    }

    /// <summary>The section of <paramref name="field"/> through sample (i, j) along (ux, uz), and its trough's shape to <paramref name="depth"/>.</summary>
    private static ValleyProfile? Profile(HeightField field, int i, int j, double ux, double uz, double depth)
    {
        int half = (int)(ReachMetres / StepMetres);
        var heights = new double[2 * half + 1];
        double x0 = field.XOf(i), z0 = field.ZOf(j);
        for (int k = -half; k <= half; k++)
            heights[k + half] = field.HeightAt(x0 + ux * k * StepMetres, z0 + uz * k * StepMetres);
        int search = (int)(FloorSearchMetres / StepMetres), floor = half;
        for (int k = half - search; k <= half + search; k++)
        {
            if (heights[k] < heights[floor])
                floor = k;
        }
        double? Width(double level)
        {
            int a = floor, b = floor;
            while (a > 0 && heights[a] < level)
                a--;
            while (b < heights.Length - 1 && heights[b] < level)
                b++;
            if (heights[a] < level || heights[b] < level)
                return null;
            // Linear between the samples either side of the level.
            double left = a + (level - heights[a]) / (heights[a + 1] - heights[a]);
            double right = b - (level - heights[b]) / (heights[b - 1] - heights[b]);
            return (right - left) * StepMetres;
        }
        double? whole = Width(heights[floor] + depth), halfWidth = Width(heights[floor] + 0.5 * depth);
        if (whole is not { } w || halfWidth is not { } h || !(h > 0.0) || !(w > h))
            return null;
        return new ValleyProfile(heights, floor, h / w, Math.Log(2.0) / Math.Log(w / h), w);
    }
}

/// <summary>One cross-section of <see cref="ValleyShape.Measure"/>: where, how deep the glacier was, and the two surfaces' troughs.</summary>
/// <param name="I">The middle's column on the grid.</param>
/// <param name="J">Its row.</param>
/// <param name="IceMetres">The glacier's thickness there on the uncarved ground, m.</param>
/// <param name="Flux">Its flux there, m³ a year.</param>
/// <param name="Carved">The carved surface's trough.</param>
/// <param name="Incised">The trough of the surface incised without glaciers.</param>
public sealed record ValleySection(int I, int J, double IceMetres, double Flux, ValleyProfile Carved, ValleyProfile Incised);

/// <summary>A trough's section (<see cref="ValleyShape"/>).</summary>
/// <param name="Heights">The surface along the section, every <see cref="ValleyShape.StepMetres"/>, the middle in the middle.</param>
/// <param name="Floor">The floor's index in <paramref name="Heights"/>.</param>
/// <param name="HalfDepthRatio">The width at half the depth over the width at the whole of it (a V 0.5, a U toward 1).</param>
/// <param name="Power">b of <c>z − floor = a·|x|^b</c> from those widths (a V 1, a parabola 2).</param>
/// <param name="WidthMetres">The width at <see cref="ValleyShape.TroughMetres"/> above the floor, m.</param>
public sealed record ValleyProfile(double[] Heights, int Floor, double HalfDepthRatio, double Power, double WidthMetres);
