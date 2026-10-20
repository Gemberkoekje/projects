namespace Vista.World;

/// <summary>
/// An eroded world (vista-plan §4.3, §10 M1.7): the ground surface, and the maps later stages read — the loose material left
/// on the bedrock (scree under faces, sediment in valleys: the deposition map), the mean discharge (the flow map: rivers carry
/// the rain of everything upstream), the mean water depth (the wetness map) and the water standing at the end (where lakes
/// pool). Curvature is a function of the surface alone (<see cref="TerrainMaps.Curvature"/>). Every map is row-major on the
/// grid of <see cref="Surface"/>.
/// </summary>
public sealed class ErosionResult
{
    /// <summary>Wraps the maps without copying.</summary>
    public ErosionResult(HeightField surface, float[] loose, float[] discharge, float[] waterDepth, float[] standingWater, int steps)
    {
        ArgumentNullException.ThrowIfNull(surface);
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

    /// <summary>
    /// The result of <paramref name="state"/>: the sediment still suspended is set down where it is (the water dries out), the
    /// flow sums become means, and every map is rounded to half precision (<see cref="Half"/>), the precision the cache keeps
    /// them in, so a world fresh from the device and the same world read from the cache are the same bits. The surface stays
    /// full precision.
    /// </summary>
    public static ErosionResult From(ErosionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return From(state.Size, state.CellSize, state.Terrain, state.Water, state.Sediment, state.Flow, state.FlowSteps, state.Steps);
    }

    /// <summary>The result of a state given as its buffers (laid out as <see cref="ErosionState"/>'s), read back from a device.</summary>
    public static ErosionResult From(int size, double cellSize, float[] terrain, float[] water, float[] sediment, float[] flow, int flowSteps, int steps)
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
        return new ErosionResult(new HeightField(size, cellSize, heights), loose, discharge, depth, standing, steps);
    }

    /// <summary><paramref name="value"/> rounded to the nearest half-precision value.</summary>
    public static float ToHalf(float value) => (float)(Half)value;

    /// <summary>A 64-bit fingerprint of the surface and every map's exact bits.</summary>
    public ulong ContentHash()
    {
        ulong hash = Surface.ContentHash();
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
