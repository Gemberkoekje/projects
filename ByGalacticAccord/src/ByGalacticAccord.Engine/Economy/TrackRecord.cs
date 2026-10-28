using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Economy;

/// <summary>
/// How an actor's contracts ended (design §7.3, §9): as the side that performs (delivered on time, late or short; failed;
/// cancelled for default by its payer; walked away from) and as the side that pays (paid; defaulted; walked away from). A
/// contract counts once it has ended.
/// </summary>
public sealed class TrackRecord
{
    public int OnTime { get; private set; }

    public int Late { get; private set; }

    /// <summary>Delivered short and paid pro rata (on time or late as well).</summary>
    public int Short { get; private set; }

    /// <summary>Failed by its terms: the goods lost, a short delivery a strict term treats as failure, goods rejected.</summary>
    public int Failed { get; private set; }

    /// <summary>Not performed by the end of its grace, and its payer cancelled.</summary>
    public int CancelledForDefault { get; private set; }

    /// <summary>It walked away from contracts it was to perform, paying the break fee.</summary>
    public int WalkedAway { get; private set; }

    public int Paid { get; private set; }

    /// <summary>Outside the Accord, it couldn't pay at handover.</summary>
    public int Defaulted { get; private set; }

    /// <summary>It walked away from contracts it was to pay for, paying the break fee.</summary>
    public int PayerWalkedAway { get; private set; }

    /// <summary>Contracts it performed or failed to: delivered (on time or late), failed, cancelled for default, walked away from.</summary>
    public int AsPerformer => OnTime + Late + Failed + CancelledForDefault + WalkedAway;

    public int AsPayer => Paid + Defaulted + PayerWalkedAway;

    public int Total => AsPerformer + AsPayer;

    internal void Delivered(long ticksLate, bool shortDelivery)
    {
        if (ticksLate > 0)
        {
            Late++;
        }
        else
        {
            OnTime++;
        }

        if (shortDelivery)
        {
            Short++;
        }
    }

    internal void Fail() => Failed++;

    internal void CancelledForItsDefault() => CancelledForDefault++;

    internal void WalkedAwayAsPerformer() => WalkedAway++;

    internal void PaidInFull() => Paid++;

    internal void Default() => Defaulted++;

    internal void WalkedAwayAsPayer() => PayerWalkedAway++;
}

/// <summary>
/// Records of how contracts ended (design §7.3, D25). The Accord's public record covers every contract performed in Accord
/// space, per actor, and anyone can look it up from anywhere; the frontier leaves none. Each actor also remembers its own
/// dealings with each counterparty, frontier ones too: what the other side did on contracts with it.
/// </summary>
public sealed class Records
{
    private readonly Dictionary<ActorId, TrackRecord> _public = new();
    private readonly Dictionary<(ActorId Observer, ActorId Other), TrackRecord> _dealings = new();

    /// <summary>An actor's public record: its Accord contracts. Empty for an actor with none.</summary>
    public TrackRecord PublicRecordOf(ActorId actor) => _public.TryGetValue(actor, out TrackRecord? record) ? record : Empty;

    /// <summary>What <paramref name="other"/> did on contracts with <paramref name="observer"/>, as the observer saw it.</summary>
    public TrackRecord DealingsWith(ActorId observer, ActorId other) =>
        _dealings.TryGetValue((observer, other), out TrackRecord? record) ? record : Empty;

    private static TrackRecord Empty { get; } = new();

    /// <summary>A contract was delivered (in full, or short and paid pro rata): its performer delivered and its payer paid.</summary>
    internal void Delivered(Contract contract, long ticksLate, bool shortDelivery)
    {
        Each(contract, contract.Performer, r => r.Delivered(ticksLate, shortDelivery));
        Each(contract, contract.Payer, r => r.PaidInFull());
    }

    /// <summary>Its performer failed by the terms.</summary>
    internal void Failed(Contract contract) => Each(contract, contract.Performer, r => r.Fail());

    /// <summary>Its payer cancelled it for default: the performer's.</summary>
    internal void CancelledForDefault(Contract contract) => Each(contract, contract.Performer, r => r.CancelledForItsDefault());

    /// <summary>A party walked away from it.</summary>
    internal void WalkedAway(Contract contract, ActorId by)
    {
        if (by == contract.Payer)
        {
            Each(contract, by, r => r.WalkedAwayAsPayer());
        }
        else
        {
            Each(contract, by, r => r.WalkedAwayAsPerformer());
        }
    }

    /// <summary>Its payer didn't pay (outside the Accord, so only the payee's own dealings remember it).</summary>
    internal void Defaulted(Contract contract) => Each(contract, contract.Payer, r => r.Default());

    // The party's public record, if the contract was performed in Accord space, and its counterparty's memory of it.
    private void Each(Contract contract, ActorId? party, System.Action<TrackRecord> note)
    {
        if (party is not { } actor || contract is StorageContract)
        {
            return;
        }

        if (contract.UsesEscrow)
        {
            note(Get(_public, actor));
        }

        if (contract.Other(actor) is { } observer)
        {
            note(Get(_dealings, (observer, actor)));
        }
    }

    private static TrackRecord Get<TKey>(Dictionary<TKey, TrackRecord> records, TKey key)
        where TKey : notnull
    {
        if (!records.TryGetValue(key, out TrackRecord? record))
        {
            record = new TrackRecord();
            records[key] = record;
        }

        return record;
    }
}
