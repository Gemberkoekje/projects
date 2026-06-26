using System;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// The personality vector that biases an actor's decisions (§4.5.2). These are not clever AI —
/// they are multipliers on decision thresholds. The same vector drives both AI actors and,
/// where the player declines to decide manually, the player's defaults.
/// </summary>
public readonly record struct Personality(
    decimal RiskTolerance,
    decimal LiquidityPreference,
    decimal QualityBias,
    decimal GrowthAmbition,
    decimal TrustThreshold,
    decimal ConcessionRate)
{
    /// <summary>A neutral middle-of-the-road operator.</summary>
    public static Personality Balanced => new(0.5m, 0.5m, 0.5m, 0.5m, 0.5m, 0.4m);

    public Personality Clamped() => new(
        Math.Clamp(RiskTolerance, 0m, 1m),
        Math.Clamp(LiquidityPreference, 0m, 1m),
        Math.Clamp(QualityBias, 0m, 1m),
        Math.Clamp(GrowthAmbition, 0m, 1m),
        Math.Clamp(TrustThreshold, 0m, 1m),
        Math.Clamp(ConcessionRate, 0.05m, 0.95m));
}
