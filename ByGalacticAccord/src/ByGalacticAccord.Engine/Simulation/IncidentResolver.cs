using System;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Random;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>The abstract, actor-free outcomes of a Transit Incident (§6.3).</summary>
public enum TransitIncidentKind
{
    MinorDelay,
    CargoDamage,
    PartialLoss,
    CreditCost,
    TotalLoss,
}

/// <summary>The resolved effect of an incident, ready to apply to a haul.</summary>
public readonly record struct TransitIncident(
    TransitIncidentKind Kind,
    long DelayTicks,
    decimal QualityLoss,
    decimal QuantityLossFraction,
    Credits CreditCost,
    string Description);

/// <summary>
/// Rolls a transit incident's outcome (design §8.4, §16.7). One roll picks the kind from a table (a delay, cargo damage, a partial
/// loss, an unplanned cost, a total loss, mildest first); a route's danger shifts the table toward its severe end
/// (<see cref="Bound"/>), and the ship's toughness scales the damage (<see cref="ShipStats.IncidentSeverity"/>: armour milder, a
/// courier frame worse). How often incidents happen is the drive's reliability and the throttle's (<see cref="Throttles"/>). This
/// is the very hook pirate interdiction will use (design §22.2).
/// </summary>
public static class IncidentResolver
{
    // The outcome table on a route with no danger: one roll, and each kind takes the rolls below its bound.
    private const decimal DelayBelow = 0.40m;
    private const decimal DamageBelow = 0.66m;
    private const decimal PartialLossBelow = 0.85m;
    private const decimal CreditCostBelow = 0.96m;

    /// <summary>
    /// The share of incidents on a route of <paramref name="danger"/> that turn out as <paramref name="kind"/>, so the interface can
    /// quote the table and an AI weigh it. <paramref name="shift"/> is <see cref="SimulationConfig.IncidentDangerShift"/>.
    /// </summary>
    public static decimal Share(TransitIncidentKind kind, decimal danger, decimal shift) => kind switch
    {
        TransitIncidentKind.MinorDelay => Bound(DelayBelow, danger, shift),
        TransitIncidentKind.CargoDamage => Bound(DamageBelow, danger, shift) - Bound(DelayBelow, danger, shift),
        TransitIncidentKind.PartialLoss => Bound(PartialLossBelow, danger, shift) - Bound(DamageBelow, danger, shift),
        TransitIncidentKind.CreditCost => Bound(CreditCostBelow, danger, shift) - Bound(PartialLossBelow, danger, shift),
        TransitIncidentKind.TotalLoss => 1m - Bound(CreditCostBelow, danger, shift),
        _ => 0m,
    };

    /// <summary>
    /// A bound of the table on a route of <paramref name="danger"/>: b − k·b·(1 − b), with k = shift × danger at most 1. Every bound
    /// moves down, the middle ones most, so the rolls fall toward the severe end: on the red Rust Belt run (danger 0.42, k 0.84) a
    /// delay is half as likely as on a safe route and a total loss nearly twice (7.2% of incidents against 4%).
    /// </summary>
    public static decimal Bound(decimal bound, decimal danger, decimal shift)
    {
        decimal k = Math.Clamp(shift * Math.Clamp(danger, 0m, 1m), 0m, 1m);
        return bound - (k * bound * (1m - bound));
    }

    public static TransitIncident Resolve(DeterministicRandom rng, decimal dangerRating, ShipStats ship, decimal shift)
    {
        decimal severity = ship.IncidentSeverity;
        decimal roll = rng.NextDecimal();
        decimal danger = Math.Clamp(dangerRating, 0m, 1m);

        if (roll < Bound(DelayBelow, danger, shift))
        {
            long delay = (long)(rng.NextDecimal(4m, 18m) * (1m + danger) * severity);
            return new TransitIncident(TransitIncidentKind.MinorDelay, delay, 0m, 0m, Credits.Zero,
                $"navigational delay (+{delay} ticks)");
        }

        if (roll < Bound(DamageBelow, danger, shift))
        {
            decimal qLoss = rng.NextDecimal(0.08m, 0.22m) * severity;
            return new TransitIncident(TransitIncidentKind.CargoDamage, 0, qLoss, 0m, Credits.Zero,
                $"cargo damage (-{qLoss:P0} quality)");
        }

        if (roll < Bound(PartialLossBelow, danger, shift))
        {
            decimal qtyLoss = rng.NextDecimal(0.10m, 0.30m) * severity;
            return new TransitIncident(TransitIncidentKind.PartialLoss, 0, 0m, qtyLoss, Credits.Zero,
                $"partial cargo loss (-{qtyLoss:P0})");
        }

        if (roll < Bound(CreditCostBelow, danger, shift))
        {
            decimal cost = rng.NextDecimal(40m, 160m) * (1m + danger) * severity;
            return new TransitIncident(TransitIncidentKind.CreditCost, 0, 0m, 0m, Credits.Of(cost),
                $"unplanned costs ({Credits.Of(cost)})");
        }

        // Rare, and rarest on a safe run: the whole cargo is lost, and a breach is coming.
        return new TransitIncident(TransitIncidentKind.TotalLoss, 0, 0m, 1m, Credits.Zero, "total cargo loss");
    }
}
