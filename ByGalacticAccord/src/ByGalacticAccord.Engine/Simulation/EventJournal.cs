using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// One thing that happened, recorded as data rather than prose (design §11.5): the interface turns each
/// kind of entry into a sentence with a template keyed by the entry's type. An entry carries only what
/// was true at that moment; anything the contract itself still holds (its good, places, posted fee) is
/// read from the contract, which is never removed. Nothing here is hidden state: the poster's
/// reservation fee is deliberately absent.
/// <para>
/// Every entry records its cause (design §16.2, §19): <see cref="Cause"/> is the <see cref="Seq"/> of the entry
/// that led to it. By default that is the previous entry about the same contract, or about a haul delivering it,
/// so a contract's record reads back as a chain: handed over late, because the haul was late, because of an
/// incident on the Rust Belt run. Where something else caused it (a haul's failure failing the Sale it served),
/// the recorder names that entry.
/// </para>
/// </summary>
public abstract record JournalEntry(long Tick)
{
    /// <summary>Its place in the journal: how many entries were recorded before it. Set when it is recorded.</summary>
    public long Seq { get; init; } = -1;

    /// <summary>The entry that led to this one, if any.</summary>
    public long? Cause { get; init; }
}

/// <summary>An entry about one contract. The journal keeps each contract's latest entry as its next default cause.</summary>
public interface IContractEntry
{
    ContractId Contract { get; }
}

/// <summary>Someone posted a contract: a Sale (wanted or for sale) or a Haul.</summary>
public sealed record ContractPostedEntry(long Tick, ContractId Contract, ActorId Poster)
    : JournalEntry(Tick), IContractEntry;

/// <summary>Someone took a posted contract at a price: supplied a wanted Sale, bought a lot, took a Haul.</summary>
public sealed record ContractSignedEntry(long Tick, ContractId Contract, ActorId Taker, Credits Price)
    : JournalEntry(Tick), IContractEntry;

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
    : JournalEntry(Tick), IContractEntry;

/// <summary>A Haul settled: what arrived, how late, and what the terms took off the fee (lateness, quality, goods lost).</summary>
public sealed record HaulDeliveredEntry(
    long Tick,
    ContractId Contract,
    ActorId Hauler,
    decimal Quantity,
    decimal Quality,
    long TicksLate,
    Credits Fee,
    Credits LateFee,
    Credits QualityRefund,
    Credits Liability)
    : JournalEntry(Tick), IContractEntry;

/// <summary>Outside the Accord, the payer could not pay at handover: a default. A seller keeps its goods.</summary>
public sealed record PaymentDefaultedEntry(long Tick, ContractId Contract, ActorId Payer, ActorId Payee)
    : JournalEntry(Tick), IContractEntry;

/// <summary>Why a performer failed by the terms.</summary>
public enum FailureReason
{
    /// <summary>The goods were lost in transit.</summary>
    CargoLost,

    /// <summary>The goods never came aboard: the purchase behind the delivery fell through.</summary>
    NotDelivered,

    /// <summary>Short, under terms that treat a short delivery as a failure.</summary>
    ShortDelivery,

    /// <summary>Below the warranty's floor, and the receiver rejected them on inspection.</summary>
    Rejected,
}

/// <summary>
/// A Sale or Haul failed by its terms: its performer's bond went to the payer and the payer's escrow came back.
/// <see cref="Delivered"/> and <see cref="Quality"/> are what arrived, for a short delivery or a rejection.
/// </summary>
public sealed record DeliveryFailedEntry(long Tick, ContractId Contract, ActorId Performer, FailureReason Reason, decimal Delivered = 0m, decimal Quality = 1m)
    : JournalEntry(Tick), IContractEntry;

/// <summary>
/// A signed contract's grace ran out with it not performed (design §3.4): it is in default, and its payer may cancel
/// for default or keep waiting while the late fee runs to its cap.
/// </summary>
public sealed record ContractInDefaultEntry(long Tick, ContractId Contract, ActorId Performer, ActorId Payer)
    : JournalEntry(Tick), IContractEntry;

/// <summary>
/// A signed contract was cancelled (design §16.2): for default by its payer (<see cref="Paid"/> is the bond it took),
/// or at will by either party (<see cref="Paid"/> is the break fee it paid).
/// </summary>
public sealed record ContractCancelledEntry(long Tick, ContractId Contract, ActorId By, CancelReason Reason, Credits Paid)
    : JournalEntry(Tick), IContractEntry;

/// <summary>Nobody took an open contract before its deadline.</summary>
public sealed record ContractExpiredEntry(long Tick, ContractId Contract)
    : JournalEntry(Tick), IContractEntry;

/// <summary>The poster took an open contract down: it no longer needs it, or it is posting new terms.</summary>
public sealed record ContractWithdrawnEntry(long Tick, ContractId Contract)
    : JournalEntry(Tick), IContractEntry;

/// <summary>An entry about a haggle (design §3.6). Its default cause is the haggle's previous entry, not the contract's.</summary>
public interface INegotiationEntry : IContractEntry
{
    NegotiationId Negotiation { get; }
}

/// <summary>
/// A side made a counter-offer in a haggle: its terms and its round. A reply carries how long it took and how it read
/// (<see cref="ReplyHours"/>, <see cref="Tell"/>): both are tells (design §3.6). The player's own offers carry neither.
/// </summary>
public sealed record OfferMadeEntry(
    long Tick, ContractId Contract, NegotiationId Negotiation, ActorId By, ActorId To, Offer Offer, int Round, long? ReplyHours = null, Tell Tell = Tell.None)
    : JournalEntry(Tick), INegotiationEntry;

/// <summary>A side accepted the other's offer in a haggle; the contract is signed on it next.</summary>
public sealed record OfferAcceptedEntry(
    long Tick, ContractId Contract, NegotiationId Negotiation, ActorId By, ActorId To, Offer Offer, long? ReplyHours = null, Tell Tell = Tell.None)
    : JournalEntry(Tick), INegotiationEntry;

/// <summary>A haggle ended without a deal: a side walked away, the rounds ran out, a side declined, or the performer no longer could.</summary>
public sealed record NegotiationEndedEntry(
    long Tick, ContractId Contract, NegotiationId Negotiation, ActorId By, ActorId To, NegotiationEnd End, long? ReplyHours = null)
    : JournalEntry(Tick), INegotiationEntry;

/// <summary>Someone rented storage from a station operator.</summary>
public sealed record StorageOpenedEntry(long Tick, ContractId Contract, ActorId Renter)
    : JournalEntry(Tick), IContractEntry;

/// <summary>The last goods left a store, which ended its contract.</summary>
public sealed record StorageClosedEntry(long Tick, ContractId Contract, ActorId Renter, Credits RentPaid)
    : JournalEntry(Tick), IContractEntry;

/// <summary>A transit incident struck a laden ship, on the job for <see cref="Contract"/>.</summary>
public sealed record IncidentEntry(long Tick, ContractId Contract, ActorId Hauler, TransitIncident Incident)
    : JournalEntry(Tick), IContractEntry;

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

    /// <summary>Keep an entry, stamped with its sequence number, and return it as kept.</summary>
    public JournalEntry Record(JournalEntry entry)
    {
        entry = entry with { Seq = TotalRecorded };
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

        return entry;
    }

    /// <summary>The entry with sequence number <paramref name="seq"/>, if the ring still keeps it.</summary>
    public JournalEntry? Find(long seq)
    {
        long first = TotalRecorded - _count;
        return seq < first || seq >= TotalRecorded ? null : Entries[(int)(seq - first)];
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
