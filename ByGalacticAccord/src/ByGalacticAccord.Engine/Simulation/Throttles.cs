using System;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// The throttle's rules (design §16.7): pushing multiplies the laden leg's speed by <see cref="SimulationConfig.PushSpeedFactor"/> and
/// its incident chance by <see cref="SimulationConfig.PushIncidentFactor"/>, or <see cref="SimulationConfig.PushHotIncidentFactor"/> for
/// a drive that runs hot (<see cref="ShipStats.RunsHot"/>). Every laden trip's time and incident odds come from here: the engine's
/// events, the AI's scoring and the interface's estimates alike.
/// </summary>
public static class Throttles
{
    /// <summary>What the throttle does to speed: ×1.25 pushing, else ×1.</summary>
    public static decimal SpeedFactor(SimulationConfig config, Throttle throttle) => throttle == Throttle.Push ? config.PushSpeedFactor : 1m;

    /// <summary>What the throttle does to the incident chance: ×1.5 pushing, ×2 pushing a drive that runs hot, else ×1.</summary>
    public static decimal IncidentFactor(SimulationConfig config, Throttle throttle, ShipStats ship) =>
        throttle != Throttle.Push ? 1m : ship.RunsHot ? config.PushHotIncidentFactor : config.PushIncidentFactor;

    /// <summary>How long a laden leg of <paramref name="distanceTicks"/> at standard speed takes this ship at this throttle (at least an hour).</summary>
    public static long LadenTicks(SimulationConfig config, long distanceTicks, ShipStats ship, Throttle throttle) =>
        Math.Max(1, PriceModel.TravelTicks(distanceTicks, ship.Speed * SpeedFactor(config, throttle)));

    /// <summary>
    /// The chance of an incident on a laden trip (design §8.4, §16.7): the route's danger (its worst leg), scaled, times the ship's
    /// incident chance (one over its reliability), times the throttle's factor.
    /// </summary>
    public static decimal IncidentOdds(SimulationConfig config, decimal danger, ShipStats ship, Throttle throttle) =>
        Math.Clamp(config.IncidentProbabilityScale * danger * ship.IncidentChance * IncidentFactor(config, throttle, ship), 0m, 1m);
}
