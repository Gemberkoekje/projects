namespace Vista.World;

/// <summary>
/// The smooth normals of one of the hierarchy's grids, sample by sample (vista-plan §10 M1.9): what the view-dependent mesh's
/// shading adds where its triangles are coarser than the grid. A triangle many samples wide carries the normals of its three
/// corners only, so the gullies inside it — light on one wall, dark on the other — vanish from its shading though they would
/// fill pixels a finer mesh renders; the integrator adds the map's normal at the hit less the map's normals interpolated from
/// the corners (<see cref="TerrainMesh.MapNormals"/>), which is nothing on a triangle as fine as the grid and the grid's own
/// shading on a coarse one. Normals are central differences of the grid's heights (the Catmull–Rom surface's own slope at a
/// sample), stored as their x and z in half precision; between samples they are interpolated bilinearly, and outside the grid
/// the edge's are taken.
/// </summary>
public sealed class NormalMap
{
    private readonly uint[] _packed;

    /// <summary>The map of <paramref name="heights"/> (row-major, <paramref name="size"/>² samples <paramref name="cell"/> metres apart from <c>(originX, originZ)</c>).</summary>
    public NormalMap(float[] heights, int size, double cell, double originX, double originZ)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        if (heights.Length != size * size)
            throw new ArgumentException($"Expected {size * size} heights, got {heights.Length}.", nameof(heights));
        Size = size;
        Cell = cell;
        OriginX = originX;
        OriginZ = originZ;
        _packed = new uint[size * size];
        Parallel.For(0, size, j =>
        {
            int j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, size - 1);
            for (int i = 0; i < size; i++)
            {
                int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, size - 1);
                double dhdx = (heights[j * size + i1] - heights[j * size + i0]) / ((i1 - i0) * cell);
                double dhdz = (heights[j1 * size + i] - heights[j0 * size + i]) / ((j1 - j0) * cell);
                double inv = 1.0 / Math.Sqrt(dhdx * dhdx + 1.0 + dhdz * dhdz);
                _packed[j * size + i] = Pack(-dhdx * inv, -dhdz * inv);
            }
        });
    }

    /// <summary>Samples a side.</summary>
    public int Size { get; }

    /// <summary>Metres between samples.</summary>
    public double Cell { get; }

    /// <summary>World x of sample column 0.</summary>
    public double OriginX { get; }

    /// <summary>World z of sample row 0.</summary>
    public double OriginZ { get; }

    /// <summary>The normals' x and z, as two halves in a uint (x low), row-major: what the GPU reads.</summary>
    public ReadOnlySpan<uint> Packed => _packed;

    /// <summary>Upload size, bytes.</summary>
    public long ByteSize => (long)_packed.Length * sizeof(uint);

    /// <summary>Whether world <c>(x, z)</c> lies on the map (its edges included).</summary>
    public bool Contains(double x, double z)
    {
        double extent = (Size - 1) * Cell;
        return x >= OriginX && x <= OriginX + extent && z >= OriginZ && z <= OriginZ + extent;
    }

    /// <summary>The normal's x and z at world <c>(x, z)</c>: bilinear between the samples around it, the edge's outside the map.</summary>
    public (float X, float Z) Sample(double x, double z)
    {
        double u = Math.Clamp((x - OriginX) / Cell, 0.0, Size - 1), v = Math.Clamp((z - OriginZ) / Cell, 0.0, Size - 1);
        int i = Math.Min((int)u, Size - 2), j = Math.Min((int)v, Size - 2);
        float fu = (float)(u - i), fv = (float)(v - j);
        (float x00, float z00) = Unpack(_packed[j * Size + i]);
        (float x10, float z10) = Unpack(_packed[j * Size + i + 1]);
        (float x01, float z01) = Unpack(_packed[(j + 1) * Size + i]);
        (float x11, float z11) = Unpack(_packed[(j + 1) * Size + i + 1]);
        float bx = x00 + (x10 - x00) * fu, tx = x01 + (x11 - x01) * fu;
        float bz = z00 + (z10 - z00) * fu, tz = z01 + (z11 - z01) * fu;
        return (bx + (tx - bx) * fv, bz + (tz - bz) * fv);
    }

    /// <summary>Two halves in a uint, x in the low 16 bits (HLSL's <c>f16tof32</c> reads them back).</summary>
    public static uint Pack(double x, double z) =>
        BitConverter.HalfToUInt16Bits((Half)x) | ((uint)BitConverter.HalfToUInt16Bits((Half)z) << 16);

    /// <summary>The two halves of <see cref="Pack"/>.</summary>
    public static (float X, float Z) Unpack(uint packed) =>
        ((float)BitConverter.UInt16BitsToHalf((ushort)(packed & 0xFFFF)), (float)BitConverter.UInt16BitsToHalf((ushort)(packed >> 16)));
}
