using System.Buffers.Binary;

namespace Vista.World;

/// <summary>
/// A square height field on the fixed global grid (vista-plan §4.1): <see cref="Size"/>² samples spaced
/// <see cref="CellSize"/> metres apart, centred on the world origin. Sample <c>(i, j)</c> sits at world
/// <c>x = OriginX + i·CellSize</c>, <c>z = OriginZ + j·CellSize</c> (<c>+X</c> east, <c>+Z</c> north).
/// The grid never depends on the camera, which is what lets the global erosion of M1 stay deterministic.
///
/// <para>A height field never changes once made: its lowest and highest points and its <see cref="ContentHash"/> are worked out
/// the first time they are asked for and kept, since a watch session (vista-plan §10 M1.8) asks for them on every render of the
/// same 4097² world.</para>
/// </summary>
public sealed class HeightField
{
    private readonly float[] _heights;
    private (float Min, float Max)? _range;
    private ulong? _hash;

    /// <summary>
    /// Wraps <paramref name="heights"/> (row-major, <c>j·size + i</c>) without copying; the caller must not change it afterwards.
    /// </summary>
    public HeightField(int size, double cellSize, float[] heights)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        ArgumentNullException.ThrowIfNull(heights);
        if (heights.Length != size * size)
            throw new ArgumentException($"Expected {size * size} heights, got {heights.Length}.", nameof(heights));
        Size = size;
        CellSize = cellSize;
        _heights = heights;
    }

    /// <summary>Samples per side.</summary>
    public int Size { get; }

    /// <summary>Spacing between samples in metres.</summary>
    public double CellSize { get; }

    /// <summary>Side length in metres.</summary>
    public double Extent => (Size - 1) * CellSize;

    /// <summary>World x of column 0.</summary>
    public double OriginX => -0.5 * Extent;

    /// <summary>World z of row 0.</summary>
    public double OriginZ => -0.5 * Extent;

    /// <summary>Height in metres of sample <c>(i, j)</c>.</summary>
    public float this[int i, int j] => _heights[j * Size + i];

    /// <summary>All heights, row-major (<c>j·Size + i</c>).</summary>
    public ReadOnlySpan<float> Heights => _heights;

    /// <summary>Lowest sample in metres.</summary>
    public float MinHeight => Range.Min;

    /// <summary>Highest sample in metres.</summary>
    public float MaxHeight => Range.Max;

    private (float Min, float Max) Range => _range ??= (_heights.Min(), _heights.Max());

    /// <summary>World x of column <paramref name="i"/>.</summary>
    public double XOf(int i) => OriginX + i * CellSize;

    /// <summary>World z of row <paramref name="j"/>.</summary>
    public double ZOf(int j) => OriginZ + j * CellSize;

    /// <summary>Bilinearly interpolated height at world <c>(x, z)</c>, clamped to the grid edge outside it.</summary>
    public double HeightAt(double x, double z)
    {
        double u = Math.Clamp((x - OriginX) / CellSize, 0.0, Size - 1);
        double v = Math.Clamp((z - OriginZ) / CellSize, 0.0, Size - 1);
        int i = Math.Min((int)u, Size - 2);
        int j = Math.Min((int)v, Size - 2);
        double fu = u - i;
        double fv = v - j;
        double h0 = this[i, j] + (this[i + 1, j] - this[i, j]) * fu;
        double h1 = this[i, j + 1] + (this[i + 1, j + 1] - this[i, j + 1]) * fu;
        return h0 + (h1 - h0) * fv;
    }

    /// <summary>
    /// A 64-bit fingerprint of the grid and every height's exact bits. Recorded in the sidecar, so two renders
    /// can be checked for "same world" without comparing images, and pinned by the determinism tests.
    /// </summary>
    public ulong ContentHash() => _hash ??= Hash();

    private ulong Hash()
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, Size);
        ulong hash = StableHash.Fnv1a(StableHash.FnvStart, buffer[..4]);
        BinaryPrimitives.WriteInt64LittleEndian(buffer, BitConverter.DoubleToInt64Bits(CellSize));
        hash = StableHash.Fnv1a(hash, buffer);
        foreach (float h in _heights)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(h));
            hash = StableHash.Fnv1a(hash, buffer[..4]);
        }
        return hash;
    }
}
