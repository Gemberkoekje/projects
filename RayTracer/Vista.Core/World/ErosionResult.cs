namespace Vista.World;

/// <summary>
/// An eroded world (vista-plan §4.3, §10 M1.7): the ground surface, and the maps later stages read — the loose material left
/// on the bedrock (scree under faces, sediment in valleys: the deposition map), the mean discharge (the flow map: rivers carry
/// the rain of everything upstream), the mean water depth (the wetness map) and the water standing at the end (where lakes
/// pool). Curvature is a function of the surface alone (<see cref="TerrainMaps.Curvature"/>). Since M1.11a it also holds the
/// surface's drainage (<see cref="DrainageMap"/>): where the rain on every cell goes, and how much land drains through it — the
/// flow accumulation that the materials and the rivers read, where the simulation's mean discharge only follows the water of
/// its last steps. Every map is row-major on the grid of <see cref="Surface"/>.
/// </summary>
public sealed class ErosionResult
{
    /// <summary>Wraps the maps without copying.</summary>
    public ErosionResult(HeightField surface, float[] loose, float[] discharge, float[] waterDepth, float[] standingWater, int steps, DrainageMap drainage)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(drainage);
        if (drainage.Size != surface.Size || drainage.CellSize != surface.CellSize)
            throw new ArgumentException("The drainage is not on the surface's grid.", nameof(drainage));
        int cells = surface.Size * surface.Size;
        foreach ((string name, float[] map) in new[] { (nameof(loose), loose), (nameof(discharge), discharge), (nameof(waterDepth), waterDepth), (nameof(standingWater), standingWater) })
        {
            ArgumentNullException.ThrowIfNull(map, name);
            if (map.Length != cells)
                throw new ArgumentException($"Expected {cells} values, got {map.Length}.", name);
        }
        Surface = surface;
        Loose = loose;
        Discharge = discharge;
        WaterDepth = waterDepth;
        StandingWater = standingWater;
        Steps = steps;
        Drainage = drainage;
    }

    /// <summary>The ground surface (bedrock plus loose material).</summary>
    public HeightField Surface { get; }

    /// <summary>Loose material on the bedrock per cell, in metres.</summary>
    public float[] Loose { get; }

    /// <summary>Mean outflow per cell over the averaging steps, in m³/s.</summary>
    public float[] Discharge { get; }

    /// <summary>Mean water depth per cell over the averaging steps, in metres.</summary>
    public float[] WaterDepth { get; }

    /// <summary>Water depth per cell after the last step, in metres.</summary>
    public float[] StandingWater { get; }

    /// <summary>Steps the erosion ran.</summary>
    public int Steps { get; }

    /// <summary>The surface's drainage: each cell's way down, its upstream area, and how deep its hollow would fill.</summary>
    public DrainageMap Drainage { get; }

    /// <summary>
    /// The result of <paramref name="state"/>: the sediment still suspended is set down where it is (the water dries out), the
    /// flow sums become means, and every map is rounded to half precision (<see cref="Half"/>), the precision the cache keeps
    /// them in, so a world fresh from the device and the same world read from the cache are the same bits. The surface stays
    /// full precision. The pools the water leaves fill with sediment up to <paramref name="poolFillMetres"/> deep
    /// (<see cref="ErosionSettings.PoolFillMetres"/>; 0 leaves them). The drainage is worked out from the surface
    /// (<see cref="DrainageMap.Of"/>, a few seconds for the world grid).
    /// </summary>
    public static ErosionResult From(ErosionState state, float poolFillMetres = 0f)
    {
        ArgumentNullException.ThrowIfNull(state);
        return From(state.Size, state.CellSize, state.Terrain, state.Water, state.Sediment, state.Flow, state.FlowSteps, state.Steps, poolFillMetres);
    }

    /// <summary>The result of a state given as its buffers (laid out as <see cref="ErosionState"/>'s), read back from a device.</summary>
    public static ErosionResult From(int size, double cellSize, float[] terrain, float[] water, float[] sediment, float[] flow, int flowSteps, int steps,
        float poolFillMetres = 0f)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(sediment);
        ArgumentNullException.ThrowIfNull(flow);
        int cells = size * size;
        if (terrain.Length < cells * 2 || water.Length < cells || sediment.Length < cells || flow.Length < cells * 2)
            throw new ArgumentException($"The buffers are too short for a {size}² grid.");
        var heights = new float[cells];
        var loose = new float[cells];
        var discharge = new float[cells];
        var depth = new float[cells];
        var standing = new float[cells];
        float divisor = Math.Max(flowSteps, 1);
        Parallel.For(0, size, j =>
        {
            for (int c = j * size; c < (j + 1) * size; c++)
            {
                float settled = terrain[c * 2 + 1] + sediment[c];
                heights[c] = terrain[c * 2] + settled;
                loose[c] = ToHalf(settled);
                discharge[c] = ToHalf(flow[c * 2] / divisor);
                depth[c] = ToHalf(flow[c * 2 + 1] / divisor);
                standing[c] = ToHalf(water[c]);
            }
        });
        var surface = new HeightField(size, cellSize, heights);
        if (poolFillMetres > 0f)
            surface = FillPools(surface, loose, standing, poolFillMetres);
        return new ErosionResult(surface, loose, discharge, depth, standing, steps, DrainageMap.Of(surface));
    }

    /// <summary>
    /// The pools the erosion left fill with sediment toward their outlets, at most <paramref name="limit"/> deep (vista-plan §10
    /// M1.11a): the sediment the water set down at the mouths of side valleys dams the valleys below, and a lake behind such a dam
    /// fills as it would in time, into a flat valley floor the water crosses. The fill joins the loose material; the water that
    /// stood there is gone.
    /// </summary>
    private static HeightField FillPools(HeightField surface, float[] loose, float[] standing, float limit)
    {
        double[] routing = DrainageMap.Flood(surface);
        float[] heights = surface.Heights.ToArray();
        int size = surface.Size;
        Parallel.For(0, size, j =>
        {
            for (int c = j * size; c < (j + 1) * size; c++)
            {
                double depth = routing[c] - heights[c];
                if (depth <= 0.0)
                    continue;
                float filled = (float)(heights[c] + Math.Min(depth, limit));
                loose[c] = ToHalf(loose[c] + (filled - heights[c]));
                standing[c] = 0f;
                heights[c] = filled;
            }
        });
        return new HeightField(size, surface.CellSize, heights);
    }

    /// <summary><paramref name="value"/> rounded to the nearest half-precision value.</summary>
    public static float ToHalf(float value) => (float)(Half)value;

    /// <summary>A 64-bit fingerprint of the surface, every map's exact bits and the drainage.</summary>
    public ulong ContentHash()
    {
        ulong hash = StableHash.Combine(Surface.ContentHash(), Drainage.ContentHash());
        Span<byte> buffer = stackalloc byte[4];
        foreach (float[] map in new[] { Loose, Discharge, WaterDepth, StandingWater })
        {
            foreach (float v in map)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(v));
                hash = StableHash.Fnv1a(hash, buffer);
            }
        }
        return hash;
    }
}

/// <summary>Maps that are functions of a height field alone.</summary>
public static class TerrainMaps
{
    /// <summary>
    /// The Laplacian of the surface per cell, in 1/m (positive in hollows and gullies, negative on ridges and peaks): the
    /// curvature map that snow (M1.13) reads for wind scour and fill. One-sided at the grid's edges (there it is 0).
    /// </summary>
    public static float[] Curvature(HeightField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        int n = field.Size;
        var curvature = new float[n * n];
        double inv = 1.0 / (field.CellSize * field.CellSize);
        Parallel.For(1, n - 1, j =>
        {
            for (int i = 1; i < n - 1; i++)
            {
                double sum = (double)field[i - 1, j] + field[i + 1, j] + field[i, j - 1] + field[i, j + 1] - 4.0 * field[i, j];
                curvature[j * n + i] = (float)(sum * inv);
            }
        });
        return curvature;
    }
}
