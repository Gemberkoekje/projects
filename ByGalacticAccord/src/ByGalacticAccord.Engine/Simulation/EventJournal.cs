using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// One thing that happened, recorded as data rather than prose (design §11.5): the interface turns each
/// kind of entry into a sentence with a template keyed by the entry's type. An entry carries only what
/// was true at that moment; anything the contract itself still holds (its good, places, posted fee) is
/// read from the contract, which is never removed. Nothing here is hidden state: the poster's
/// reservation fee is deliberately absent.
/// </summary>
public abstract record JournalEntry(long Tick);

/// <summary>A consumer posted a Haul from a producer's location to its own.</summary>
public sealed record ContractPostedEntry(long Tick, ContractId Contract, ActorId Poster, ActorId Supplier)
    : JournalEntry(Tick);

/// <summary>A hauler signed a Haul at a fee.</summary>
public sealed record HaulTakenEntry(long Tick, ContractId Contract, ActorId Hauler, Credits Fee)
    : JournalEntry(Tick);

/// <summary>A Haul settled: what arrived, how late, and what the terms took off the fee.</summary>
public sealed record HaulDeliveredEntry(
    long Tick,
    ContractId Contract,
    ActorId Hauler,
    decimal Quantity,
    decimal Quality,
    long TicksLate,
    Credits Fee,
    Credits LateFee,
    Credits QualityRefund)
    : JournalEntry(Tick);

/// <summary>Outside the Accord, the poster could not pay at handover: a default. The goods are written off.</summary>
public sealed record PaymentDefaultedEntry(long Tick, ContractId Contract, ActorId Payer, ActorId Hauler)
    : JournalEntry(Tick);

/// <summary>A Haul failed because its whole cargo was lost in transit.</summary>
public sealed record DeliveryFailedEntry(long Tick, ContractId Contract, ActorId Hauler)
    : JournalEntry(Tick);

/// <summary>Nobody took an open contract before its deadline.</summary>
public sealed record ContractExpiredEntry(long Tick, ContractId Contract)
    : JournalEntry(Tick);

/// <summary>A transit incident struck a laden ship.</summary>
public sealed record IncidentEntry(long Tick, ContractId Contract, ActorId Hauler, TransitIncident Incident)
    : JournalEntry(Tick);

/// <summary>The inflation monitor moved the faucet (design §5.5). Internal bookkeeping, not news.</summary>
public sealed record FaucetAdjustedEntry(long Tick, decimal Drift, decimal Multiplier)
    : JournalEntry(Tick);

/// <summary>
/// The append-only record of what happened. In memory, capped so long soak runs stay bounded; the
/// interface reads its tail as the event log.
/// </summary>
public sealed class EventJournal
{
    private readonly List<JournalEntry> _entries = new();
    private readonly int _cap;

    public EventJournal(int cap = 5000) => _cap = cap;

    public IReadOnlyList<JournalEntry> Entries => _entries;

    public long TotalRecorded { get; private set; }

    public void Record(JournalEntry entry)
    {
        TotalRecorded++;
        _entries.Add(entry);
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
