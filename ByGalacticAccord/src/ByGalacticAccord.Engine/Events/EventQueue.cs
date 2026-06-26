using System.Collections.Generic;

namespace ByGalacticAccord.Engine.Events;

/// <summary>
/// A min-heap of future events ordered by tick (§5.4). Ties break by insertion sequence so the
/// dequeue order is fully deterministic — essential for replay (§5.5).
/// </summary>
public sealed class EventQueue
{
    private readonly List<Entry> _heap = new();
    private long _sequence;

    private readonly record struct Entry(long Tick, long Sequence, ISimEvent Event);

    public int Count => _heap.Count;

    public bool HasEvents => _heap.Count > 0;

    public long? NextTick => _heap.Count > 0 ? _heap[0].Tick : null;

    public void Enqueue(ISimEvent simEvent)
    {
        var entry = new Entry(simEvent.Tick, _sequence++, simEvent);
        _heap.Add(entry);
        SiftUp(_heap.Count - 1);
    }

    public ISimEvent Dequeue()
    {
        Entry root = _heap[0];
        int last = _heap.Count - 1;
        _heap[0] = _heap[last];
        _heap.RemoveAt(last);
        if (_heap.Count > 0)
        {
            SiftDown(0);
        }

        return root.Event;
    }

    private static bool Precedes(in Entry a, in Entry b) =>
        a.Tick != b.Tick ? a.Tick < b.Tick : a.Sequence < b.Sequence;

    private void SiftUp(int i)
    {
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (Precedes(_heap[i], _heap[parent]))
            {
                (_heap[i], _heap[parent]) = (_heap[parent], _heap[i]);
                i = parent;
            }
            else
            {
                break;
            }
        }
    }

    private void SiftDown(int i)
    {
        int count = _heap.Count;
        while (true)
        {
            int left = (2 * i) + 1;
            int right = (2 * i) + 2;
            int smallest = i;

            if (left < count && Precedes(_heap[left], _heap[smallest]))
            {
                smallest = left;
            }

            if (right < count && Precedes(_heap[right], _heap[smallest]))
            {
                smallest = right;
            }

            if (smallest == i)
            {
                break;
            }

            (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]);
            i = smallest;
        }
    }
}
