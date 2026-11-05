using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>A decision the terms hand to a party (design §3.4): an AI actor makes it by utility, the player at a hard pause.</summary>
public enum DecisionKind
{
    /// <summary>The counterparty is in default: cancel (escrow back, its bond taken) or keep waiting.</summary>
    CancelForDefault,

    /// <summary>Goods handed over below the warranty's floor, or a ship built outside the Build's policy: accept with the refund, or reject.</summary>
    Inspection,

    /// <summary>A yard can't get a part for a Build in time (design §3.4): more time, a substitute, or the refund.</summary>
    SupplyFailure,
}

/// <summary>An answer to a decision.</summary>
public enum DecisionChoice
{
    /// <summary>Cancel for default.</summary>
    Cancel,

    /// <summary>Keep waiting; the right to cancel stays.</summary>
    Wait,

    /// <summary>Accept the goods, with the warranty's refund.</summary>
    Accept,

    /// <summary>Reject the goods: they stay the seller's, and the sale fails.</summary>
    Reject,

    /// <summary>A supply failure: wait longer, with compensation.</summary>
    Extend,

    /// <summary>A supply failure: take a part a grade lower, at a discount, sooner.</summary>
    Substitute,

    /// <summary>A supply failure: cancel, with the deposit and escrow back and compensation.</summary>
    TakeRefund,
}

/// <summary>
/// A decision waiting on the player. While one is pending the world does not move (design §8.3: the game pauses
/// for it), so the goods and contracts it concerns stay exactly as they were when it was raised.
/// <see cref="Entry"/> is the journal entry that raised it; for an inspection, <see cref="Quantity"/> and
/// <see cref="Quality"/> are the goods as handed over.
/// </summary>
public sealed record PlayerDecision(DecisionKind Kind, ContractId Contract, long RaisedTick, long Entry, decimal Quantity = 0m, decimal Quality = 1m)
{
    /// <summary>The choices it offers, in the order the interface shows them.</summary>
    public DecisionChoice[] Choices => Kind switch
    {
        DecisionKind.CancelForDefault => new[] { DecisionChoice.Cancel, DecisionChoice.Wait },
        DecisionKind.SupplyFailure => new[] { DecisionChoice.Extend, DecisionChoice.Substitute, DecisionChoice.TakeRefund },
        _ => new[] { DecisionChoice.Accept, DecisionChoice.Reject },
    };
}

/// <summary>
/// The failure paths of design §16.3 that the soak harness counts, so a long run shows each one happening. One
/// contract can count under several (late and short; in default and then cancelled).
/// </summary>
public enum FailureKind
{
    /// <summary>Settled after its deadline.</summary>
    LateDelivery,

    /// <summary>Its grace ran out unperformed.</summary>
    InDefault,

    /// <summary>The payer cancelled for default.</summary>
    CancelledForDefault,

    /// <summary>A party walked away and paid the break fee.</summary>
    CancelledAtWill,

    /// <summary>A short delivery paid pro rata.</summary>
    ShortProRata,

    /// <summary>A short delivery treated as a failure.</summary>
    ShortFailed,

    /// <summary>Goods below the warranty's minimum: part of the price back.</summary>
    QualityRefund,

    /// <summary>Goods below the warranty's floor, rejected.</summary>
    Rejected,

    /// <summary>The whole cargo lost.</summary>
    CargoLost,

    /// <summary>A carrier paid for goods it lost.</summary>
    CargoLiability,

    /// <summary>The payer didn't pay (outside the Accord).</summary>
    PayerDefault,

    /// <summary>Nobody took a post before its deadline.</summary>
    Expired,

    /// <summary>A yard couldn't get a part in time and proposed remedies (design §3.4).</summary>
    SupplyFailure,

    /// <summary>A ship handed over with parts below the grade ordered, priced down by the Build's policy.</summary>
    Substituted,

    /// <summary>A Build cancelled by agreement after a supply-failure proposal: refund and compensation.</summary>
    CancelledOnSupplyFailure,
}
