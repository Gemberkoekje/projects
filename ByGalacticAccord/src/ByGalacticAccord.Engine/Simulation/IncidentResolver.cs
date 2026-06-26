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
/// Rolls a Transit Incident outcome (§6.3). Severity is weighted by route danger and modulated by
/// ship type — the Armed Freighter shrugs incidents off, the Fast Courier's thin hull suffers worse
/// damage. This is the very same hook a pirate-driven interdiction will use in Slice 3 (§6.3.1).
/// </summary>
public static class IncidentResolver
{
    public static TransitIncident Resolve(DeterministicRandom rng, decimal dangerRating, ShipClassInfo ship)
    {
        decimal severity = ship.IncidentSeverityModifier;
        decimal roll = rng.NextDecimal();

        // Higher danger pushes the roll toward the nastier end of the table.
        decimal danger = Math.Clamp(dangerRating, 0m, 1m);

        if (roll < 0.40m)
        {
            long delay = (long)(rng.NextDecimal(4m, 18m) * (1m + danger) * severity);
            return new TransitIncident(TransitIncidentKind.MinorDelay, delay, 0m, 0m, Credits.Zero,
                $"navigational delay (+{delay} ticks)");
        }

        if (roll < 0.66m)
        {
            decimal qLoss = rng.NextDecimal(0.08m, 0.22m) * severity;
            return new TransitIncident(TransitIncidentKind.CargoDamage, 0, qLoss, 0m, Credits.Zero,
                $"cargo damage (-{qLoss:P0} quality)");
        }

        if (roll < 0.85m)
        {
            decimal qtyLoss = rng.NextDecimal(0.10m, 0.30m) * severity;
            return new TransitIncident(TransitIncidentKind.PartialLoss, 0, 0m, qtyLoss, Credits.Zero,
                $"partial cargo loss (-{qtyLoss:P0})");
        }

        if (roll < 0.96m)
        {
            decimal cost = rng.NextDecimal(40m, 160m) * (1m + danger) * severity;
            return new TransitIncident(TransitIncidentKind.CreditCost, 0, 0m, 0m, Credits.Of(cost),
                $"unplanned costs ({Credits.Of(cost)})");
        }

        // Rare on a high-danger run: the whole cargo is lost — a breach is coming.
        return new TransitIncident(TransitIncidentKind.TotalLoss, 0, 0m, 1m, Credits.Zero, "total cargo loss");
    }
}
