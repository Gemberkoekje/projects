namespace Vista.World;

/// <summary>
/// A glacier's climate on a surface (vista-plan §4.5, §10 M1.13): the mass balance, in metres of ice a year, grows with height
/// above the equilibrium line at <see cref="BalanceGradient"/> per metre, up to <see cref="AccumulationMetres"/> above it and down to
/// −<see cref="AblationMetres"/> below it. The heights are the surface's own, whatever frame that is in.
/// </summary>
/// <param name="ElaMetres">The equilibrium line: where a year's snow and a year's melt balance, m.</param>
/// <param name="BalanceGradient">Metres of ice a year per metre of height (about 0.006–0.01 in the Alps).</param>
/// <param name="AccumulationMetres">The most a year adds, m of ice.</param>
/// <param name="AblationMetres">The most a year melts, m of ice.</param>
public sealed record GlacierClimate(double ElaMetres, double BalanceGradient, double AccumulationMetres, double AblationMetres)
{
    /// <summary>The mass balance at height <paramref name="z"/>, m of ice a year.</summary>
    public double Balance(double z) => Math.Clamp(BalanceGradient * (z - ElaMetres), -AblationMetres, AccumulationMetres);
}

/// <summary>How ice flows over a surface (<see cref="GlacierFlow"/>).</summary>
/// <param name="SteepDegrees">
/// Ground steeper than this holds no ice: its snow avalanches into the cell below (it still joins the flux there), so faces above a
/// glacier stay rock.
/// </param>
/// <param name="SlopeFloor">The least ice-surface slope the thickness is worked out with (flat ice would be infinitely thick).</param>
/// <param name="MaxThicknessMetres">The thickest the ice may grow, m.</param>
/// <param name="Damping">The share of the way to the new thickness each iteration moves (1: all of it).</param>
public sealed record GlacierFlowSettings(double SteepDegrees, double SlopeFloor, double MaxThicknessMetres, double Damping)
{
    /// <summary>The flow of every glacier Vista makes.</summary>
    public static GlacierFlowSettings Default { get; } = new(40.0, 0.02, 1200.0, 0.5);
}

/// <summary>The ice on a surface (<see cref="GlacierFlow.Solve"/>), row-major on its grid.</summary>
/// <param name="Thickness">Ice thickness per cell, m.</param>
/// <param name="Flux">The ice that flows through each cell a year, m³.</param>
/// <param name="Speed">The ice's mean speed per cell, m a year (the flux per metre of width over the thickness).</param>
public sealed record IceField(float[] Thickness, float[] Flux, float[] Speed)
{
    /// <summary>
    /// The thickness of the glacier a cell lies under, m: its ice averaged over the iced cells around it, as its speed is
    /// (<see cref="GlacierFlow.SpeedSmoothingCells"/>), so a valley glacier's walls share the thickness of its middle. Empty when not
    /// worked out.
    /// </summary>
    public float[] Body { get; init; } = [];

    /// <summary>The cells whose ice is at least <paramref name="metres"/> thick.</summary>
    public int Count(float metres = 1f)
    {
        int count = 0;
        foreach (float h in Thickness)
            count += h >= metres ? 1 : 0;
        return count;
    }
}

/// <summary>
/// Glaciers on a surface in their steady state (vista-plan §10 M1.13): the ice a climate keeps on the ground, not its history. Ice
/// adjusts to its climate in centuries, far faster than it carves, so the world stage solves for the steady ice at each step of the
/// carving (<see cref="StreamPower"/>) and once more for today's glaciers on the finished world (<see cref="Glaciers"/>).
///
/// <para><b>Where the ice goes</b> is the balance flux (as glaciologists reconstruct a glacier from its mass balance): each cell adds
/// its year's balance times its area to the ice arriving from upstream, and passes what is left on, downhill over the <i>ice's</i>
/// surface (the ground plus the ice), shared among every lower neighbour in proportion to the drop toward it (multiple flow
/// directions, Quinn et al. 1991), so a glacier spreads across the valley it fills rather than running down its floor's line. A flux
/// that melts away to nothing is the glacier's snout. The ice's surface is routed after its hollows are filled (the drainage's
/// Priority-Flood+ε, <see cref="DrainageMap"/>), so ice that runs into a hollow fills it and spills over.</para>
///
/// <para><b>How thick it is</b> follows from the flux by the shallow-ice approximation: ice deforming under its own weight carries
/// <c>q = (2A/(n+2))·(ρg)ⁿ·Sⁿ·Hⁿ⁺²</c> per metre of width (Glen's law, n = 3, A for temperate ice, 2.4·10⁻²⁴ Pa⁻³ s⁻¹; Cuffey and
/// Paterson 2010), and as much again sliding on its bed, so <c>H = (q / (K·S³))^⅕</c> with <c>S</c> the ice surface's slope. The
/// thickness and the surface depend on each other, so the solve iterates, each time moving part of the way (damped).</para>
///
/// <para>Pure and deterministic: the routing runs in one order, sorted by the routing surface; the fifth root is Newton's, in
/// arithmetic alone, so no library's last bit moves a world.</para>
/// </summary>
public static class GlacierFlow
{
    /// <summary>Glen's flow parameter for temperate ice, Pa⁻³ a⁻¹ (2.4·10⁻²⁴ Pa⁻³ s⁻¹, Cuffey and Paterson 2010).</summary>
    public const double FlowParameter = 2.4e-24 * 31_556_926.0;

    /// <summary>Ice's weight per cubic metre, Pa/m (917 kg/m³ at 9.81 m/s²).</summary>
    public const double IceWeight = 917.0 * 9.81;

    /// <summary>
    /// The flux constant K of <c>q = K·S³·H⁵</c>, m⁻³ a⁻¹: deformation's <c>2A(ρg)³/5</c>, doubled for the ice that slides
    /// (half of a temperate valley glacier's motion is sliding).
    /// </summary>
    public static readonly double FluxConstant = 2.0 * (2.0 * FlowParameter * IceWeight * IceWeight * IceWeight / 5.0);

    /// <summary>
    /// The half-width, in cells, of the tent the ice's surface is averaged over (a 5 × 5 tent: 320 m at the incision's 64 m, about
    /// two ice thicknesses of a valley glacier).
    /// </summary>
    public const int SmoothingCells = 2;

    /// <summary>
    /// The half-width, in cells, of the tent a glacier's sliding speed is averaged over (a 9 × 9 tent: 576 m at the incision's 64 m, a
    /// valley glacier's width).
    /// </summary>
    public const int SpeedSmoothingCells = 4;

    /// <summary>The least ice thickness a speed is worked out with, m.</summary>
    public const double SpeedFloorMetres = 10.0;

    /// <summary>The ice thickness over which a glacier's speed falls to nothing toward its edges, m.</summary>
    public const double EdgeDragMetres = 30.0;

    /// <summary>
    /// The steady ice on <paramref name="bed"/> under <paramref name="climate"/>, flowing by <paramref name="flow"/>: from
    /// <paramref name="start"/>'s thickness (null: none) after <paramref name="iterations"/> iterations.
    /// </summary>
    public static IceField Solve(HeightField bed, GlacierClimate climate, GlacierFlowSettings flow, float[]? start, int iterations)
    {
        ArgumentNullException.ThrowIfNull(bed);
        ArgumentNullException.ThrowIfNull(climate);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentOutOfRangeException.ThrowIfNegative(iterations);
        int n = bed.Size;
        int cells = n * n;
        if (start is not null && start.Length != cells)
            throw new ArgumentException($"Expected {cells} thicknesses, got {start.Length}.", nameof(start));
        double cell = bed.CellSize;
        ReadOnlySpan<float> ground = bed.Heights;
        float[] heights = ground.ToArray();
        var thickness = start is null ? new float[cells] : (float[])start.Clone();
        var flux = new float[cells];
        var speed = new float[cells];
        bool[] steep = SteepCells(bed, flow.SteepDegrees);
        for (int c = 0; c < cells; c++)
        {
            if (steep[c])
                thickness[c] = 0f;
        }

        var surface = new float[cells];
        var wanted = new float[cells];
        var inflow = new double[cells];
        var order = new int[cells];
        var keys = new double[cells];
        double area = cell * cell;
        double diagonal = Math.Sqrt(2.0);
        Span<double> share = stackalloc double[8];
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            for (int c = 0; c < cells; c++)
                surface[c] = heights[c] + thickness[c];
            double[] routing = DrainageMap.Flood(new HeightField(n, cell, (float[])surface.Clone()));
            for (int c = 0; c < cells; c++)
            {
                order[c] = c;
                keys[c] = -routing[c];
            }
            Array.Sort(keys, order);

            // The balance flux, from the highest ice down: a cell's year passes on to every lower neighbour by its drop.
            Array.Clear(inflow);
            foreach (int c in order)
            {
                double through = Math.Max(inflow[c] + climate.Balance(surface[c]) * area, 0.0);
                flux[c] = (float)through;
                int i = c % n, j = c / n;
                if (through <= 0.0 || i == 0 || j == 0 || i == n - 1 || j == n - 1)
                    continue;
                double total = 0.0;
                for (int q = 0; q < 8; q++)
                {
                    int m = c + DrainageMap.Dj[q] * n + DrainageMap.Di[q];
                    double drop = routing[c] - routing[m];
                    share[q] = drop > 0.0 ? drop / (DrainageMap.Di[q] != 0 && DrainageMap.Dj[q] != 0 ? diagonal : 1.0) : 0.0;
                    total += share[q];
                }
                for (int q = 0; q < 8; q++)
                {
                    if (share[q] > 0.0)
                        inflow[c + DrainageMap.Dj[q] * n + DrainageMap.Di[q]] += through * share[q] / total;
                }
            }

            // The thickness each cell's flux needs at its surface's slope.
            Parallel.For(0, n, j =>
            {
                for (int i = 0; i < n; i++)
                {
                    int c = j * n + i;
                    double target = 0.0;
                    if (!steep[c] && flux[c] > 0f)
                    {
                        double slope = Math.Max(SurfaceSlope(surface, n, cell, i, j), flow.SlopeFloor);
                        double perWidth = flux[c] / cell;
                        target = Math.Min(FifthRoot(perWidth / (FluxConstant * slope * slope * slope)), flow.MaxThicknessMetres);
                    }
                    wanted[c] = (float)target;
                }
            });

            // A glacier's surface is smooth over a few ice thicknesses (its longitudinal stresses couple it), so the ice the flux
            // asks for becomes a surface averaged over the iced cells around each cell, and the ice fills the valley up to it, the
            // walls included, rather than standing in a ridge along the line the flux takes. Part of the way each iteration.
            double damping = flow.Damping;
            Parallel.For(0, n, j =>
            {
                for (int i = 0; i < n; i++)
                {
                    int c = j * n + i;
                    double sum = 0.0, weights = 0.0;
                    for (int dj = -SmoothingCells; dj <= SmoothingCells; dj++)
                    {
                        int jj = j + dj;
                        if (jj < 0 || jj >= n)
                            continue;
                        for (int di = -SmoothingCells; di <= SmoothingCells; di++)
                        {
                            int ii = i + di;
                            if (ii < 0 || ii >= n)
                                continue;
                            int k = jj * n + ii;
                            if (!(wanted[k] > 0f))
                                continue;
                            double w = (SmoothingCells + 1 - Math.Abs(di)) * (SmoothingCells + 1 - Math.Abs(dj));
                            sum += w * (heights[k] + (double)wanted[k]);
                            weights += w;
                        }
                    }
                    double target = steep[c] || weights == 0.0 ? 0.0 : Math.Clamp(sum / weights - heights[c], 0.0, flow.MaxThicknessMetres);
                    thickness[c] = (float)(thickness[c] + (target - thickness[c]) * damping);
                }
            });
        }

        // A snowfield's few metres carry a cell's whole year without speeding up: the speed counts at least SpeedFloorMetres of ice.
        // And a glacier slides as a plug across most of its width, not fastest along the line its flux happens to take: the speed is
        // averaged over the iced cells around each one, as the surface is, and falls only where the ice thins toward its edges.
        var raw = new float[cells];
        var body = new float[cells];
        for (int c = 0; c < cells; c++)
            raw[c] = thickness[c] > 0f ? (float)(flux[c] / (cell * Math.Max(thickness[c], SpeedFloorMetres))) : 0f;
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                int c = j * n + i;
                if (!(thickness[c] > 0f))
                    continue;
                double sum = 0.0, weights = 0.0, held = 0.0;
                for (int dj = -SpeedSmoothingCells; dj <= SpeedSmoothingCells; dj++)
                {
                    int jj = j + dj;
                    if (jj < 0 || jj >= n)
                        continue;
                    for (int di = -SpeedSmoothingCells; di <= SpeedSmoothingCells; di++)
                    {
                        int ii = i + di;
                        if (ii < 0 || ii >= n || !(thickness[jj * n + ii] > 0f))
                            continue;
                        double w = (SpeedSmoothingCells + 1 - Math.Abs(di)) * (SpeedSmoothingCells + 1 - Math.Abs(dj));
                        sum += w * raw[jj * n + ii];
                        held += w * thickness[jj * n + ii];
                        weights += w;
                    }
                }
                double plug = sum / weights;
                body[c] = (float)(held / weights);
                // Toward the edges the ice drags on its walls: the plug's speed fades over its first EdgeDragMetres of thickness.
                speed[c] = (float)(plug * Math.Min(thickness[c] / EdgeDragMetres, 1.0));
            }
        });
        return new IceField(thickness, flux, speed) { Body = body };
    }

    /// <summary>
    /// <paramref name="x"/>^⅕ for <paramref name="x"/> ≥ 0, by Newton's iteration from its fourth root (two correctly rounded square
    /// roots): arithmetic alone, the same bits on every machine.
    /// </summary>
    public static double FifthRoot(double x)
    {
        if (!(x > 0.0))
            return 0.0;
        if (double.IsPositiveInfinity(x))
            return x;
        double y = Math.Sqrt(Math.Sqrt(x));
        for (int k = 0; k < 60; k++)
        {
            double y4 = y * y * y * y;
            double next = (4.0 * y + x / y4) / 5.0;
            if (next == y)
                break;
            y = next;
        }
        return y;
    }

    /// <summary>Per cell, whether the ground is steeper than <paramref name="degrees"/> (central differences, one-sided at the edges).</summary>
    public static bool[] SteepCells(HeightField bed, double degrees)
    {
        ArgumentNullException.ThrowIfNull(bed);
        int n = bed.Size;
        float[] heights = bed.Heights.ToArray();
        // Rounded to float, so the last bit of the platform's tangent cannot move a world (as the incision's talus).
        double limit = (float)Math.Tan(degrees * Math.PI / 180.0);
        var steep = new bool[n * n];
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
                steep[j * n + i] = SurfaceSlope(heights, n, bed.CellSize, i, j) > limit;
        });
        return steep;
    }

    /// <summary>The slope (rise over run) of a surface at sample (i, j): central differences, one-sided at the edges.</summary>
    private static double SurfaceSlope(float[] h, int n, double cell, int i, int j)
    {
        int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, n - 1), j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, n - 1);
        double dx = (h[j * n + i1] - (double)h[j * n + i0]) / ((i1 - i0) * cell);
        double dz = (h[j1 * n + i] - (double)h[j0 * n + i]) / ((j1 - j0) * cell);
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
