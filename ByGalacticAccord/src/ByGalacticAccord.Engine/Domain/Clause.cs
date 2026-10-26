namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// Clause taxonomy (§4.1.5). Slice-0 clauses are <see cref="PenaltyOnLateDelivery"/> and
/// <see cref="QualityWarranty"/>; the rest are enumerated so the schema is whole and later
/// slices (bonding, insurance, standing renewal) slot in without reshaping the model.
/// </summary>
public enum ClauseType
{
    PenaltyOnLateDelivery,
    QualityWarranty,
    EscapeHatch,
    InspectionRight,
    InsuranceRider,
    BondRequirement,
    StandingRenewal,
}

/// <summary>
/// A typed, evaluable contract term (Trigger + Effect, §4.1.5). This is the mechanism that
/// makes "everything is a contract" structural rather than slogan: penalties, warranties, and
/// later insurance/bonding are all clauses, not special-cased code paths.
/// </summary>
public abstract record ContractClause(ClauseType Type);

/// <summary>
/// Trigger: delivery tick &gt; DeadlineTick. Effect: a fee per hour late from the performer to the payer,
/// taken from the performer's bond in Accord space. <see cref="MaxPenalty"/> sizes that bond.
/// </summary>
public sealed record PenaltyOnLateDelivery(decimal PenaltyPerTickLate, Credits MaxPenalty)
    : ContractClause(ClauseType.PenaltyOnLateDelivery)
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
/// Trigger: delivered quality &lt; threshold. Effect: part of the price goes back to the payer, scaled by
/// how far below threshold the goods arrived.
/// </summary>
public sealed record QualityWarranty(decimal MinQuality, decimal MaxRefundFraction)
    : ContractClause(ClauseType.QualityWarranty)
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
}
