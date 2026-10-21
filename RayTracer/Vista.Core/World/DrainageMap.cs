using System.Buffers.Binary;

namespace Vista.World;

/// <summary>
/// Where the rain on a surface goes (vista-plan §10 M1.11a): every cell's water leaves by one neighbour, the steepest way down
/// (D8), and a cell's upstream area — itself and every cell whose water passes through it — is the flow accumulation that the
/// materials (M1.12) and the rivers (M3) read. Depressions are filled before the water is routed (Priority-Flood+ε: Barnes,
/// Lehman and Mulla 2014), so water that runs into a hollow fills it and leaves over its lowest rim instead of stopping there;
/// how deep each cell was filled is kept (<see cref="FillDepth"/>), since a depression is where a lake would stand. The edge
/// of the grid is where the world's water leaves: every edge cell is an outlet.
/// </summary>
/// <remarks>
/// A pure function of the surface's bits, single-threaded where order matters: the flood runs in double precision with a
/// fixed neighbour order, and the upstream areas are integer sums, so the map is the same on every machine.
/// </remarks>
public sealed class DrainageMap
{
    /// <summary>The <see cref="Direction"/> of a cell whose water leaves the grid.</summary>
    public const byte Outlet = 255;

    /// <summary>The eight neighbours' column offsets, in the order the erosion sums them (<see cref="ErosionReference"/>).</summary>
    public static ReadOnlySpan<int> Di => [-1, 0, 1, -1, 1, -1, 0, 1];

    /// <summary>The eight neighbours' row offsets, in the same order.</summary>
    public static ReadOnlySpan<int> Dj => [-1, -1, -1, 0, 0, 1, 1, 1];

    /// <summary>Wraps a drainage given as its directions and fill depths (as made by <see cref="Of"/>, or read back from the cache).</summary>
    public DrainageMap(int size, double cellSize, byte[] direction, float[] fillDepth)
    {
        ArgumentNullException.ThrowIfNull(direction);
        ArgumentNullException.ThrowIfNull(fillDepth);
        int cells = size * size;
        if (direction.Length != cells || fillDepth.Length != cells)
            throw new ArgumentException($"Expected {cells} values per map, got {direction.Length} and {fillDepth.Length}.");
        Size = size;
        CellSize = cellSize;
        Direction = direction;
        FillDepth = fillDepth;
        Order = UpstreamFirst(size, direction);
        Upstream = Accumulate(size, direction, Order);
    }

    /// <summary>Samples per side.</summary>
    public int Size { get; }

    /// <summary>Cell size in metres.</summary>
    public double CellSize { get; }

    /// <summary>Cell area in m².</summary>
    public double CellArea => CellSize * CellSize;

    /// <summary>
    /// Per cell, the neighbour its water leaves by, as an index into <see cref="Di"/> / <see cref="Dj"/>, or
    /// <see cref="Outlet"/> on the grid's edge.
    /// </summary>
    public byte[] Direction { get; }

    /// <summary>
    /// Per cell, how far the depression filling raised the water's surface above the ground, in metres: 0 where the ground
    /// drains, the depth a lake would stand at in a hollow. Rounded to half precision (<see cref="ErosionResult.ToHalf"/>), as
    /// the cache keeps it.
    /// </summary>
    public float[] FillDepth { get; }

    /// <summary>Per cell, the cells that drain through it, itself included.</summary>
    public int[] Upstream { get; }

    /// <summary>Every cell once, each before the cell its water flows to (sources first, outlets last).</summary>
    public int[] Order { get; }

    /// <summary>The cell that <paramref name="c"/> drains into, or −1 when its water leaves the grid.</summary>
    public int Receiver(int c)
    {
        byte d = Direction[c];
        return d == Outlet ? -1 : c + Dj[d] * Size + Di[d];
    }

    /// <summary>The distance from <paramref name="c"/> to its receiver in metres (0 for an outlet).</summary>
    public double StepLength(int c)
    {
        byte d = Direction[c];
        if (d == Outlet)
            return 0.0;
        return Di[d] != 0 && Dj[d] != 0 ? CellSize * Math.Sqrt(2.0) : CellSize;
    }

    /// <summary>The upstream area of <paramref name="c"/> in m².</summary>
    public double Area(int c) => Upstream[c] * CellArea;

    /// <summary>The drainage of <paramref name="surface"/>.</summary>
    public static DrainageMap Of(HeightField surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        int n = surface.Size;
        double[] routing = Flood(surface);
        float[] heights = surface.Heights.ToArray();
        var direction = new byte[n * n];
        var fill = new float[n * n];
        double diagonal = 1.0 / Math.Sqrt(2.0);
        // Each cell reads the finished routing surface and writes only itself, so the rows may run in any order.
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                int c = j * n + i;
                fill[c] = ErosionResult.ToHalf((float)(routing[c] - heights[c]));
                if (i == 0 || j == 0 || i == n - 1 || j == n - 1)
                {
                    direction[c] = Outlet;
                    continue;
                }
                int best = -1;
                double steepest = 0.0;
                for (int q = 0; q < 8; q++)
                {
                    double drop = routing[c] - routing[c + Dj[q] * n + Di[q]];
                    double slope = Di[q] != 0 && Dj[q] != 0 ? drop * diagonal : drop;
                    if (slope > steepest)
                    {
                        steepest = slope;
                        best = q;
                    }
                }
                // The flood leaves every inner cell a strictly lower neighbour: the one it was reached from.
                direction[c] = best >= 0 ? (byte)best : throw new InvalidOperationException($"Cell ({i}, {j}) has no way down after the flood.");
            }
        });
        return new DrainageMap(n, surface.CellSize, direction, fill);
    }

    /// <summary>
    /// The routing surface: the ground with every depression filled to its spill height plus one double-precision ulp per cell
    /// from the spill point inward (Priority-Flood+ε), so every inner cell has a strictly lower neighbour and flats drain toward
    /// their outlet. The flood starts from the whole edge; a cell reached from a lower one at or below its height is part of a
    /// depression and is raised.
    /// </summary>
    internal static double[] Flood(HeightField surface)
    {
        int n = surface.Size;
        ReadOnlySpan<float> h = surface.Heights;
        var routing = new double[n * n];
        var closed = new bool[n * n];
        var open = new PriorityQueue<int, double>(4 * n);
        var pit = new Queue<int>();
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                if (i != 0 && j != 0 && i != n - 1 && j != n - 1)
                    continue;
                int c = j * n + i;
                routing[c] = h[c];
                closed[c] = true;
                open.Enqueue(c, h[c]);
            }
        }
        // The depression's cells go through a plain queue, ahead of the priority queue: each is one ulp above the cell it was
        // reached from, so they are already in order (and most of a world never enters the slower priority queue twice).
        while (pit.Count > 0 || open.Count > 0)
        {
            int c = pit.Count > 0 ? pit.Dequeue() : open.Dequeue();
            int i = c % n, j = c / n;
            double raised = Math.BitIncrement(routing[c]);
            for (int q = 0; q < 8; q++)
            {
                int ni = i + Di[q], nj = j + Dj[q];
                if (ni < 0 || nj < 0 || ni >= n || nj >= n)
                    continue;
                int m = nj * n + ni;
                if (closed[m])
                    continue;
                closed[m] = true;
                if (h[m] <= raised)
                {
                    routing[m] = raised;
                    pit.Enqueue(m);
                }
                else
                {
                    routing[m] = h[m];
                    open.Enqueue(m, h[m]);
                }
            }
        }
        return routing;
    }

    /// <summary>A 64-bit fingerprint of the directions and the fill depths.</summary>
    public ulong ContentHash()
    {
        ulong hash = StableHash.Fnv1a(StableHash.FnvStart, Direction);
        Span<byte> buffer = stackalloc byte[4];
        foreach (float v in FillDepth)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(v));
            hash = StableHash.Fnv1a(hash, buffer);
        }
        return hash;
    }

    // Kahn's order: cells nothing drains into first, then each cell once everything upstream of it has come.
    private static int[] UpstreamFirst(int n, byte[] direction)
    {
        int cells = n * n;
        var donors = new int[cells];
        for (int c = 0; c < cells; c++)
        {
            int r = ReceiverOf(n, direction, c);
            if (r >= 0)
                donors[r]++;
        }
        var order = new int[cells];
        int head = 0, tail = 0;
        for (int c = 0; c < cells; c++)
        {
            if (donors[c] == 0)
                order[tail++] = c;
        }
        while (head < tail)
        {
            int r = ReceiverOf(n, direction, order[head++]);
            if (r >= 0 && --donors[r] == 0)
                order[tail++] = r;
        }
        if (tail != cells)
            throw new InvalidDataException($"The drainage has a loop: {cells - tail} cells never reach an outlet.");
        return order;
    }

    private static int[] Accumulate(int n, byte[] direction, int[] order)
    {
        var upstream = new int[n * n];
        Array.Fill(upstream, 1);
        foreach (int c in order)
        {
            int r = ReceiverOf(n, direction, c);
            if (r >= 0)
                upstream[r] += upstream[c];
        }
        return upstream;
    }

    private static int ReceiverOf(int n, byte[] direction, int c)
    {
        byte d = direction[c];
        if (d == Outlet)
            return -1;
        if (d > 7)
            throw new InvalidDataException($"Cell {c} has the direction {d}.");
        int r = c + Dj[d] * n + Di[d];
        return r >= 0 && r < n * n ? r : throw new InvalidDataException($"Cell {c} drains off the grid without being an outlet.");
    }
}
