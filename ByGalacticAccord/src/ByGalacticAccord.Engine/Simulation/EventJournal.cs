using System.Collections.Generic;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>One processed event, recorded. The player's own slice of this is their captain's log (§5.7, §3.5).</summary>
public readonly record struct JournalEntry(long Tick, string Category, string Text);

/// <summary>
/// The append-only record of everything that happened. In the GDD's Marten design this is the
/// event store that aggregates project from; here it is an in-memory log that also doubles as the
/// readable history surfaced in the UI. Capped to keep long soak runs bounded in memory.
/// </summary>
public sealed class EventJournal
{
    private readonly List<JournalEntry> _entries = new();
    private readonly int _cap;

    public EventJournal(int cap = 5000) => _cap = cap;

    public IReadOnlyList<JournalEntry> Entries => _entries;

    public long TotalRecorded { get; private set; }

    public void Record(long tick, string category, string text)
    {
        TotalRecorded++;
        _entries.Add(new JournalEntry(tick, category, text));
        if (_entries.Count > _cap)
        {
            _entries.RemoveRange(0, _entries.Count - _cap);
        }
    }

    public IEnumerable<JournalEntry> Tail(int count)
    {
        int start = _entries.Count - count;
        if (start < 0)
        {
            start = 0;
        }

        for (int i = start; i < _entries.Count; i++)
        {
            yield return _entries[i];
        }
    }
}
