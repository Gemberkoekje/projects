using Vista.Scene;

namespace Vista.World;

/// <summary>
/// The far field's two grids as height fields (vista-plan §10 M1.10): g1 (32 m over its square) and g2 (256 m over the root),
/// each sample the surface the view mesh's far field refines — the height its finest store holds, so g1's samples on the world
/// grid's edge are the world grid's and g2's inside g1's square are the blended surface there. Between samples the surface is
/// linear on each grid's 4-8 triangles (a square split along the diagonal that joins its corners of even <c>i + j</c>), as the
/// mesh's is. Built once per hierarchy; the GPU holds both grids as floats.
/// </summary>
public sealed class HeightFieldGrids
{
    private HeightFieldGrids(float[] level1, float[] level2, int size, double origin1, double cell1, double origin2, double cell2, double seconds)
    {
        Level1 = level1;
        Level2 = level2;
        Size = size;
        Origin1 = origin1;
        Cell1 = cell1;
        Origin2 = origin2;
        Cell2 = cell2;
        BuildSeconds = seconds;
    }

    /// <summary>g1's heights, row-major, <see cref="Size"/>² samples <see cref="Cell1"/> apart from <c>(Origin1, Origin1)</c>.</summary>
    public float[] Level1 { get; }

    /// <summary>g2's heights, row-major, <see cref="Size"/>² samples <see cref="Cell2"/> apart from <c>(Origin2, Origin2)</c>.</summary>
    public float[] Level2 { get; }

    /// <summary>Samples a side (both grids).</summary>
    public int Size { get; }

    /// <summary>World x (and z) of g1's sample 0, m.</summary>
    public double Origin1 { get; }

    /// <summary>g1's spacing, m.</summary>
    public double Cell1 { get; }

    /// <summary>World x (and z) of g2's sample 0, m.</summary>
    public double Origin2 { get; }

    /// <summary>g2's spacing, m.</summary>
    public double Cell2 { get; }

    /// <summary>Wall time of the build, s.</summary>
    public double BuildSeconds { get; }

    /// <summary>Upload size of both grids, bytes.</summary>
    public long ByteSize => ((long)Level1.Length + Level2.Length) * sizeof(float);

    /// <summary>The heights of <paramref name="level"/> (0: g1, 1: g2).</summary>
    public float[] Heights(int level) => level == 0 ? Level1 : Level2;

    /// <summary>The spacing of <paramref name="level"/>, m.</summary>
    public double Cell(int level) => level == 0 ? Cell1 : Cell2;

    /// <summary>World x (and z) of <paramref name="level"/>'s sample 0, m.</summary>
    public double Origin(int level) => level == 0 ? Origin1 : Origin2;

    internal static HeightFieldGrids Build(TerrainHierarchy hierarchy)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int size = hierarchy.Surface.Size;
        int step1 = TerrainHierarchy.Ratio1 * TerrainHierarchy.Sub, low1 = hierarchy.Grid1Low;
        int step2 = TerrainHierarchy.Ratio2 * TerrainHierarchy.Sub;
        float[] level1 = Sample(hierarchy, size, low1, step1);
        float[] level2 = Sample(hierarchy, size, 0, step2);
        return new HeightFieldGrids(level1, level2, size, hierarchy.WorldOf(low1), step1 * hierarchy.SubUnit, hierarchy.WorldOf(0),
            step2 * hierarchy.SubUnit, stopwatch.Elapsed.TotalSeconds);
    }

    private static float[] Sample(TerrainHierarchy hierarchy, int size, int low, int step)
    {
        var heights = new float[size * size];
        Parallel.For(0, size, j =>
        {
            for (int i = 0; i < size; i++)
            {
                if (!hierarchy.TryStored(low + i * step, low + j * step, out float height, out _, out _))
                    throw new InvalidOperationException($"No store holds the far grid's sample ({i}, {j}).");
                heights[j * size + i] = height;
            }
        });
        return heights;
    }
}

/// <summary>
/// The far field of one view as height-field tiles (vista-plan §10 M1.10), the alternative to meshing it that M1.10 measures:
/// square tiles of <see cref="TileCells"/>² cells of g1 outside the world grid's square and of g2 outside g1's, out to the view's
/// horizon reach, each an axis-aligned box in the camera's <see cref="EarthFrame"/> around the tile's cells bent onto the Earth
/// (the procedural primitives of an acceleration structure) and a local frame at its centre — origin on the ground sphere, U and
/// V along the world's x and z there, W its vertical — in which the shader clips a ray to the tile and reads its height. The frame's
/// regular grid is off the bent samples by at most <see cref="GridMismatch"/> of a cell, so the clip has a <see cref="Margin"/> and
/// the height test a pad; the shader walks the cells on the world's own grid and bends their triangles exactly, as the mesh's
/// vertices are.
/// </summary>
public sealed class FarHeightField
{
    /// <summary>The default tile: 8² cells (256 m of g1, 2 km of g2), the fastest of 4², 8², 16² and 32² short of the smallest (§10 M1.10).</summary>
    public const int DefaultTileCells = 8;

    /// <summary>Floats per tile in <see cref="Tiles"/>: (O, packed level and first sample), (U, lowest height), (W, highest height).</summary>
    public const int TileStride = 12;

    /// <summary>Floats per tile in <see cref="Aabbs"/>: the box's minimum, then its maximum.</summary>
    public const int AabbStride = 6;

    /// <summary>The boxes are this much larger than the samples they were bent from, m, plus <see cref="BoxPadPerMetre"/> of a tile's width.</summary>
    private const double BoxPadMetres = 1.0;

    private const double BoxPadPerMetre = 1e-3;

    /// <summary>No height field: the far field is meshed, or there is none.</summary>
    public static FarHeightField None { get; } = new(null, 0, [], [], 0, 0, 0.0, 0.0, 0.0, 0.0);

    private readonly HeightFieldGrids? _grids;

    private FarHeightField(HeightFieldGrids? grids, int tileCells, float[] tiles, float[] aabbs, int level1Tiles, int level2Tiles, double pad1, double pad2,
        double mismatch, double seconds)
    {
        _grids = grids;
        TileCells = tileCells;
        Tiles = tiles;
        Aabbs = aabbs;
        Level1Tiles = level1Tiles;
        Level2Tiles = level2Tiles;
        Pad1 = pad1;
        Pad2 = pad2;
        GridMismatch = mismatch;
        BuildSeconds = seconds;
    }

    /// <summary>Whether there are tiles to trace.</summary>
    public bool Any => TileCount > 0;

    /// <summary>The grids the tiles index (none for <see cref="None"/>).</summary>
    public HeightFieldGrids Grids => _grids ?? throw new InvalidOperationException("This view has no height field.");

    /// <summary>Cells a side of a tile.</summary>
    public int TileCells { get; }

    /// <summary>
    /// Per tile, <see cref="TileStride"/> floats: the local frame's origin and the tile's packed level and first sample, U and the
    /// tile's lowest height, W and its highest.
    /// </summary>
    public float[] Tiles { get; }

    /// <summary>Per tile, <see cref="AabbStride"/> floats: the box in the frame (what the acceleration structure is built from).</summary>
    public float[] Aabbs { get; }

    /// <summary>Tiles in all.</summary>
    public int TileCount => Level1Tiles + Level2Tiles;

    /// <summary>Tiles of g1 (they come first).</summary>
    public int Level1Tiles { get; }

    /// <summary>Tiles of g2.</summary>
    public int Level2Tiles { get; }

    /// <summary>
    /// How far the cells' triangles can lie below or above their heights in a g1 tile's local frame, m: the ground sphere's drop
    /// across half a tile, a metre, and the margin's worth of a 45° slope. The shader's height test pads by it (and by the margin's
    /// worth of the ray's own slope).
    /// </summary>
    public double Pad1 { get; }

    /// <summary>As <see cref="Pad1"/>, for g2's tiles.</summary>
    public double Pad2 { get; }

    /// <summary>
    /// The worst distance between a bent sample and the regular grid of its tile's local frame, as a fraction of a cell, over every
    /// tile's corners, edge midpoints and centre at its lowest and highest height.
    /// </summary>
    public double GridMismatch { get; }

    /// <summary>How far past its square the shader's clip lets a ray into a tile, in cells: twice the mismatch, at least 1%.</summary>
    public double Margin => MarginFor(GridMismatch);

    /// <summary>
    /// The triangle test's tolerance in barycentric terms, for float rounding alone: the walk visits the cells the ray crosses, so
    /// no hit outside a triangle needs to count.
    /// </summary>
    public double Epsilon => 1e-3;

    /// <summary>Wall time of <see cref="Build"/>, s.</summary>
    public double BuildSeconds { get; }

    /// <summary>Upload size of the tiles and their boxes, bytes (the grids are the world's, counted apart).</summary>
    public long ByteSize => ((long)Tiles.Length + Aabbs.Length) * sizeof(float);

    /// <summary>Triangles the tiles stand for (two per cell).</summary>
    public long Triangles => 2L * TileCells * TileCells * TileCount;

    /// <summary>
    /// The tiles of <paramref name="grids"/> a camera at world <c>(cameraX, cameraZ)</c> sees within <paramref name="reach"/> metres,
    /// in <paramref name="frame"/>, <paramref name="tileCells"/>² cells each (a power of two that divides the inner squares' edges).
    /// </summary>
    public static FarHeightField Build(HeightFieldGrids grids, EarthFrame frame, double cameraX, double cameraZ, double reach, int tileCells)
    {
        ArgumentNullException.ThrowIfNull(grids);
        int cells = grids.Size - 1;
        if (tileCells < 2 || !System.Numerics.BitOperations.IsPow2(tileCells) || (cells * 7 / 16) % tileCells != 0 || (cells * 3 / 8) % tileCells != 0)
            throw new ArgumentException($"{tileCells}² cells do not tile a {grids.Size}² grid around its inner square.", nameof(tileCells));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int perSide = cells / tileCells;
        var tiles = new List<float>[2];
        var aabbs = new List<float>[2];
        var counts = new int[2];
        var mismatch = new double[2];
        for (int level = 0; level < 2; level++)
        {
            // The finer grid's square, in this grid's tiles: the world grid's within g1 (a quarter of it), g1's within g2 (an eighth).
            int innerLow = (level == 0 ? cells * 3 / 8 : cells * 7 / 16) / tileCells;
            int innerHigh = (level == 0 ? cells * 5 / 8 : cells * 9 / 16) / tileCells;
            var rows = new (List<float> Tiles, List<float> Aabbs, double Mismatch)[perSide];
            int lvl = level;
            Parallel.For(0, perSide, tj =>
            {
                var rowTiles = new List<float>();
                var rowAabbs = new List<float>();
                double rowMismatch = 0.0;
                for (int ti = 0; ti < perSide; ti++)
                {
                    if (ti >= innerLow && ti < innerHigh && tj >= innerLow && tj < innerHigh)
                        continue;
                    rowMismatch = Math.Max(rowMismatch, AddTile(grids, lvl, ti, tj, tileCells, frame, cameraX, cameraZ, reach, rowTiles, rowAabbs));
                }
                rows[tj] = (rowTiles, rowAabbs, rowMismatch);
            });
            tiles[level] = [.. rows.SelectMany(r => r.Tiles)];
            aabbs[level] = [.. rows.SelectMany(r => r.Aabbs)];
            counts[level] = tiles[level].Count / TileStride;
            mismatch[level] = rows.Max(r => r.Mismatch);
        }

        double worst = Math.Max(mismatch[0], mismatch[1]);
        double margin = MarginFor(worst);
        return new FarHeightField(grids, tileCells, [.. tiles[0], .. tiles[1]], [.. aabbs[0], .. aabbs[1]], counts[0], counts[1],
            Pad(grids.Cell1 * tileCells, frame.GroundRadius, margin * grids.Cell1), Pad(grids.Cell2 * tileCells, frame.GroundRadius, margin * grids.Cell2),
            worst, stopwatch.Elapsed.TotalSeconds);
    }

    private static double MarginFor(double mismatch) => Math.Max(0.01, 2.0 * mismatch);

    /// <summary>The height test's pad for a tile <paramref name="width"/> metres wide: the sphere's drop over its half-diagonal, a metre, and the margin's reach.</summary>
    private static double Pad(double width, double radius, double tolerance)
    {
        double halfDiagonal = 0.5 * width * Math.Sqrt(2.0);
        return halfDiagonal * halfDiagonal / (2.0 * radius) + 1.0 + tolerance;
    }

    /// <summary>Adds tile (<paramref name="ti"/>, <paramref name="tj"/>) of <paramref name="level"/> when it lies within the reach; returns its grid mismatch in cells.</summary>
    private static double AddTile(HeightFieldGrids grids, int level, int ti, int tj, int tileCells, EarthFrame frame, double cameraX, double cameraZ, double reach,
        List<float> tiles, List<float> aabbs)
    {
        double cell = grids.Cell(level), origin = grids.Origin(level);
        double width = tileCells * cell;
        double x0 = origin + ti * width, z0 = origin + tj * width;
        double dx = Math.Max(Math.Max(x0 - cameraX, cameraX - (x0 + width)), 0.0);
        double dz = Math.Max(Math.Max(z0 - cameraZ, cameraZ - (z0 + width)), 0.0);
        if (dx * dx + dz * dz > reach * reach)
            return 0.0;

        float[] heights = grids.Heights(level);
        int size = grids.Size;
        float low = float.MaxValue, high = float.MinValue;
        for (int b = 0; b <= tileCells; b++)
        {
            int row = (tj * tileCells + b) * size + ti * tileCells;
            for (int a = 0; a <= tileCells; a++)
            {
                float h = heights[row + a];
                low = Math.Min(low, h);
                high = Math.Max(high, h);
            }
        }

        // The box: the tile bent at its lowest and highest height, sampled at its corners, edge midpoints and centre, padded for the
        // sphere's bulge between them (under 0.1 m across half a 4 km tile) and the shader's float rounding.
        double xc = x0 + 0.5 * width, zc = z0 + 0.5 * width;
        Vector3D o = frame.ToFrame(xc, 0.0, zc);
        Vector3D w = frame.LocalUp(o);
        Vector3D along = frame.ToFrame(x0 + width, 0.0, zc) - frame.ToFrame(x0, 0.0, zc);
        Vector3D u = (along - w * Vector3D.Dot(along, w)).Normalized();
        Vector3D v = Vector3D.Cross(u, w);
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        double mismatch = 0.0;
        int half = tileCells / 2;
        foreach (int a in (ReadOnlySpan<int>)[0, half, tileCells])
        {
            foreach (int b in (ReadOnlySpan<int>)[0, half, tileCells])
            {
                foreach (float h in (ReadOnlySpan<float>)[low, high])
                {
                    Vector3D p = frame.ToFrame(x0 + a * cell, h, z0 + b * cell);
                    (minX, minY, minZ) = (Math.Min(minX, p.X), Math.Min(minY, p.Y), Math.Min(minZ, p.Z));
                    (maxX, maxY, maxZ) = (Math.Max(maxX, p.X), Math.Max(maxY, p.Y), Math.Max(maxZ, p.Z));
                    Vector3D r = p - o;
                    double du = Vector3D.Dot(r, u) - (a - half) * cell, dv = Vector3D.Dot(r, v) - (b - half) * cell;
                    mismatch = Math.Max(mismatch, Math.Max(Math.Abs(du), Math.Abs(dv)) / cell);
                }
            }
        }
        double pad = BoxPadMetres + BoxPadPerMetre * width;
        aabbs.AddRange([(float)(minX - pad), (float)(minY - pad), (float)(minZ - pad), (float)(maxX + pad), (float)(maxY + pad), (float)(maxZ + pad)]);

        uint packed = (uint)(ti * tileCells) | ((uint)(tj * tileCells) << 13) | ((uint)level << 26);
        tiles.AddRange([(float)o.X, (float)o.Y, (float)o.Z, BitConverter.UInt32BitsToSingle(packed),
            (float)u.X, (float)u.Y, (float)u.Z, low, (float)w.X, (float)w.Y, (float)w.Z, high]);
        return mismatch;
    }
}
