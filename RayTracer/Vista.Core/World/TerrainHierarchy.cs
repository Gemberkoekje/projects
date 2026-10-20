using System.Numerics;

namespace Vista.World;

/// <summary>
/// The world as a hierarchy the view-dependent mesh refines (vista-plan §4.3, §10 M1.9): a 4-8 mesh — a square split along a
/// diagonal into two right triangles, each split again at the midpoint of its long edge — over a square of the plane 32 world
/// grids wide, holding three nested grids of heights and, at every vertex, how far that vertex may stray from the edge it
/// splits (its error) and how far its descendants reach (its radius), both saturated so that a parent always counts at least as
/// much as its children (Lindstrom &amp; Pascucci, "Visualization of large terrains made easy", 2001). That saturation is what keeps
/// the mesh free of cracks: a vertex the camera needs implies its parents, so every triangle it splits has its neighbour split
/// too.
///
/// <para><b>Three grids.</b> At the centre, the eroded world grid (g0, 4097² × 8 m, 32.8 km); around it the base terrain sampled
/// every 4 cells (g1, 4097² × 32 m, 131 km) and every 32 cells (g2, 4097² × 256 m, 1049 km), the far field. The coarse grids
/// sample the base terrain's octaves they can hold (<see cref="TerrainProfile.OctavesFor"/>), so nothing aliases there, and
/// each grid blends over its outer <see cref="BlendCells"/> cells into the surface of the next one out, so the eroded world
/// fades into the base terrain and the 32 m far field into the 256 m one without a step. Every other vertex of the hierarchy —
/// between a grid's samples, or below the 8 m grid near the camera — lies on the straight edge it splits, plus the
/// <see cref="TerrainDetail"/>, which is everywhere.</para>
///
/// <para><b>Coordinates.</b> Vertices are integer points of the root square in sub-units, <see cref="Sub"/> to a g0 cell
/// (<see cref="SubLevels"/> halvings below the grid), so the root is <see cref="RootSize"/> = (size − 1)·32·<see cref="Sub"/>
/// sub-units wide (2²⁴ for the default grid). A point's level follows from its coordinates: with <c>t</c> the trailing zeros
/// of <c>I | J</c> and <c>s = 2^t</c>, both odd multiples of <c>s</c> make it the centre of a <c>2s</c>-square (a diagonal
/// level), one odd multiple the midpoint of an edge of length <c>2s</c> (an axis level). A <c>2s</c>-square with its lower
/// left corner at <c>(a, b)</c> is split along its main diagonal when <c>a/2s + b/2s</c> is even and along the other one when
/// it is odd: the diagonal that joins its two older corners.</para>
///
/// <para><b>What is stored.</b> Three stores hold heights, errors and radii on the grids' lattices: store 0 g0's square and
/// a margin of <see cref="Margin0Cells"/> cells around it, store 1 g1's square and <see cref="Margin1Steps"/> of its samples
/// around it, store 2 the root. The margins hold the vertices between a coarser grid's samples whose descendants reach into
/// a finer grid: their errors are the finer grid's, saturated up. Every vertex outside the stores is analytic: its height is
/// the mean of its edge's ends, its error the detail's bound for its level, its radius a bound from its level and the
/// steepest slope.</para>
///
/// <para>Built once per world (and detail); about 0.6 GB for the default grid; immutable afterwards, so meshes for any camera
/// read it concurrently.</para>
/// </summary>
public sealed class TerrainHierarchy
{
    /// <summary>Halvings of a g0 cell below the grid: 8 m / 2⁷ = 6.25 cm on the default grid.</summary>
    public const int SubLevels = 7;

    /// <summary>Sub-units per g0 cell.</summary>
    public const int Sub = 1 << SubLevels;

    /// <summary>g1's cell in g0 cells.</summary>
    public const int Ratio1 = 4;

    /// <summary>g2's cell in g0 cells.</summary>
    public const int Ratio2 = 32;

    /// <summary>Cells over which a grid blends into the next one out, in its own cells (2 km for g0, 8 km for g1).</summary>
    public const int BlendCells = 256;

    /// <summary>Store 0's margin around g0's square, in g0 cells.</summary>
    public const int Margin0Cells = 16;

    /// <summary>Store 1's margin around g1's square, in g1 cells.</summary>
    public const int Margin1Steps = 16;

    /// <summary>How much steeper the Catmull–Rom surface below the grid can be than the grid's own triangles.</summary>
    private const double CatmullRomSlope = 1.875 * 1.05;

    private readonly Store[] _stores;
    private readonly float[] _analyticError;
    private readonly float[] _analyticRadius;

    private TerrainHierarchy(HeightField surface, TerrainDetail detail, Store[] stores, float[] analyticError, float[] analyticRadius,
        double slopeBound, float minHeight, float maxHeight, IReadOnlyList<NormalMap> normalMaps, double seconds)
    {
        Surface = surface;
        NormalMaps = normalMaps;
        Detail = detail;
        _stores = stores;
        _analyticError = analyticError;
        _analyticRadius = analyticRadius;
        SlopeBound = slopeBound;
        MinHeight = minHeight;
        MaxHeight = maxHeight;
        BuildSeconds = seconds;
        RootSize = stores[2].Step * (stores[2].Count - 1);
        RootLog = BitOperations.Log2((uint)RootSize);
        SubUnit = surface.CellSize / Sub;
        RootOrigin = -0.5 * RootSize * SubUnit;
        G0Low = RootSize / 2 - (surface.Size - 1) / 2 * Sub;
        G0High = RootSize / 2 + (surface.Size - 1) / 2 * Sub;
    }

    /// <summary>The eroded (or base) world grid the hierarchy was built from.</summary>
    public HeightField Surface { get; }

    /// <summary>The detail added everywhere.</summary>
    public TerrainDetail Detail { get; }

    /// <summary>
    /// The smooth normals of g0 and g1, finest first (<see cref="NormalMap"/>): the shading coarse triangles get back. g2 needs none:
    /// beyond g1 its 256 m samples are wider than a pixel wherever the mesh reaches them.
    /// </summary>
    public IReadOnlyList<NormalMap> NormalMaps { get; }
    /// <summary>The root square's side in sub-units.</summary>
    public int RootSize { get; }

    /// <summary>log₂ of <see cref="RootSize"/>.</summary>
    public int RootLog { get; }

    /// <summary>Metres per sub-unit.</summary>
    public double SubUnit { get; }

    /// <summary>World x (and z) of sub-unit 0.</summary>
    public double RootOrigin { get; }

    /// <summary>The root's centre in sub-units (world x = z = 0).</summary>
    public int Centre => RootSize / 2;

    /// <summary>g0's square, in sub-units: [<see cref="G0Low"/>, <see cref="G0High"/>] on both axes.</summary>
    public int G0Low { get; }

    /// <summary>The far edge of g0's square, in sub-units.</summary>
    public int G0High { get; }

    /// <summary>The steepest slope of any grid's surface (rise over run), with a margin: bounds the analytic radii.</summary>
    public double SlopeBound { get; }

    /// <summary>The lowest height any grid holds, m (the detail can go <see cref="TerrainDetail.MaxAmplitude"/> further).</summary>
    public float MinHeight { get; }

    /// <summary>The highest height any grid holds, m.</summary>
    public float MaxHeight { get; }

    /// <summary>Wall time of <see cref="Build"/>, s.</summary>
    public double BuildSeconds { get; }

    /// <summary>The number of levels, coarsest (the root's centre, 0) to finest (<c>2·RootLog − 1</c>).</summary>
    internal int LevelCount => 2 * RootLog;

    /// <summary>
    /// Builds the hierarchy of <paramref name="surface"/> (the world grid, 2ⁿ + 1 samples a side) in the world of
    /// <paramref name="profile"/> and <paramref name="seed"/>, with <paramref name="detail"/> everywhere.
    /// </summary>
    public static TerrainHierarchy Build(HeightField surface, TerrainProfile profile, ulong seed, TerrainDetail detail)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(detail);
        if (!BitOperations.IsPow2(surface.Size - 1) || surface.Size < 9)
            throw new ArgumentException($"A {surface.Size}² grid cannot be meshed by view: the hierarchy needs 2ⁿ + 1 samples a side, at least 9.", nameof(surface));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        int size = surface.Size;
        int half = (size - 1) / 2;
        int root = (size - 1) * Ratio2 * Sub;
        int centre = root / 2;
        double cell = surface.CellSize;
        double subUnit = cell / Sub;
        double rootOrigin = -0.5 * root * subUnit;
        ulong terrainSeed = SeedDerivation.Derive(seed, SeedDerivation.TerrainBase);

        // The three grids' squares and lattices, in sub-units.
        var g0 = new Grid(Sub, centre - half * Sub, size);
        var g1 = new Grid(Ratio1 * Sub, centre - half * Ratio1 * Sub, size);
        var g2 = new Grid(Ratio2 * Sub, 0, size);

        // The data. g2 holds the base terrain's coarse octaves, g1 finer ones blending into g2's surface over its outer cells,
        // and g0 the world grid blending into g1's surface.
        float[] d2 = SampleBase(g2, profile, terrainSeed, profile.OctavesFor(Ratio2 * cell), rootOrigin, subUnit);
        float[] d1 = SampleBase(g1, profile, terrainSeed, profile.OctavesFor(Ratio1 * cell), rootOrigin, subUnit);
        BlendInto(d1, g1, d2, g2);
        float[] d0 = surface.Heights.ToArray();
        BlendInto(d0, g0, d1, g1);
        var data = new[] { (g0, d0), (g1, d1), (g2, d2) };

        // The stores: g0's lattice over its square and a margin, g1's likewise (inside g1's or the root's square), the root.
        int m0 = Math.Min(Margin0Cells * Sub, g0.Low - g1.Low);
        int m1 = Math.Min(Margin1Steps * g1.Step, g1.Low);
        var stores = new[]
        {
            new Store(g0.Step, g0.Low - m0, g0.High + m0),
            new Store(g1.Step, g1.Low - m1, g1.High + m1),
            new Store(g2.Step, 0, root),
        };

        // One slope bound for the whole hierarchy (one per region would not nest: a vertex in a gentle region can have a child in
        // a steep one). Below the grid g0's surface is Catmull–Rom, which overshoots: its gradient reaches 1.5 × 1.25 = 1.875 times
        // the steepest step between samples (the spline's own derivative times the sum of the other axis's weights); elsewhere the
        // surface is linear between samples, bound with a margin.
        double slope = Math.Max(CatmullRomSlope * SlopeOf(d0, size, cell),
            1.25 * Math.Max(SlopeOf(d1, size, Ratio1 * cell), SlopeOf(d2, size, Ratio2 * cell)));
        int rootLog = BitOperations.Log2((uint)root);
        int levels = 2 * rootLog;
        (float[] analyticError, float[] analyticRadius) = AnalyticBounds(levels, rootLog, subUnit, detail, slope);

        var builder = new Builder(stores, data, analyticError, analyticRadius, root, subUnit, detail);
        builder.Heights(rootLog);
        builder.Saturate(rootLog);

        float min = Math.Min(Math.Min(d0.Min(), d1.Min()), d2.Min());
        float max = Math.Max(Math.Max(d0.Max(), d1.Max()), d2.Max());
        NormalMap[] maps =
        [
            new NormalMap(d0, size, cell, rootOrigin + g0.Low * subUnit, rootOrigin + g0.Low * subUnit),
            new NormalMap(d1, size, Ratio1 * cell, rootOrigin + g1.Low * subUnit, rootOrigin + g1.Low * subUnit),
        ];
        return new TerrainHierarchy(surface, detail, stores, analyticError, analyticRadius, slope, min, max, maps, stopwatch.Elapsed.TotalSeconds);
    }

    // ── Levels ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The level of vertex (<paramref name="i"/>, <paramref name="j"/>): <c>t</c>, the trailing zeros of <c>i | j</c>, and whether it
    /// is a square's centre (both odd multiples of <c>2^t</c>) rather than an edge's midpoint. Not for the root's corners.
    /// </summary>
    internal static (int T, bool Diagonal) LevelOf(int i, int j)
    {
        int t = BitOperations.TrailingZeroCount(i | j);
        return (t, ((i >> t) & (j >> t) & 1) == 1);
    }

    /// <summary>The level index of (<paramref name="t"/>, <paramref name="diagonal"/>): 0 for the root's centre, rising as levels get finer.</summary>
    internal int LevelIndex(int t, bool diagonal) => 2 * (RootLog - 1 - t) + (diagonal ? 0 : 1);

    /// <summary>
    /// The ends of the edge vertex (<paramref name="i"/>, <paramref name="j"/>) splits, at level <paramref name="t"/>: a square's
    /// diagonal (the one joining its two older corners) or an edge of length <c>2^(t+1)</c>.
    /// </summary>
    internal static (int I0, int J0, int I1, int J1) SplitEdge(int i, int j, int t, bool diagonal)
    {
        int s = 1 << t;
        if (diagonal)
        {
            bool main = ((((i - s) >> (t + 1)) + ((j - s) >> (t + 1))) & 1) == 0;
            return main ? (i - s, j - s, i + s, j + s) : (i - s, j + s, i + s, j - s);
        }
        return ((i >> t) & 1) == 1 ? (i - s, j, i + s, j) : (i, j - s, i, j + s);
    }

    /// <summary>The children of vertex (<paramref name="i"/>, <paramref name="j"/>) at level <paramref name="t"/>; none below sub-unit 1.</summary>
    internal static int Children(int i, int j, int t, bool diagonal, Span<(int I, int J)> children)
    {
        int s = 1 << t;
        if (diagonal)
        {
            children[0] = (i - s, j);
            children[1] = (i + s, j);
            children[2] = (i, j - s);
            children[3] = (i, j + s);
            return 4;
        }
        if (t == 0)
            return 0;
        int h = s >> 1;
        children[0] = (i - h, j - h);
        children[1] = (i + h, j - h);
        children[2] = (i - h, j + h);
        children[3] = (i + h, j + h);
        return 4;
    }

    // ── Lookups (for the mesher) ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The stored height, saturated error and radius of vertex (<paramref name="i"/>, <paramref name="j"/>), when a store holds it:
    /// the finest store whose bounds contain it and whose lattice it lies on.
    /// </summary>
    internal bool TryStored(int i, int j, out float height, out float error, out float radius)
    {
        foreach (Store store in _stores)
        {
            if (store.Index(i, j) is int k and >= 0)
            {
                height = store.Height[k];
                error = store.Error[k];
                radius = store.Radius[k];
                return true;
            }
        }
        (height, error, radius) = (0f, 0f, 0f);
        return false;
    }

    /// <summary>The analytic error of level <paramref name="level"/> (the detail's bound), m.</summary>
    internal float AnalyticError(int level) => _analyticError[level];

    /// <summary>The length of the edge a vertex of level <paramref name="level"/> splits, m.</summary>
    internal double EdgeLength(int level)
    {
        int t = RootLog - 1 - level / 2;
        double s = (1 << t) * SubUnit;
        return level % 2 == 0 ? 2.0 * s * Math.Sqrt(2.0) : 2.0 * s;
    }

    /// <summary>
    /// The base height of a vertex finer than the world grid: on the smooth surface through g0's samples (Catmull–Rom, C1, see
    /// <see cref="SubGridHeight(Store, int, int, float)"/>), or <paramref name="midpoint"/>, the mean of its edge's ends, where
    /// store 0 cannot give it the samples around it (beyond g0's square and its margin).
    /// </summary>
    internal float SubGridHeight(int i, int j, float midpoint) => SubGridHeight(_stores[0], i, j, midpoint);

    /// <summary>The analytic radius of level <paramref name="level"/>, m.</summary>
    internal float AnalyticRadius(int level) => _analyticRadius[level];

    /// <summary>Store 1's and store 2's indices of (i, j), or −1 (the mesher's dense vertex maps).</summary>
    internal int StoreIndex(int store, int i, int j) => _stores[store].Index(i, j);

    /// <summary>Points in store <paramref name="store"/>.</summary>
    internal int StorePoints(int store) => _stores[store].Count * _stores[store].Count;

    /// <summary>
    /// The base height (without the detail) of any vertex of the hierarchy, m: stored, or the mean of its edge's ends. For tests
    /// and checks; the mesher carries heights down its triangles instead.
    /// </summary>
    internal float BaseHeight(int i, int j)
    {
        if (TryStored(i, j, out float height, out _, out _))
            return height;
        (int t, bool diagonal) = LevelOf(i, j);
        (int i0, int j0, int i1, int j1) = SplitEdge(i, j, t, diagonal);
        float midpoint = (BaseHeight(i0, j0) + BaseHeight(i1, j1)) * 0.5f;
        return t < SubLevels ? SubGridHeight(i, j, midpoint) : midpoint;
    }

    /// <summary>
    /// The smooth surface below the world grid: the bicubic Catmull–Rom interpolation of store 0's 4×4 samples around (i, j). It
    /// passes through every sample and is C1, so the mesh below the grid is smooth where the grid's own 4-8 triangles are flat
    /// (refined through them with mesh-derived normals they shaded as flat 8 m facets). <paramref name="midpoint"/> where the
    /// samples around are not all in the store.
    /// </summary>
    private static float SubGridHeight(Store store, int i, int j, float midpoint)
    {
        int di = i - store.Low, dj = j - store.Low;
        if (di < 0 || dj < 0)
            return midpoint;
        int ci = di >> SubLevels, cj = dj >> SubLevels;
        if (ci < 1 || cj < 1 || ci + 2 >= store.Count || cj + 2 >= store.Count)
            return midpoint;
        double u = (di & (Sub - 1)) * (1.0 / Sub), v = (dj & (Sub - 1)) * (1.0 / Sub);
        Span<double> rows = stackalloc double[4];
        for (int r = 0; r < 4; r++)
        {
            int k = (cj - 1 + r) * store.Count + ci - 1;
            rows[r] = CatmullRom(store.Height[k], store.Height[k + 1], store.Height[k + 2], store.Height[k + 3], u);
        }
        return (float)CatmullRom(rows[0], rows[1], rows[2], rows[3], v);
    }

    /// <summary>The Catmull–Rom spline through p1 (t = 0) and p2 (t = 1).</summary>
    private static double CatmullRom(double p0, double p1, double p2, double p3, double t) =>
        p1 + 0.5 * t * (p2 - p0 + t * (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3 + t * (3.0 * (p1 - p2) + p3 - p0)));

    /// <summary>World x of sub-unit coordinate <paramref name="i"/>, m.</summary>
    internal double WorldOf(int i) => RootOrigin + i * SubUnit;

    /// <summary>
    /// The map normal's x and z at world <c>(x, z)</c>: from the finest of <see cref="NormalMaps"/> that holds the point, else the
    /// coarsest's edge (the shader's <c>MapNormal</c> chooses alike).
    /// </summary>
    public (float X, float Z) MapNormal(double x, double z)
    {
        NormalMap map = NormalMaps.FirstOrDefault(m => m.Contains(x, z)) ?? NormalMaps[^1];
        return map.Sample(x, z);
    }

    // ── Build helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A grid: its lattice step (a power of two) and first sample in sub-units, and its samples a side.</summary>
    private readonly record struct Grid(int Step, int Low, int Size)
    {
        public int High => Low + Step * (Size - 1);

        public bool Contains(int i, int j) => i >= Low && i <= High && j >= Low && j <= High;

        public bool OnLattice(int i, int j) => ((i - Low) & (Step - 1)) == 0 && ((j - Low) & (Step - 1)) == 0;

        public int IndexOf(int i, int j)
        {
            int shift = BitOperations.TrailingZeroCount(Step);
            return ((j - Low) >> shift) * Size + ((i - Low) >> shift);
        }
    }

    /// <summary>Heights, saturated errors and radii on one lattice (a power-of-two step) over a square.</summary>
    private sealed class Store
    {
        private readonly int _shift;
        private readonly int _mask;

        public Store(int step, int low, int high)
        {
            Step = step;
            Low = low;
            Count = (high - low) / step + 1;
            _shift = BitOperations.TrailingZeroCount(step);
            _mask = step - 1;
            Height = new float[Count * Count];
            Error = new float[Count * Count];
            Radius = new float[Count * Count];
        }

        public int Step { get; }

        public int Low { get; }

        public int Count { get; }

        public int High => Low + Step * (Count - 1);

        public float[] Height { get; }

        public float[] Error { get; }

        public float[] Radius { get; }

        /// <summary>The index of (i, j), or −1 when it is outside the bounds or off the lattice.</summary>
        public int Index(int i, int j)
        {
            int di = i - Low, dj = j - Low;
            if (((di | dj) & _mask) != 0 || di < 0 || dj < 0)
                return -1;
            di >>= _shift;
            dj >>= _shift;
            return di < Count && dj < Count ? dj * Count + di : -1;
        }

        public bool Contains(int i, int j) => i >= Low && i <= High && j >= Low && j <= High;
    }

    private static float[] SampleBase(Grid grid, TerrainProfile profile, ulong seed, int octaves, double rootOrigin, double subUnit)
    {
        var heights = new float[grid.Size * grid.Size];
        Parallel.For(0, grid.Size, j =>
        {
            double z = rootOrigin + (grid.Low + j * grid.Step) * subUnit;
            for (int i = 0; i < grid.Size; i++)
                heights[j * grid.Size + i] = (float)profile.Height(seed, rootOrigin + (grid.Low + i * grid.Step) * subUnit, z, octaves);
        });
        return heights;
    }

    /// <summary>
    /// Blends <paramref name="inner"/> over its outer <see cref="BlendCells"/> cells (an eighth of the grid on a small one) into the
    /// surface of <paramref name="outer"/> (linear on its 4-8 triangles), with a smoothstep weight on the distance to the edge: at
    /// the edge it is the outer surface.
    /// </summary>
    private static void BlendInto(float[] inner, Grid innerGrid, float[] outer, Grid outerGrid)
    {
        int n = innerGrid.Size;
        int band = Math.Min(BlendCells, (n - 1) / 8);
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                int edge = Math.Min(Math.Min(i, n - 1 - i), Math.Min(j, n - 1 - j));
                if (edge >= band)
                    continue;
                double x = (double)edge / band;
                double w = x * x * (3.0 - 2.0 * x);
                double surface = Linear(outer, outerGrid, innerGrid.Low + i * innerGrid.Step, innerGrid.Low + j * innerGrid.Step);
                int k = j * n + i;
                inner[k] = (float)(w * inner[k] + (1.0 - w) * surface);
            }
        });
    }

    /// <summary>The surface of <paramref name="grid"/>'s samples at (<paramref name="i"/>, <paramref name="j"/>): linear on its finest 4-8 triangles.</summary>
    private static double Linear(float[] heights, Grid grid, int i, int j)
    {
        int step = grid.Step;
        int ci = Math.Min((i - grid.Low) / step, grid.Size - 2);
        int cj = Math.Min((j - grid.Low) / step, grid.Size - 2);
        int a = grid.Low + ci * step, b = grid.Low + cj * step;
        double u = (double)(i - a) / step, v = (double)(j - b) / step;
        double h00 = heights[cj * grid.Size + ci], h10 = heights[cj * grid.Size + ci + 1];
        double h01 = heights[(cj + 1) * grid.Size + ci], h11 = heights[(cj + 1) * grid.Size + ci + 1];
        // The cell's diagonal by the parity rule (with 2s = step): main joins (a, b)–(a + step, b + step).
        bool main = ((a / step + b / step) & 1) == 0;
        if (main)
            return u >= v ? h00 + u * (h10 - h00) + v * (h11 - h10) : h00 + v * (h01 - h00) + u * (h11 - h01);
        return u + v <= 1.0 ? h00 + u * (h10 - h00) + v * (h01 - h00) : h11 + (1.0 - u) * (h01 - h11) + (1.0 - v) * (h10 - h11);
    }

    /// <summary>The steepest rise over run between neighbouring samples, times √2: a bound on any of the grid's triangles' slopes.</summary>
    private static double SlopeOf(float[] heights, int size, double spacing)
    {
        double worst = 0.0;
        object gate = new();
        Parallel.For(0, size, () => 0.0, (j, _, local) =>
        {
            for (int i = 0; i < size; i++)
            {
                float h = heights[j * size + i];
                if (i + 1 < size)
                    local = Math.Max(local, Math.Abs(heights[j * size + i + 1] - h));
                if (j + 1 < size)
                    local = Math.Max(local, Math.Abs(heights[(j + 1) * size + i] - h));
            }
            return local;
        }, local =>
        {
            lock (gate)
                worst = Math.Max(worst, local);
        });
        return worst / spacing * Math.Sqrt(2.0);
    }

    /// <summary>
    /// The analytic error and radius of every level: the detail's midpoint bound for the level's edge, and a radius that covers
    /// every descendant — each child's offset over the steepest slope plus the child's own radius, down to the finest level, whose
    /// radius is the detail's amplitude.
    /// </summary>
    private static (float[] Error, float[] Radius) AnalyticBounds(int levels, int rootLog, double subUnit, TerrainDetail detail, double slope)
    {
        var error = new float[levels];
        var radius = new float[levels];
        double rise = Math.Sqrt(1.0 + slope * slope);
        double below = detail.MaxAmplitude;
        for (int level = levels - 1; level >= 0; level--)
        {
            int t = rootLog - 1 - level / 2;
            bool diagonal = level % 2 == 0;
            double s = (1 << t) * subUnit;
            double edge = diagonal ? 2.0 * s * Math.Sqrt(2.0) : 2.0 * s;
            error[level] = RoundUp(detail.MidpointBound(edge));
            // A diagonal vertex's children sit s away along an axis; an axis vertex's s/√2 away on the diagonals (none at t = 0).
            double offset = s;
            if (!diagonal)
                offset = t == 0 ? 0.0 : s / Math.Sqrt(2.0);
            double r = level == levels - 1 ? detail.MaxAmplitude : offset * rise + below;
            radius[level] = RoundUp(Math.Max(r, detail.MaxAmplitude));
            below = radius[level];
        }
        return (error, radius);
    }

    /// <summary>The smallest float not below <paramref name="value"/>, so a bound never shrinks on the way to float.</summary>
    internal static float RoundUp(double value)
    {
        float f = (float)value;
        return f < value ? MathF.BitIncrement(f) : f;
    }

    /// <summary>The two passes over the stores: heights from the root down, errors and radii from the finest level up.</summary>
    private sealed class Builder
    {
        private readonly Store[] _stores;
        private readonly (Grid Grid, float[] Heights)[] _data;
        private readonly float[] _analyticError;
        private readonly float[] _analyticRadius;
        private readonly int _root;
        private readonly double _subUnit;
        private readonly TerrainDetail _detail;
        private readonly int _rootLog;

        public Builder(Store[] stores, (Grid, float[])[] data, float[] analyticError, float[] analyticRadius, int root, double subUnit,
            TerrainDetail detail)
        {
            _stores = stores;
            _data = data;
            _analyticError = analyticError;
            _analyticRadius = analyticRadius;
            _root = root;
            _subUnit = subUnit;
            _detail = detail;
            _rootLog = BitOperations.Log2((uint)root);
        }

        /// <summary>The root's corners, then every level from the coarsest down: a sample's height, or its edge's mean.</summary>
        public void Heights(int rootLog)
        {
            foreach ((int i, int j) in new[] { (0, 0), (_root, 0), (0, _root), (_root, _root) })
            {
                Store store = OwnerOf(i, j, out int k);
                store.Height[k] = DataAt(i, j) ?? throw new InvalidOperationException("The root's corners are samples of the far grid.");
            }
            for (int level = 0; level < 2 * rootLog; level++)
                ForLevel(level, (store, k, i, j, t, diagonal) =>
                {
                    float? sample = DataAt(i, j);
                    if (sample is { } h)
                    {
                        store.Height[k] = h;
                        return;
                    }
                    (int i0, int j0, int i1, int j1) = SplitEdge(i, j, t, diagonal);
                    store.Height[k] = (HeightAnywhere(i0, j0) + HeightAnywhere(i1, j1)) * 0.5f;
                });
        }

        /// <summary>Every level from the finest up: own error plus the detail's bound, then the children's, saturated.</summary>
        public void Saturate(int rootLog)
        {
            for (int level = 2 * rootLog - 1; level >= 0; level--)
            {
                int levelIndex = level;
                ForLevel(level, (store, k, i, j, t, diagonal) =>
                {
                    (int i0, int j0, int i1, int j1) = SplitEdge(i, j, t, diagonal);
                    float h = store.Height[k];
                    double own = Math.Abs(h - (HeightAnywhere(i0, j0) + HeightAnywhere(i1, j1)) * 0.5f);
                    double error = own + _analyticError[levelIndex];
                    double radius = _detail.MaxAmplitude;
                    Span<(int I, int J)> children = stackalloc (int, int)[4];
                    int count = Children(i, j, t, diagonal, children);
                    for (int c = 0; c < count; c++)
                    {
                        (int ci, int cj) = children[c];
                        if (ci < 0 || cj < 0 || ci > _root || cj > _root)
                            continue;
                        (float ch, float ce, float cr) = Saturated(ci, cj, levelIndex + 1);
                        double dx = (ci - i) * _subUnit, dz = (cj - j) * _subUnit, dy = ch - h;
                        error = Math.Max(error, ce);
                        radius = Math.Max(radius, Math.Sqrt(dx * dx + dy * dy + dz * dz) + cr);
                    }
                    store.Error[k] = RoundUp(error);
                    store.Radius[k] = RoundUp(radius);
                });
            }
            // The root's corners split nothing; they hold the largest error, so nothing above them is ever smaller.
        }

        /// <summary>Runs <paramref name="visit"/> over every stored point of <paramref name="level"/> that no finer store holds.</summary>
        private void ForLevel(int level, Action<Store, int, int, int, int, bool> visit)
        {
            int t = _rootLog - 1 - level / 2;
            bool diagonal = level % 2 == 0;
            int s = 1 << t;
            for (int index = 0; index < _stores.Length; index++)
            {
                Store store = _stores[index];
                if (s < store.Step)
                    continue; // this level is finer than the store's lattice
                int storeIndex = index;
                // Points of the level: (odd, odd) multiples of s for a diagonal level; (odd, even) and (even, odd) for an axis one.
                int first = FirstMultiple(store.Low, s, odd: true);
                int count = first > store.High ? 0 : (store.High - first) / (2 * s) + 1;
                Parallel.For(0, count, row =>
                {
                    int jOdd = first + row * 2 * s;
                    if (diagonal)
                    {
                        Row(store, storeIndex, jOdd, s, oddI: true, t, true, visit);
                    }
                    else
                    {
                        Row(store, storeIndex, jOdd, s, oddI: false, t, false, visit); // (even, odd)
                    }
                });
                if (!diagonal)
                {
                    // (odd, even): rows at even multiples of s.
                    int firstEven = FirstMultiple(store.Low, s, odd: false);
                    int evenCount = firstEven > store.High ? 0 : (store.High - firstEven) / (2 * s) + 1;
                    Parallel.For(0, evenCount, row => Row(store, storeIndex, firstEven + row * 2 * s, s, oddI: true, t, false, visit));
                }
            }
        }

        private void Row(Store store, int storeIndex, int j, int s, bool oddI, int t, bool diagonal, Action<Store, int, int, int, int, bool> visit)
        {
            int first = FirstMultiple(store.Low, s, oddI);
            for (int i = first; i <= store.High; i += 2 * s)
            {
                if (ShadowedByFinerStore(storeIndex, i, j))
                    continue;
                int k = store.Index(i, j);
                if (k >= 0)
                    visit(store, k, i, j, t, diagonal);
            }
        }

        /// <summary>The first odd (or even, zero included) multiple of <paramref name="s"/> at or after <paramref name="low"/>.</summary>
        private static int FirstMultiple(int low, int s, bool odd)
        {
            int m = (low + s - 1) / s; // first multiple index at or after low (low ≥ 0)
            if (((m & 1) == 1) != odd)
                m++;
            return m * s;
        }

        private bool ShadowedByFinerStore(int storeIndex, int i, int j)
        {
            for (int k = 0; k < storeIndex; k++)
            {
                if (_stores[k].Contains(i, j))
                    return true;
            }
            return false;
        }

        /// <summary>The store that holds (i, j) and its index there: the finest whose bounds contain it.</summary>
        private Store OwnerOf(int i, int j, out int k)
        {
            foreach (Store store in _stores)
            {
                k = store.Index(i, j);
                if (k >= 0)
                    return store;
            }
            throw new InvalidOperationException($"No store holds ({i}, {j}).");
        }

        /// <summary>The sample at (i, j) of the finest grid whose square holds it and whose lattice it lies on; null between samples.</summary>
        private float? DataAt(int i, int j)
        {
            foreach ((Grid grid, float[] heights) in _data)
            {
                if (grid.Contains(i, j))
                {
                    if (grid.OnLattice(i, j))
                        return heights[grid.IndexOf(i, j)];
                    return null; // inside a finer grid's square, between its samples: never a coarser grid's sample
                }
            }
            return null;
        }

        /// <summary>The base height of any vertex whose coarser levels are done: stored, a sample, or its edge's mean.</summary>
        private float HeightAnywhere(int i, int j)
        {
            foreach (Store store in _stores)
            {
                int k = store.Index(i, j);
                if (k >= 0)
                    return store.Height[k];
            }
            if (DataAt(i, j) is { } h)
                return h;
            (int t, bool diagonal) = LevelOf(i, j);
            (int i0, int j0, int i1, int j1) = SplitEdge(i, j, t, diagonal);
            float midpoint = (HeightAnywhere(i0, j0) + HeightAnywhere(i1, j1)) * 0.5f;
            return t < SubLevels ? SubGridHeight(_stores[0], i, j, midpoint) : midpoint;
        }

        /// <summary>Height, saturated error and radius of a child: stored, or analytic at its level.</summary>
        private (float Height, float Error, float Radius) Saturated(int i, int j, int level)
        {
            foreach (Store store in _stores)
            {
                int k = store.Index(i, j);
                if (k >= 0)
                    return (store.Height[k], store.Error[k], store.Radius[k]);
            }
            return (HeightAnywhere(i, j), _analyticError[level], _analyticRadius[level]);
        }
    }
}
