using Vista.Recipes;

namespace Vista.World;

/// <summary>
/// The drainage stage (vista-plan §10 M1.11a): before the pipe-model erosion carves gullies and lays down loose material on the
/// world grid, rivers incise the base terrain on a coarse grid (<see cref="ErosionSettings.IncisionCell"/>) by the stream-power
/// law, <c>∂h/∂t = U − K·A^½·S</c> (uplift <c>U</c>, upstream area <c>A</c>, slope <c>S</c> toward the receiver), solved
/// implicitly cell by cell from the outlets upstream (Braun and Willett 2013, as in Cordonnier et al. 2016). The water is routed
/// over the filled surface (<see cref="DrainageMap"/>), so a hollow's whole catchment drains through its lowest rim: that rim
/// cuts down fastest, the lake behind it fills with sediment a little each step, and the closed basins of the base terrain become
/// valleys that join. Cells under a lake do not incise; cells that drain less than a channel's area do not either (hillslopes);
/// every cell is kept within a talus slope of the cell it drains into, so incised valleys widen into V shapes. The ground keeps
/// rising by the uplift map (<see cref="UpliftMap"/>, M1.11d), so the massifs stand where it is high, and the rock's hardness
/// (<see cref="RockHardness"/>, M1.11d) sets each cell's erodibility and talus slope, so hard beds stand as cliffs and ledges;
/// the result is mapped back to the base terrain's range of heights.
/// </summary>
/// <remarks>
/// Single-threaded where order matters (the solve runs in the drainage's order, in double precision), so the result is a pure
/// function of the base terrain and the settings, the same on every machine.
/// </remarks>
public static class StreamPower
{
    /// <summary>The change of the ground, in metres, at which the incision has removed all of the base terrain's finer detail.</summary>
    public const float DetailFadeMetres = 25f;

    /// <summary>
    /// The ground the pipe-model erosion starts from, with the frame of its rock (<see cref="StrataFrame"/>) and the uplift per
    /// step the incision lifted it by (on the incision grid; null without an incision): <paramref name="ground"/> (the base terrain
    /// on the world grid) when the settings run no incision; else the base terrain on the coarse grid incised (<see cref="Coarse"/>)
    /// and brought back to the world grid (<see cref="Refine"/>).
    /// </summary>
    public static (HeightField Ground, StrataFrame Frame, HeightField? Uplift) Prepare(HeightField ground, Recipe recipe, ulong seed, ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.IncisionSteps == 0)
            return (ground, StrataFrame.Identity, null);
        (HeightField coarse, HeightField incised, StrataFrame frame, HeightField uplift) = Coarse(recipe, seed, new WorldGrid(ground.Size, ground.CellSize), settings);
        return (Refine(incised, coarse, ground, settings.ChannelAreaKm2), frame, uplift);
    }

    /// <summary>
    /// The base terrain on the incision grid of a world on <paramref name="grid"/> (with the octaves that grid holds), and the
    /// same incised (<see cref="Incise"/>) under the recipe's uplift map and the seed's rock, with the frame it leaves the rock in
    /// and the uplift per step on that grid.
    /// </summary>
    public static (HeightField Base, HeightField Incised, StrataFrame Frame, HeightField Uplift) Coarse(Recipe recipe, ulong seed, WorldGrid grid,
        ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(settings);
        HeightField coarse = CoarseBase(recipe, seed, grid, settings);
        float[] uplift = UpliftMap.On(recipe, seed, settings, coarse);
        // The rock on the coarse grid: per cell its beds' fold, its mottle and how massive it is, so a lookup per step is a table read.
        RockHardness rock = RockHardness.For(seed, settings);
        int n = coarse.Size;
        var fold = new double[n * n];
        var mottle = new float[n * n];
        var massive = new float[n * n];
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                fold[j * n + i] = rock.Fold(coarse.XOf(i), coarse.ZOf(j));
                mottle[j * n + i] = (float)rock.Mottle(coarse.XOf(i), coarse.ZOf(j));
                massive[j * n + i] = (float)rock.Massive(coarse.XOf(i), coarse.ZOf(j));
            }
        });
        (HeightField incised, StrataFrame frame) = Incise(coarse, settings, uplift, (c, y) => rock.AtSigma(y + fold[c], mottle[c], massive[c]));
        return (coarse, incised, frame, new HeightField(n, coarse.CellSize, uplift));
    }

    /// <summary>
    /// The uplift per step on the incision grid of a world on <paramref name="grid"/>, as <see cref="Coarse"/> lifts it, without
    /// incising (vista-plan §10 M1.12): what a world read from the cache needs to find its rock's beds (<see cref="StrataGrid"/>).
    /// Null when the settings run no incision.
    /// </summary>
    public static HeightField? UpliftField(Recipe recipe, ulong seed, WorldGrid grid, ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.IncisionSteps == 0)
            return null;
        HeightField coarse = CoarseBase(recipe, seed, grid, settings);
        return new HeightField(coarse.Size, coarse.CellSize, UpliftMap.On(recipe, seed, settings, coarse));
    }

    /// <summary>The base terrain on the incision grid of a world on <paramref name="grid"/>, with the octaves that grid holds.</summary>
    private static HeightField CoarseBase(Recipe recipe, ulong seed, WorldGrid grid, ErosionSettings settings)
    {
        int factor = CoarseFactor(grid.Size, grid.CellSize, settings.IncisionCell);
        var coarseGrid = new WorldGrid((grid.Size - 1) / factor + 1, grid.CellSize * factor);
        TerrainProfile profile = TerrainProfile.For(recipe.SetPieces);
        return TerrainGenerator.Generate(recipe, seed, coarseGrid, profile.OctavesFor(coarseGrid.CellSize));
    }

    /// <summary>
    /// <paramref name="incised"/> brought onto the finer grid of <paramref name="fineBase"/> (whose grid it nests in, with
    /// <paramref name="coarseBase"/> the base terrain on its own grid): upsampled, its channels of
    /// <paramref name="channelAreaKm2"/> and more carved (<see cref="CarveLinks"/>), and the finer base terrain's detail added back
    /// where the incision left the ground as it was.
    /// </summary>
    public static HeightField Refine(HeightField incised, HeightField coarseBase, HeightField fineBase, double channelAreaKm2)
    {
        ArgumentNullException.ThrowIfNull(incised);
        ArgumentNullException.ThrowIfNull(coarseBase);
        ArgumentNullException.ThrowIfNull(fineBase);
        int factor = CoarseFactor(fineBase.Size, fineBase.CellSize, incised.CellSize);
        float[] carved = Upsample(incised, factor);
        CarveLinks(carved, incised, factor, channelAreaKm2);
        float[] smooth = Upsample(coarseBase, factor);
        ReadOnlySpan<float> fine = fineBase.Heights;
        var heights = new float[fine.Length];
        for (int c = 0; c < heights.Length; c++)
        {
            // The base terrain's finer detail belongs to its surface: where the incision cut that surface away or buried it, the
            // detail goes with it (fully at DetailFadeMetres of change).
            float kept = Math.Clamp(1f - Math.Abs(carved[c] - smooth[c]) / DetailFadeMetres, 0f, 1f);
            heights[c] = carved[c] + (fine[c] - smooth[c]) * kept;
        }
        return new HeightField(fineBase.Size, fineBase.CellSize, heights);
    }

    /// <summary>
    /// The coarse grid's samples per world-grid sample: <paramref name="coarseCell"/> / <paramref name="cellSize"/>, which must be
    /// a whole number that divides the grid's intervals; 1 when the world grid is already as coarse (the incision then runs on it).
    /// </summary>
    public static int CoarseFactor(int size, double cellSize, double coarseCell)
    {
        if (coarseCell <= cellSize)
            return 1;
        int factor = (int)Math.Round(coarseCell / cellSize);
        if (factor < 1 || Math.Abs(factor * cellSize - coarseCell) > 1e-9 * coarseCell || (size - 1) % factor != 0)
            throw new ArgumentException($"A {coarseCell} m incision grid does not nest in a {size}² grid of {cellSize} m cells.", nameof(coarseCell));
        return factor;
    }

    /// <summary>
    /// Runs <see cref="ErosionSettings.IncisionSteps"/> steps of stream-power incision on <paramref name="ground"/>, lifting each
    /// cell by <paramref name="uplift"/> (metres per step, row-major on the ground's grid) every step. <paramref name="hardness"/>
    /// gives the hardness of the rock in cell <c>c</c> at a height in the rock's frame (the height less the uplift so far); without
    /// it every rock is the softest. The incised ground is mapped back to the base terrain's range, and that map is the rock's frame.
    /// Then (vista-plan §10 M1.13) <see cref="ErosionSettings.GlacialSteps"/> steps of glacial carving on the finished world, in its
    /// own metres: the ice cuts where it covers the ground and the rivers where it does not, without uplift (the glacial cycles are
    /// short beside the uplift's time) and without a second map, so the carving deepens the valleys under the peaks it leaves.
    /// Returns the ground and the rock's frame.
    /// </summary>
    public static (HeightField Incised, StrataFrame Frame) Incise(HeightField ground, ErosionSettings settings, float[] uplift,
        Func<int, double, float>? hardness = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(uplift);
        int n = ground.Size;
        int cells = n * n;
        if (uplift.Length != cells)
            throw new ArgumentException($"Expected {cells} uplift values, got {uplift.Length}.", nameof(uplift));
        float[] h = ground.Heights.ToArray();
        int channel = (int)Math.Ceiling(settings.ChannelAreaKm2 * 1e6 / (ground.CellSize * ground.CellSize));
        // Rounded to float like every other setting, so the last bit of the platform's tangent cannot move the result.
        double talusSoft = (float)Math.Tan(settings.IncisionTalusDegrees * Math.PI / 180.0);
        double talusHard = (float)Math.Tan(settings.IncisionTalusHardDegrees * Math.PI / 180.0);
        float low = ground.MinHeight, span = Math.Max(ground.MaxHeight - low, 1e-3f);
        var iced = new bool[cells];

        // One step of the rivers: outlets first, so each cell's receiver already has this step's height (the implicit solve), with
        // the rock's hardness at each cell's height in the rock's frame; the cells under ice (the carving's) only keep their talus.
        void Rivers(Func<int, double> rockHeight)
        {
            DrainageMap drainage = DrainageMap.Of(new HeightField(n, ground.CellSize, (float[])h.Clone()));
            for (int k = cells - 1; k >= 0; k--)
            {
                int c = drainage.Order[k];
                int r = drainage.Receiver(c);
                if (r < 0)
                    continue;
                if (drainage.FillDepth[c] > 0f && !iced[c])
                {
                    // A lake fills with sediment toward its outlet's height while the outlet cuts down (not under ice).
                    h[c] += settings.IncisionLakeFill * drainage.FillDepth[c];
                    continue;
                }
                double length = drainage.StepLength(c);
                double height = h[c];
                // The rock at the cell's surface: harder rock cuts more slowly and stands steeper.
                double hard = hardness is null ? 0.0 : hardness(c, rockHeight(c));
                if (drainage.Upstream[c] >= channel && !iced[c])
                {
                    double f = settings.IncisionRate * (1.0 + (settings.HardErodibility - 1.0) * hard) * Math.Sqrt(drainage.Area(c)) / length;
                    height = (height + f * h[r]) / (1.0 + f);
                }
                // Under the ice the glacier cut the bed this step (Carve); the walls keep to their talus all the same.
                double talus = talusSoft + (talusHard - talusSoft) * hard;
                h[c] = (float)Math.Min(height, h[r] + talus * length);
            }
        }

        for (int step = 0; step < settings.IncisionSteps; step++)
        {
            for (int c = 0; c < cells; c++)
                h[c] += uplift[c];
            int lifted = step + 1;
            Rivers(c => h[c] - lifted * (double)uplift[c]);
        }
        // Back to the base terrain's range of heights, so the uplift does not lift the world under the cameras: a linear map,
        // which changes no drainage direction and no lake.
        float top = h.Max(), bottom = h.Min();
        double scale = span / Math.Max(top - bottom, 1e-3);
        for (int c = 0; c < cells; c++)
            h[c] = (float)(low + (h[c] - bottom) * scale);
        var frame = new StrataFrame(low, bottom, scale, settings.IncisionSteps);

        // The glacial carving on the finished world.
        float[]? ice = null;
        for (int step = 0; step < settings.GlacialSteps; step++)
        {
            ice = Carve(h, n, ground.CellSize, settings, step, ice, iced, hardness is null ? null : c => hardness(c, frame.RockHeight(h[c], uplift[c])));
            Rivers(c => frame.RockHeight(h[c], uplift[c]));
        }
        return (new HeightField(n, ground.CellSize, h), frame);
    }

    /// <summary>
    /// One step of glacial carving (vista-plan §10 M1.13) on the world's heights <paramref name="h"/>, carving step
    /// <paramref name="carving"/>: the steady ice of the step's climate (<see cref="ErosionSettings.CarvingClimate"/>, its line in the
    /// world's metres; <see cref="GlacierFlow"/>, from <paramref name="previous"/>'s), and the bed under it lowered by
    /// <see cref="ErosionSettings.GlacialErosionMetres"/> per 100 m a year of sliding (half the ice's mean speed), times the erodibility of
    /// the rock there. Marks the cells the ice covers in <paramref name="iced"/>; returns the ice's thickness.
    /// </summary>
    private static float[] Carve(float[] h, int n, double cell, ErosionSettings settings, int carving, float[]? previous, bool[] iced,
        Func<int, double>? hardness)
    {
        IceField field = GlacierFlow.Solve(new HeightField(n, cell, (float[])h.Clone()), settings.CarvingClimate(carving), GlacierFlowSettings.Default,
            previous, previous is null ? FirstIterations : StepIterations);
        double perSpeed = settings.GlacialErosionMetres * SlidingShare / 100.0;
        double most = MaxCutShare * settings.GlacialErosionMetres;
        // Every cut from this step's heights first (a wall's slope reads its neighbours), then the cuts. Below the spill of the hollow it
        // has dug the ice must climb out again, which slows its sliding: the cut fades out over OverdeepeningMetres of depth, so the
        // troughs keep basins the erosion's pools fill with sediment (flat floors), not pits.
        double[] spill = DrainageMap.Flood(new HeightField(n, cell, (float[])h.Clone()));
        var cuts = new float[n * n];
        Parallel.For(0, n, j =>
        {
            for (int c = j * n; c < (j + 1) * n; c++)
            {
                iced[c] = field.Thickness[c] >= IcedMetres;
                if (!iced[c])
                    continue;
                double hard = hardness is null ? 0.0 : hardness(c);
                // Thin ice is frozen to its bed (the summits' caps and the snowfields): only a thick glacier, warm at its bed, slides and
                // abrades, so the carving cuts the valleys the glaciers fill and spares the tops they cover; its walls are as warm as its
                // middle. The ice abrades its bed across the bed's slope, which lowers a wall by that over the slope's cosine: the walls
                // retreat sideways as the floor deepens.
                double warm = Math.Min(field.Body[c] / WarmBedMetres, 1.0);
                double across = Math.Min(Math.Sqrt(1.0 + BedSlopeSquared(h, n, cell, c)), MaxWallFactor);
                double deep = Math.Clamp((spill[c] - h[c]) / OverdeepeningMetres, 0.0, 1.0);
                // The grid's edge is every water's and ice's outlet, where no hollow can form to stop the cut: it fades out there.
                int i = c % n, edge = Math.Min(Math.Min(i, n - 1 - i), Math.Min(j, n - 1 - j));
                double inside = Math.Clamp((double)edge / EdgeCells, 0.0, 1.0);
                double cut = perSpeed * field.Speed[c] * warm * across * (1.0 + (settings.HardErodibility - 1.0) * hard) * (1.0 - deep * deep * (3.0 - 2.0 * deep))
                    * inside;
                cuts[c] = (float)Math.Min(cut, most);
            }
        });
        for (int c = 0; c < n * n; c++)
            h[c] -= cuts[c];
        return field.Thickness;
    }

    /// <summary>The square of the slope (rise over run) of heights <paramref name="h"/> at cell <paramref name="c"/>: central differences, one-sided at the edges.</summary>
    private static double BedSlopeSquared(float[] h, int n, double cell, int c)
    {
        int i = c % n, j = c / n;
        int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, n - 1), j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, n - 1);
        double dx = (h[j * n + i1] - (double)h[j * n + i0]) / ((i1 - i0) * cell);
        double dz = (h[j1 * n + i] - (double)h[j0 * n + i]) / ((j1 - j0) * cell);
        return dx * dx + dz * dz;
    }

    /// <summary>
    /// How deep below its spill a glacier's hollow may be dug, m: less than the pools the erosion fills with sediment
    /// (<see cref="ErosionSettings.PoolFillMetres"/>), so a glacial trough's basin ends as a flat floor.
    /// </summary>
    public const double OverdeepeningMetres = 80.0;

    /// <summary>The cells from the grid's edge over which the carving fades in.</summary>
    public const int EdgeCells = 8;

    /// <summary>The most a wall's slope multiplies the carving's cut (a 60° wall's 1 / cos).</summary>
    public const double MaxWallFactor = 2.0;

    /// <summary>The ice solve's iterations at the carving's first step, from no ice.</summary>
    public const int FirstIterations = 40;

    /// <summary>The ice solve's iterations at every later carving step, from the last step's ice (the climate moves slowly).</summary>
    public const int StepIterations = 4;

    /// <summary>The ice that counts as covering a cell in the carving, m: thinner is a snowfield, which does not slide.</summary>
    public const float IcedMetres = 10f;

    /// <summary>
    /// The ice over which a glacier's bed warms to melting, m: thinner ice is frozen to its bed and does not slide, so the carving's
    /// abrasion grows with the thickness up to this (the summits' thin caps cut nothing; Sugden's selective linear erosion).
    /// </summary>
    public const double WarmBedMetres = 150.0;

    /// <summary>The share of a glacier's mean speed that is sliding on its bed, which is what abrades it.</summary>
    public const double SlidingShare = 0.5;

    /// <summary>The most a carving step lowers the bed, in <see cref="ErosionSettings.GlacialErosionMetres"/> (an icefall's speed is not abrasion).</summary>
    public const double MaxCutShare = 4.0;

    /// <summary>
    /// Lowers <paramref name="fine"/> (<paramref name="coarse"/> upsampled by <see cref="Upsample"/>) along every flow link of the
    /// coarse surface to the straight line between the link's two heights. Bilinear interpolation is that line along a grid edge,
    /// but across a cell it averages in the other two corners: a channel that runs diagonally down a V-shaped valley would get a
    /// saddle of half the valley's depth in every coarse cell, and the fine grid's water would pool behind each one. Only
    /// channels are carved (links from cells draining at least <paramref name="channelAreaKm2"/>): on a hillslope the lines
    /// would run side by side and groove it.
    /// </summary>
    public static void CarveLinks(float[] fine, HeightField coarse, int factor, double channelAreaKm2)
    {
        ArgumentNullException.ThrowIfNull(fine);
        ArgumentNullException.ThrowIfNull(coarse);
        int nc = coarse.Size;
        int n = (nc - 1) * factor + 1;
        if (fine.Length != n * n)
            throw new ArgumentException($"Expected {n * n} fine heights, got {fine.Length}.", nameof(fine));
        DrainageMap drainage = DrainageMap.Of(coarse);
        int channel = (int)Math.Ceiling(channelAreaKm2 * 1e6 / drainage.CellArea);
        // Each link lowers the fine cells strictly between its two ends, the coarse samples, which keep their heights. Where two
        // diagonal links cross, the lower line wins whichever comes first.
        for (int jc = 0; jc < nc; jc++)
        {
            for (int ic = 0; ic < nc; ic++)
            {
                int c = jc * nc + ic;
                byte d = drainage.Direction[c];
                if (d == DrainageMap.Outlet || drainage.Upstream[c] < channel)
                    continue;
                int di = DrainageMap.Di[d], dj = DrainageMap.Dj[d];
                double from = coarse[ic, jc], to = coarse[ic + di, jc + dj];
                for (int t = 1; t < factor; t++)
                {
                    int f = (jc * factor + t * dj) * n + ic * factor + t * di;
                    fine[f] = Math.Min(fine[f], (float)(from + (to - from) * t / factor));
                }
            }
        }
    }

    /// <summary>
    /// <paramref name="coarse"/> bilinearly interpolated onto the grid <paramref name="factor"/> times finer over the same extent
    /// (coarse sample <c>(I, J)</c> is fine sample <c>(factor·I, factor·J)</c>).
    /// </summary>
    public static float[] Upsample(HeightField coarse, int factor)
    {
        ArgumentNullException.ThrowIfNull(coarse);
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
        int nc = coarse.Size;
        int n = (nc - 1) * factor + 1;
        var fine = new float[n * n];
        Parallel.For(0, n, j =>
        {
            int jc = Math.Min(j / factor, nc - 2);
            double v = (j - jc * factor) / (double)factor;
            for (int i = 0; i < n; i++)
            {
                int ic = Math.Min(i / factor, nc - 2);
                double u = (i - ic * factor) / (double)factor;
                double bottom = coarse[ic, jc] + (coarse[ic + 1, jc] - (double)coarse[ic, jc]) * u;
                double top = coarse[ic, jc + 1] + (coarse[ic + 1, jc + 1] - (double)coarse[ic, jc + 1]) * u;
                fine[j * n + i] = (float)(bottom + (top - bottom) * v);
            }
        });
        return fine;
    }
}
