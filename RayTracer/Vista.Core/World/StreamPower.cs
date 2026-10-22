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
        int factor = CoarseFactor(grid.Size, grid.CellSize, settings.IncisionCell);
        var coarseGrid = new WorldGrid((grid.Size - 1) / factor + 1, grid.CellSize * factor);
        TerrainProfile profile = TerrainProfile.For(recipe.SetPieces);
        HeightField coarse = TerrainGenerator.Generate(recipe, seed, coarseGrid, profile.OctavesFor(coarseGrid.CellSize));
        float[] uplift = UpliftMap.On(recipe, seed, settings, coarse);
        // The rock on the coarse grid: per cell its beds' fold and its mottle, so a lookup per step is one table read.
        RockHardness rock = RockHardness.For(seed, settings);
        int n = coarse.Size;
        var fold = new double[n * n];
        var mottle = new float[n * n];
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                fold[j * n + i] = rock.Fold(coarse.XOf(i), coarse.ZOf(j));
                mottle[j * n + i] = (float)rock.Mottle(coarse.XOf(i), coarse.ZOf(j));
            }
        });
        (HeightField incised, StrataFrame frame) = Incise(coarse, settings, uplift, (c, y) => rock.AtSigma(y + fold[c], mottle[c]));
        return (coarse, incised, frame, new HeightField(n, coarse.CellSize, uplift));
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
    /// it every rock is the softest. Returns the incised ground mapped back to the base terrain's range, and that map as the rock's frame.
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
        for (int step = 0; step < settings.IncisionSteps; step++)
        {
            for (int c = 0; c < cells; c++)
                h[c] += uplift[c];
            DrainageMap drainage = DrainageMap.Of(new HeightField(n, ground.CellSize, (float[])h.Clone()));
            int lifted = step + 1;
            // Outlets first, so each cell's receiver already has this step's height (the implicit solve).
            for (int k = cells - 1; k >= 0; k--)
            {
                int c = drainage.Order[k];
                int r = drainage.Receiver(c);
                if (r < 0)
                    continue;
                if (drainage.FillDepth[c] > 0f)
                {
                    // A lake fills with sediment toward its outlet's height while the outlet cuts down.
                    h[c] += settings.IncisionLakeFill * drainage.FillDepth[c];
                    continue;
                }
                double length = drainage.StepLength(c);
                double height = h[c];
                // The rock at the cell's surface: harder rock cuts more slowly and stands steeper.
                double hard = hardness is null ? 0.0 : hardness(c, h[c] - lifted * (double)uplift[c]);
                if (drainage.Upstream[c] >= channel)
                {
                    double f = settings.IncisionRate * (1.0 + (settings.HardErodibility - 1.0) * hard) * Math.Sqrt(drainage.Area(c)) / length;
                    height = (height + f * h[r]) / (1.0 + f);
                }
                double talus = talusSoft + (talusHard - talusSoft) * hard;
                h[c] = (float)Math.Min(height, h[r] + talus * length);
            }
        }
        // Back to the base terrain's range of heights, so the uplift does not lift the world under the cameras: a linear map,
        // which changes no drainage direction and no lake.
        float top = h.Max(), bottom = h.Min();
        double scale = span / Math.Max(top - bottom, 1e-3);
        for (int c = 0; c < cells; c++)
            h[c] = (float)(low + (h[c] - bottom) * scale);
        return (new HeightField(n, ground.CellSize, h), new StrataFrame(low, bottom, scale, settings.IncisionSteps));
    }

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
