using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Thread-safe in-memory circular buffer that keeps the last 360 credit snapshots.
/// At a 10-second recording interval this covers a rolling one-hour window.
/// </summary>
public sealed class CreditHistoryService : ICreditHistoryService
{
    private const int Capacity = 360;

    private readonly (DateTimeOffset Timestamp, long Credits)[] _buffer =
        new (DateTimeOffset, long)[Capacity];

    private readonly object _lock = new();
    private int _head;
    private int _count;

    public void Record(long credits)
    {
        lock (_lock)
        {
            _buffer[_head] = (TimeProvider.System.GetUtcNow(), credits);
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }
    }

    public IReadOnlyList<(DateTimeOffset Timestamp, long Credits)> GetHistory()
    {
        lock (_lock)
        {
            if (_count == 0)
                return Array.Empty<(DateTimeOffset, long)>();

            var result = new (DateTimeOffset, long)[_count];
            // oldest entry is at (_head - _count + Capacity) % Capacity
            var start = (_head - _count + Capacity) % Capacity;
            for (var i = 0; i < _count; i++)
                result[i] = _buffer[(start + i) % Capacity];

            return result;
        }
    }
}
