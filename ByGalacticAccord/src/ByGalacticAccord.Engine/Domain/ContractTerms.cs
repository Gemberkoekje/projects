namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// The three ready-made sets of terms (design §3.4): strict terms protect the payer and cost more, lenient ones are
/// cheaper and riskier. A poster picks one; haggling (S5) can move single terms.
/// </summary>
public enum TermPackage
{
    Strict,
    Standard,
    Lenient,
}

/// <summary>What a short delivery is (design §3.4): accepted and paid pro rata, or treated as a failure.</summary>
public enum PartialDelivery
{
    ProRata,
    Failure,
}

/// <summary>Why a signed contract was cancelled (design §16.2).</summary>
public enum CancelReason
{
    /// <summary>The wronged party cancelled once the grace period had passed: escrow back, the defaulter's bond to it.</summary>
    ForDefault,

    /// <summary>A party walked away and paid the break fee.</summary>
    AtWill,
}

/// <summary>Who cancelled a contract, why, and what it cost them (the bond forfeited, or the break fee paid).</summary>
public sealed record Cancellation(ActorId By, CancelReason Reason, long Tick, Credits Paid);

/// <summary>
/// The late fee (design §3.4): paid by the performer per hour late, from the deadline, up to a cap. The cap is the
/// performer's bond (design §16.4).
/// </summary>
public sealed record PenaltyOnLateDelivery(decimal PenaltyPerTickLate, Credits MaxPenalty)
{
    public Credits PenaltyFor(long ticksLate)
    {
        if (ticksLate <= 0)
        {
            return Credits.Zero;
        }

        decimal raw = PenaltyPerTickLate * ticksLate;
        return raw >= MaxPenalty.Amount ? MaxPenalty : Credits.Of(raw);
    }

    /// <summary>How many hours late it takes for the fee to reach its cap.</summary>
    public long HoursToCap => PenaltyPerTickLate <= 0m ? 0 : (long)System.Math.Ceiling(MaxPenalty.Amount / PenaltyPerTickLate);
}

/// <summary>
/// The quality warranty (design §3.4): below <see cref="MinQuality"/> part of the price goes back, scaled by how far
/// short the goods fell; below <see cref="RejectBelow"/>, if there is a floor, the receiver may reject them on inspection.
/// </summary>
public sealed record QualityWarranty(decimal MinQuality, decimal MaxRefundFraction, decimal? RejectBelow = null)
{
    /// <summary>Fraction of the price refunded for sub-threshold quality (0 if quality is fine).</summary>
    public decimal RefundFraction(decimal deliveredQuality)
    {
        if (deliveredQuality >= MinQuality || MinQuality <= 0m)
        {
            return 0m;
        }

        decimal shortfall = (MinQuality - deliveredQuality) / MinQuality;
        return System.Math.Clamp(shortfall, 0m, 1m) * MaxRefundFraction;
    }

    /// <summary>Whether goods of this quality may be rejected.</summary>
    public bool Rejectable(decimal deliveredQuality) => RejectBelow is { } floor && deliveredQuality < floor;
}

/// <summary>
/// Cargo liability (design §3.4): what a carrier owes the shipper for goods lost on the way, per unit at their declared
/// value, up to a cap.
/// </summary>
public sealed record CargoLiability(Credits PerUnit, Credits Cap)
{
    public Credits For(decimal unitsLost) =>
        unitsLost <= 0m ? Credits.Zero : Credits.Of(System.Math.Min(Cap.Amount, PerUnit.Amount * unitsLost));
}

/// <summary>
/// A contract's terms (design §3.4): what happens when it goes wrong. They are part of the contract, visible before
/// signing: the grace after the deadline before the payer may cancel for default, the late fee and its cap (the bond),
/// the break fee for walking away, what a short delivery is, the quality warranty, and a haul's cargo liability.
/// </summary>
public sealed record ContractTerms(
    TermPackage Package,
    long GraceHours,
    PenaltyOnLateDelivery? LateFee,
    Credits BreakFee,
    PartialDelivery Partial,
    QualityWarranty? Warranty,
    CargoLiability? Liability)
{
    /// <summary>Terms with no remedies: a station's storage, which runs until it is emptied.</summary>
    public static ContractTerms None { get; } = new(TermPackage.Standard, 0, null, Credits.Zero, PartialDelivery.ProRata, null, null);

    /// <summary>The bond a fully solvent performer posts in Accord space: the late fee's cap (design §16.4).</summary>
    public Credits Bond => LateFee?.MaxPenalty ?? Credits.Zero;
}
