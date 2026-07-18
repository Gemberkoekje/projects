using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;

namespace RayTracer;

public sealed class DiffuseIrradianceCache
{
    private readonly float _cellSize;
    private readonly float _invCellSize;
    private readonly uint _minSamples;

    private readonly ConcurrentDictionary<CellKey, CellState> _entries = new();
    private long _lookupCount;
    private long _hitCount;
    private int _currentFrame;

    public DiffuseIrradianceCache(float cellSize, uint minSamples)
    {
        if (cellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be greater than zero.");

        if (minSamples == 0)
            throw new ArgumentOutOfRangeException(nameof(minSamples), minSamples, "Minimum samples must be greater than zero.");

        _cellSize = cellSize;
        _invCellSize = 1f / cellSize;
        _minSamples = minSamples;
    }

    public int EntryCount => _entries.Count;

    public double HitRate
    {
        get
        {
            long lookups = Interlocked.Read(ref _lookupCount);
            if (lookups <= 0)
                return 0d;

            long hits = Interlocked.Read(ref _hitCount);
            return (double)hits / lookups;
        }
    }

    public bool TryLookup(Vector3 position, Vector3 normal, out float irradiance)
    {
        Interlocked.Increment(ref _lookupCount);

        CellKey key = MakeKey(position, normal);
        if (_entries.TryGetValue(key, out CellState? state))
        {
            lock (state.Sync)
            {
                IrradianceCacheEntry entry = state.Entry;
                if (entry.SampleCount >= _minSamples)
                {
                    entry.LastUsedFrame = Volatile.Read(ref _currentFrame);
                    state.Entry = entry;
                    irradiance = entry.Irradiance;
                    Interlocked.Increment(ref _hitCount);
                    return true;
                }
            }
        }

        irradiance = 0f;
        return false;
    }

    public void Accumulate(Vector3 position, Vector3 normal, float sample)
    {
        CellKey key = MakeKey(position, normal);
        CellState state = _entries.GetOrAdd(key, _ => new CellState
        {
            Entry = new IrradianceCacheEntry
            {
                Position = position,
                Normal = Vector3.Normalize(normal),
                Irradiance = sample,
                SampleCount = 0,
                LastUsedFrame = Volatile.Read(ref _currentFrame)
            }
        });

        lock (state.Sync)
        {
            IrradianceCacheEntry entry = state.Entry;
            uint nextCount = entry.SampleCount + 1;
            entry.Position = Vector3.Lerp(entry.Position, position, 1f / nextCount);
            entry.Normal = Vector3.Normalize(Vector3.Lerp(entry.Normal, Vector3.Normalize(normal), 1f / nextCount));
            entry.Irradiance += (sample - entry.Irradiance) / nextCount;
            entry.SampleCount = nextCount;
            entry.LastUsedFrame = Volatile.Read(ref _currentFrame);
            state.Entry = entry;
        }
    }

    public void EvictStale(int currentFrame, int maxAge)
    {
        if (maxAge < 0)
            throw new ArgumentOutOfRangeException(nameof(maxAge), maxAge, "Max age cannot be negative.");

        Volatile.Write(ref _currentFrame, currentFrame);

        foreach (KeyValuePair<CellKey, CellState> kv in _entries)
        {
            bool remove;
            lock (kv.Value.Sync)
            {
                remove = currentFrame - kv.Value.Entry.LastUsedFrame > maxAge;
            }

            if (remove)
                _entries.TryRemove(kv.Key, out _);
        }
    }

    public void Clear()
    {
        _entries.Clear();
        Interlocked.Exchange(ref _lookupCount, 0);
        Interlocked.Exchange(ref _hitCount, 0);
        Volatile.Write(ref _currentFrame, 0);
    }

    private CellKey MakeKey(Vector3 position, Vector3 normal)
    {
        int x = (int)MathF.Floor(position.X * _invCellSize);
        int y = (int)MathF.Floor(position.Y * _invCellSize);
        int z = (int)MathF.Floor(position.Z * _invCellSize);
        int octant = NormalOctant(normal);
        return new CellKey(x, y, z, octant);
    }

    private static int NormalOctant(Vector3 normal)
    {
        Vector3 n = Vector3.Normalize(normal);
        int sx = n.X >= 0f ? 1 : 0;
        int sy = n.Y >= 0f ? 1 : 0;
        int sz = n.Z >= 0f ? 1 : 0;
        return sx | (sy << 1) | (sz << 2);
    }

    private readonly record struct CellKey(int X, int Y, int Z, int NormalOctant);

    private sealed class CellState
    {
        public object Sync { get; } = new();
        public IrradianceCacheEntry Entry;
    }
}
