namespace Vista.World;

/// <summary>
/// Where a world's cold air pools on a clear night (vista-plan §10 M1.13b): for every place, the valley floor its air drains to, how
/// well that valley holds the cold air, and how deep a pool its walls allow. M1.13a's pool filled every valley to one altitude; here
/// each valley has its own floor, so a hanging valley holds its own pool and the main valley its own, and the night's depth (from the
/// weather, <see cref="Weather.ValleyHaze"/>) is scaled by each valley's shape.
/// <list type="bullet">
///   <item><b>The floor</b>: at night the air cooled by the ground slides down the fall line until it reaches a valley floor, where it
///   collects. A cell's floor is the first cell on its way down (the drainage, <see cref="DrainageMap"/>) that drains at least
///   <see cref="ChannelAreaSquareMetres"/> of land: the height above it is the terrain's height above its nearest drainage (Rennó
///   et al. 2008's HAND). Both slopes of a valley drain to the same floor, so a pool's top lies level across it and follows the valley
///   down its length. The floor is the water's surface of the drainage (the ground plus <see cref="DrainageMap.FillDepth"/>), so a
///   closed hollow fills flat to its rim.</item>
///   <item><b>How well it holds</b> (<see cref="PoolLevel.Hold"/>, 0 to 1): cold air drains out of a steep valley as a shallow
///   down-valley flow and pools deep on flat floors and in basins (Whiteman 2000, ch. 9; the closed Gstettneralm basin holds the
///   coldest pools in the Alps). The hold falls with the floor's gradient over the next <see cref="GradientReachMetres"/> downstream,
///   to a half at <see cref="HalfHoldGradient"/>: <c>1/(1 + (g/g½)²)</c>.</item>
///   <item><b>The cap</b> (<see cref="PoolLevel.Cap"/>): a pool fills its valley, not the peaks above it. The cap is how far the
///   valley's surroundings (the ground smoothed over <see cref="ReliefSigmaMetres"/>) stand above its floor: about half a V-valley's
///   depth, as M1.13a's cap was half the world's relief.</item>
/// </list>
/// Two levels: the eroded world's grid, every <see cref="FineFactor"/>th sample (64 m on the default grid), and the base terrain
/// around it out to the far field's 32 m grid's reach (256 m cells), so the valleys beyond the world's tile hold their pools too.
///
/// <para>Pure: a function of the surfaces' bits, the same on every machine.</para>
/// </summary>
public sealed class PoolMap
{
    /// <summary>The land a valley floor drains, m²: the cells that drain at least this much are floors.</summary>
    public const double ChannelAreaSquareMetres = 1.0e6;

    /// <summary>How far downstream a floor's gradient is measured, m.</summary>
    public const double GradientReachMetres = 1000.0;

    /// <summary>The floor's gradient at which a valley holds half of the night's pool: steeper valleys drain theirs away.</summary>
    public const double HalfHoldGradient = 0.06;

    /// <summary>The scale (a Gaussian's σ, m) of the surroundings whose height above a floor caps its pool.</summary>
    public const double ReliefSigmaMetres = 1500.0;

    /// <summary>The fine level's cell in world grid cells.</summary>
    public const int FineFactor = 8;

    /// <summary>
    /// How deep the drainage's water stands in a hollow for the hollow to be a floor of its own, m: a closed basin holds its pool
    /// whatever land drains into it, while a puddle the erosion left on a slope does not gather a valley's pool.
    /// </summary>
    public const double MinHollowMetres = 10.0;

    /// <summary>The far level's cell in its own grid's cells (<see cref="FarCellMetres"/> over the far grid's 128 m).</summary>
    public const int FarFactor = 2;

    /// <summary>The far level's grid: the base terrain every this many metres.</summary>
    public const double FarGridMetres = 128.0;

    /// <summary>The far level's cell, m.</summary>
    public const double FarCellMetres = FarGridMetres * FarFactor;

    /// <summary>The map of <paramref name="levels"/>, finest first.</summary>
    public PoolMap(IReadOnlyList<PoolLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count == 0)
            throw new ArgumentException("A pool map needs at least one level.", nameof(levels));
        Levels = levels;
    }

    /// <summary>The levels, finest first: the world's grid, then the far field around it.</summary>
    public IReadOnlyList<PoolLevel> Levels { get; }

    /// <summary>A 64-bit fingerprint of every level's grid and values.</summary>
    public ulong ContentHash()
    {
        ulong hash = StableHash.FnvStart;
        foreach (PoolLevel level in Levels)
            hash = StableHash.Combine(hash, level.ContentHash());
        return hash;
    }

    /// <summary>
    /// The pool map of a world whose eroded surface is <paramref name="surface"/> with <paramref name="drainage"/>, and whose base terrain
    /// beyond it is <paramref name="far"/> (the base terrain on a <see cref="FarGridMetres"/> grid, centred like the world's; null for the
    /// world's level alone).
    /// </summary>
    public static PoolMap For(HeightField surface, DrainageMap drainage, HeightField? far)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(drainage);
        PoolLevel fine = Level(surface, drainage, FactorFor(surface.Size, FineFactor), basins: true);
        if (far is null)
            return new PoolMap([fine]);
        return new PoolMap([fine, Level(far, DrainageMap.Of(far), FactorFor(far.Size, FarFactor), basins: false)]);
    }

    /// <summary>The largest factor up to <paramref name="factor"/> that divides a grid of <paramref name="size"/>² samples into whole cells.</summary>
    private static int FactorFor(int size, int factor)
    {
        while ((size - 1) % factor != 0)
            factor--;
        return factor;
    }

    /// <summary>
    /// The base terrain the far level is measured on: <paramref name="seed"/>'s base terrain under <paramref name="profile"/> on a
    /// <see cref="FarGridMetres"/> grid as wide as the hierarchy's 32 m far field (<see cref="TerrainHierarchy.Ratio1"/> world grids),
    /// with the octaves that grid can hold.
    /// </summary>
    public static HeightField FarTerrain(Recipes.Recipe recipe, ulong seed, WorldGrid world)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(world);
        double extent = (world.Size - 1) * world.CellSize * TerrainHierarchy.Ratio1;
        int size = (int)Math.Round(extent / FarGridMetres) + 1;
        TerrainProfile profile = TerrainProfile.For(recipe.SetPieces);
        return TerrainGenerator.Generate(recipe, seed, new WorldGrid(size, FarGridMetres), profile.OctavesFor(FarGridMetres));
    }

    /// <summary>
    /// One level from <paramref name="surface"/> and its <paramref name="drainage"/>, every <paramref name="factor"/>th sample. With
    /// <paramref name="basins"/> a closed hollow's floor is the drainage's water level, so its pool fills it to its rim (an eroded world's
    /// hollows are basins); without, every floor is the ground's own height (the far field's un-eroded base terrain is full of hollows
    /// that are noise, not basins, and filled to their rims they would drown it).
    /// </summary>
    public static PoolLevel Level(HeightField surface, DrainageMap drainage, int factor, bool basins)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(drainage);
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
        int n = surface.Size;
        if (drainage.Size != n)
            throw new ArgumentException("The drainage is not on the surface's grid.", nameof(drainage));
        if ((n - 1) % factor != 0)
            throw new ArgumentException($"A {n}² grid does not divide into cells of {factor}.", nameof(factor));

        ReadOnlySpan<float> heights = surface.Heights;
        var level = new float[n * n];
        for (int c = 0; c < level.Length; c++)
            level[c] = basins ? heights[c] + drainage.FillDepth[c] : heights[c];

        // The floors: each cell's first cell downstream that drains a valley's worth of land, or lies in a closed hollow (receivers
        // first, so a cell's floor is its receiver's unless it is a floor itself). A cell that leaves the grid before reaching one has none.
        int floorCells = (int)Math.Ceiling(ChannelAreaSquareMetres / drainage.CellArea);
        bool Hollow(int c) => basins && drainage.FillDepth[c] >= MinHollowMetres;
        var floor = new int[n * n];
        int[] order = drainage.Order;
        for (int k = order.Length - 1; k >= 0; k--)
        {
            int c = order[k];
            int r = drainage.Receiver(c);
            floor[c] = drainage.Upstream[c] >= floorCells || Hollow(c) ? c : r >= 0 ? floor[r] : -1;
        }

        // How well each floor holds its pool: by its gradient downstream; a closed hollow holds all of it.
        var hold = new float[n * n];
        Parallel.For(0, n, j =>
        {
            for (int c = j * n; c < (j + 1) * n; c++)
            {
                if (Hollow(c))
                {
                    hold[c] = 1f;
                    continue;
                }
                if (drainage.Upstream[c] < floorCells)
                    continue;
                double length = 0.0;
                int x = c;
                while (length < GradientReachMetres)
                {
                    int r = drainage.Receiver(x);
                    if (r < 0)
                        break;
                    length += drainage.StepLength(x);
                    x = r;
                }
                double gradient = length > 0.0 ? Math.Max(level[c] - level[x], 0.0) / length : 0.0;
                double g = gradient / HalfHoldGradient;
                hold[c] = (float)(1.0 / (1.0 + g * g));
            }
        });

        // The surroundings: the ground averaged over each map cell, then smoothed over the relief's scale.
        int m = (n - 1) / factor + 1;
        double cell = surface.CellSize * factor;
        float[] block = BlockMean(level, n, factor, m);
        float[] ground = BlockMean(heights.ToArray(), n, factor, m);
        float[] around = Gaussian(block, m, ReliefSigmaMetres / cell);

        // Each map cell's floor, hold and cap: the means over its block of the floors its cells drain to.
        var floors = new float[m * m];
        var holds = new float[m * m];
        var caps = new float[m * m];
        Parallel.For(0, m, J =>
        {
            for (int I = 0; I < m; I++)
            {
                double sumFloor = 0.0, sumHold = 0.0, sumCap = 0.0;
                int count = 0, held = 0;
                double lowest = double.MaxValue;
                for (int j = Math.Max(J * factor - factor / 2, 0); j <= Math.Min(J * factor + factor / 2, n - 1); j++)
                {
                    for (int i = Math.Max(I * factor - factor / 2, 0); i <= Math.Min(I * factor + factor / 2, n - 1); i++)
                    {
                        int f = floor[j * n + i];
                        count++;
                        if (f < 0)
                            continue;
                        held++;
                        double fl = level[f];
                        sumFloor += fl;
                        sumHold += hold[f];
                        double surroundings = Bilinear(around, m, (f % n) / (double)factor, (f / n) / (double)factor);
                        sumCap += Math.Max(surroundings - fl, 0.0);
                        lowest = Math.Min(lowest, level[j * n + i]);
                    }
                }
                int k = J * m + I;
                if (held == 0)
                {
                    // No floor: the ground itself, holding nothing.
                    floors[k] = block[k];
                    continue;
                }
                floors[k] = (float)(sumFloor / held);
                holds[k] = (float)(sumHold / count);
                caps[k] = (float)(sumCap / held);
            }
        });
        return new PoolLevel(m, cell, surface.OriginX, surface.OriginZ, floors, holds, caps) { Ground = ground };
    }

    /// <summary>The mean of each <paramref name="factor"/>-wide block of <paramref name="values"/> around every <paramref name="factor"/>th sample.</summary>
    private static float[] BlockMean(float[] values, int n, int factor, int m)
    {
        var mean = new float[m * m];
        Parallel.For(0, m, J =>
        {
            for (int I = 0; I < m; I++)
            {
                double sum = 0.0;
                int count = 0;
                for (int j = Math.Max(J * factor - factor / 2, 0); j <= Math.Min(J * factor + factor / 2, n - 1); j++)
                {
                    for (int i = Math.Max(I * factor - factor / 2, 0); i <= Math.Min(I * factor + factor / 2, n - 1); i++)
                    {
                        sum += values[j * n + i];
                        count++;
                    }
                }
                mean[J * m + I] = (float)(sum / count);
            }
        });
        return mean;
    }

    /// <summary>A separable Gaussian blur of σ <paramref name="sigma"/> samples, the edge's samples standing in beyond the edge.</summary>
    private static float[] Gaussian(float[] values, int m, double sigma)
    {
        int radius = (int)Math.Ceiling(3.0 * sigma);
        var weights = new double[2 * radius + 1];
        double total = 0.0;
        for (int k = -radius; k <= radius; k++)
            total += weights[k + radius] = Math.Exp(-0.5 * k * k / (sigma * sigma));
        for (int k = 0; k < weights.Length; k++)
            weights[k] /= total;
        var rows = new float[m * m];
        Parallel.For(0, m, j =>
        {
            for (int i = 0; i < m; i++)
            {
                double sum = 0.0;
                for (int k = -radius; k <= radius; k++)
                    sum += weights[k + radius] * values[j * m + Math.Clamp(i + k, 0, m - 1)];
                rows[j * m + i] = (float)sum;
            }
        });
        var result = new float[m * m];
        Parallel.For(0, m, j =>
        {
            for (int i = 0; i < m; i++)
            {
                double sum = 0.0;
                for (int k = -radius; k <= radius; k++)
                    sum += weights[k + radius] * rows[Math.Clamp(j + k, 0, m - 1) * m + i];
                result[j * m + i] = (float)sum;
            }
        });
        return result;
    }

    private static double Bilinear(float[] values, int m, double u, double v)
    {
        u = Math.Clamp(u, 0.0, m - 1);
        v = Math.Clamp(v, 0.0, m - 1);
        int i = Math.Min((int)u, m - 2), j = Math.Min((int)v, m - 2);
        double fu = u - i, fv = v - j;
        double a = values[j * m + i] + (values[j * m + i + 1] - values[j * m + i]) * fu;
        double b = values[(j + 1) * m + i] + (values[(j + 1) * m + i + 1] - values[(j + 1) * m + i]) * fu;
        return a + (b - a) * fv;
    }
}

/// <summary>
/// One level of a <see cref="PoolMap"/>: a square grid of <see cref="Size"/>² samples <see cref="CellMetres"/> apart whose first sample
/// sits at world (<see cref="OriginX"/>, <see cref="OriginZ"/>), row-major (<c>j·Size + i</c>), like a <see cref="HeightField"/>.
/// </summary>
/// <param name="Size">Samples per side.</param>
/// <param name="CellMetres">Spacing between samples, m.</param>
/// <param name="OriginX">World x of column 0.</param>
/// <param name="OriginZ">World z of row 0.</param>
/// <param name="Floor">The valley floor the air over each sample drains to, m above the world's height 0.</param>
/// <param name="Hold">How well that valley holds the night's pool, 0 to 1 (0 where the air drains off the map).</param>
/// <param name="Cap">The deepest pool the valley's walls allow, m above its floor.</param>
public sealed record PoolLevel(int Size, double CellMetres, double OriginX, double OriginZ, float[] Floor, float[] Hold, float[] Cap)
{
    /// <summary>The ground's mean height over each sample's cell, m above the world's height 0: where a pool lies over land, not over a ridge.</summary>
    public float[] Ground { get; init; } = [];

    /// <summary>The level's side, m.</summary>
    public double Extent => (Size - 1) * CellMetres;

    /// <summary>A 64-bit fingerprint of the grid and every value's bits.</summary>
    public ulong ContentHash()
    {
        ulong hash = StableHash.Combine(StableHash.FnvStart, (ulong)Size);
        hash = StableHash.Combine(hash, BitConverter.DoubleToUInt64Bits(CellMetres));
        hash = StableHash.Combine(hash, BitConverter.DoubleToUInt64Bits(OriginX));
        hash = StableHash.Combine(hash, BitConverter.DoubleToUInt64Bits(OriginZ));
        Span<byte> buffer = stackalloc byte[4];
        foreach (float[] map in new[] { Floor, Hold, Cap, Ground })
        {
            foreach (float v in map)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(v));
                hash = StableHash.Fnv1a(hash, buffer);
            }
        }
        return hash;
    }

    /// <summary>Bilinear interpolation of <paramref name="values"/> (one of this level's maps) at world (<paramref name="x"/>, <paramref name="z"/>), the edge's beyond it.</summary>
    public double Sample(float[] values, double x, double z)
    {
        ArgumentNullException.ThrowIfNull(values);
        double u = Math.Clamp((x - OriginX) / CellMetres, 0.0, Size - 1);
        double v = Math.Clamp((z - OriginZ) / CellMetres, 0.0, Size - 1);
        int i = Math.Min((int)u, Size - 2), j = Math.Min((int)v, Size - 2);
        double fu = u - i, fv = v - j;
        double a = values[j * Size + i] + (values[j * Size + i + 1] - values[j * Size + i]) * fu;
        double b = values[(j + 1) * Size + i] + (values[(j + 1) * Size + i + 1] - values[(j + 1) * Size + i]) * fu;
        return a + (b - a) * fv;
    }
}
