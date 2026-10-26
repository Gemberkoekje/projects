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

/// <summary>Someone posted a contract: a Sale (wanted or for sale) or a Haul.</summary>
public sealed record ContractPostedEntry(long Tick, ContractId Contract, ActorId Poster)
    : JournalEntry(Tick);

/// <summary>Someone took a posted contract at a price: supplied a wanted Sale, bought a lot, took a Haul.</summary>
public sealed record ContractSignedEntry(long Tick, ContractId Contract, ActorId Taker, Credits Price)
    : JournalEntry(Tick);

/// <summary>A Sale settled: goods handed over, how late, what the seller was paid and what the terms took off.</summary>
public sealed record GoodsHandedOverEntry(
    long Tick,
    ContractId Contract,
    ActorId Seller,
    ActorId Buyer,
    decimal Quantity,
    decimal Quality,
    long TicksLate,
    Credits Price,
    Credits LateFee,
    Credits QualityRefund)
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

/// <summary>Outside the Accord, the payer could not pay at handover: a default. A seller keeps its goods.</summary>
public sealed record PaymentDefaultedEntry(long Tick, ContractId Contract, ActorId Payer, ActorId Payee)
    : JournalEntry(Tick);

/// <summary>Why a performer failed.</summary>
public enum FailureReason
{
    /// <summary>The goods were lost in transit.</summary>
    CargoLost,

    /// <summary>Nothing was delivered by the time the late fee reached its cap.</summary>
    NotDelivered,
}

/// <summary>A Sale or Haul failed: its performer's bond went to the payer and the payer's escrow came back.</summary>
public sealed record DeliveryFailedEntry(long Tick, ContractId Contract, ActorId Performer, FailureReason Reason)
    : JournalEntry(Tick);

/// <summary>Nobody took an open contract before its deadline.</summary>
public sealed record ContractExpiredEntry(long Tick, ContractId Contract)
    : JournalEntry(Tick);

/// <summary>The poster took an open contract down: it no longer needs it, or it is posting new terms.</summary>
public sealed record ContractWithdrawnEntry(long Tick, ContractId Contract)
    : JournalEntry(Tick);

/// <summary>Someone rented storage from a station operator.</summary>
public sealed record StorageOpenedEntry(long Tick, ContractId Contract, ActorId Renter)
    : JournalEntry(Tick);

/// <summary>The last goods left a store, which ended its contract.</summary>
public sealed record StorageClosedEntry(long Tick, ContractId Contract, ActorId Renter, Credits RentPaid)
    : JournalEntry(Tick);

/// <summary>A transit incident struck a laden ship, on the job for <see cref="Contract"/>.</summary>
public sealed record IncidentEntry(long Tick, ContractId Contract, ActorId Hauler, TransitIncident Incident)
    : JournalEntry(Tick);

/// <summary>The inflation monitor moved the faucet (design §5.5). Internal bookkeeping, not news.</summary>
public sealed record FaucetAdjustedEntry(long Tick, decimal Drift, decimal Multiplier)
    : JournalEntry(Tick);

/// <summary>
/// The append-only record of what happened. In memory, capped so long soak runs stay bounded: a ring of the last
/// <c>cap</c> entries, oldest first. The interface reads its tail as the event log.
/// </summary>
public sealed class EventJournal
{
    private readonly JournalEntry[] _ring;
    private int _start;
    private int _count;

    public EventJournal(int cap = 5000)
    {
        _ring = new JournalEntry[cap];
        Entries = new View(this);
    }

    /// <summary>The kept entries, oldest first.</summary>
    public IReadOnlyList<JournalEntry> Entries { get; }

    public long TotalRecorded { get; private set; }

    public void Record(JournalEntry entry)
    {
        TotalRecorded++;
        if (_count < _ring.Length)
        {
            _ring[(_start + _count) % _ring.Length] = entry;
            _count++;
        }
        else
        {
            _ring[_start] = entry; // overwrite the oldest
            _start = (_start + 1) % _ring.Length;
        }
    }

    public IEnumerable<JournalEntry> Tail(int count)
    {
        for (int i = System.Math.Max(0, _count - count); i < _count; i++)
        {
            yield return Entries[i];
        }
    }

    private sealed class View : IReadOnlyList<JournalEntry>
    {
        private readonly EventJournal _journal;

        public View(EventJournal journal) => _journal = journal;

        public int Count => _journal._count;

        public JournalEntry this[int index] =>
            (uint)index < (uint)_journal._count
                ? _journal._ring[(_journal._start + index) % _journal._ring.Length]
                : throw new System.ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<JournalEntry> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
