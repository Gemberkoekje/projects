using System.Text.Json;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Which plan a ship works for, by what it can do (PLAN.md slice 6.4), so every plan sees the fleet alike:
/// <list type="bullet">
///   <item>with the survey plan on, a ship that can survey surveys, and nothing else (D20). With the spare-time plan
///   on too (slice 6.8), one that can also mine or siphon (the command ship) trades when it has nothing to survey
///   (D34), and with no trade either mines or siphons whatever it can, which a survey or a trade interrupts
///   (D35–D37);</item>
///   <item>a ship that can mine, and doesn't survey, mines: the contract first, every free miner (D23), then
///   the mining plan's trips. Only a miner neither has work for may trade;</item>
///   <item>a ship that can siphon, and can neither mine nor survey, siphons (slice 6.7): the siphon plan's
///   trips. Only a siphoner the siphon plan has no work for may trade. The command ship siphons only in its
///   spare time;</item>
///   <item>an explorer explores (slice 6.30), and trades when the explore plan has no system for it (D102), though it
///   carries a gas siphon;</item>
///   <item>any other ship with a hold and a tank trades.</item>
/// </list>
/// </summary>
public static class FleetRoles
{
    /// <summary>The ship the explore plan buys (PLAN.md slice 6.30, D98).</summary>
    public const string ExplorerShipType = "SHIP_EXPLORER";

    /// <summary>
    /// Whether the ship is an explorer (PLAN.md slice 6.30, D98): the type it is cached with, <c>SHIP_EXPLORER</c> when bought
    /// and its registration role <c>EXPLORER</c> after startup sync, or an explorer's frame. The explore plan flies it; in
    /// between it only trades (D102).
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for an explorer.</returns>
    public static bool IsExplorer(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.ShipType.Equals(ExplorerShipType, StringComparison.OrdinalIgnoreCase)
            || ship.ShipType.Equals("EXPLORER", StringComparison.OrdinalIgnoreCase)
            || (ship.FrameJson ?? string.Empty).Contains("\"FRAME_EXPLORER\"", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the ship is a surveyor, which surveys before anything else (D20); with the spare-time plan on, it trades
    /// or gathers when it has nothing to survey (slice 6.8, D34).
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="surveyPlanOn">Whether the survey plan is switched on.</param>
    /// <returns>True for a ship with a surveyor mount while the survey plan is on.</returns>
    public static bool IsSurveyor(ShipModel ship, bool surveyPlanOn)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return surveyPlanOn && ship.HasSurveyEquipment;
    }

    /// <summary>Whether the ship mines: a mining laser, a hold and a tank, and not a surveyor.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="surveyPlanOn">Whether the survey plan is switched on.</param>
    /// <returns>True for a ship the contract and mining plans may give work.</returns>
    public static bool IsMiner(ShipModel ship, bool surveyPlanOn)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.IsMiningCapable && !IsSurveyor(ship, surveyPlanOn);
    }

    /// <summary>
    /// Whether the ship siphons (slice 6.7): a gas siphon, a hold and a tank, and nothing to mine or survey with,
    /// whichever plans are on. That is a siphon drone: a ship that can also mine mines, or surveys (D20) and siphons
    /// only in its spare time (slice 6.8).
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship the siphon plan may give work.</returns>
    public static bool IsSiphoner(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.HasGasSiphonEquipment
            && ship.IsTradingCapable
            && !ship.HasMiningEquipment
            && !ship.HasSurveyEquipment
            && !IsExplorer(ship);
    }

    /// <summary>
    /// Whether the ship mines or siphons in its spare time (slice 6.8): a surveyor (<see cref="IsSurveyor"/>) with a
    /// hold, a tank, and a mining laser or a gas siphon. That is the command ship, while the survey plan is on;
    /// with it off, the command ship is a miner, and the mining plan gives it work.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="surveyPlanOn">Whether the survey plan is switched on.</param>
    /// <returns>True for a ship the spare-time plan may give a trip.</returns>
    public static bool GathersInSpareTime(ShipModel ship, bool surveyPlanOn)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return IsSurveyor(ship, surveyPlanOn)
            && ship.IsTradingCapable
            && (HasMiningLaser(ship) || ship.HasGasSiphonEquipment);
    }

    /// <summary>
    /// Whether the ship has a mining laser. <see cref="ShipModel.HasMiningEquipment"/> also counts a surveyor
    /// mount, which can't extract; a bought mining drone counts by its type, as its mounts are recorded only at
    /// the next startup sync.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship that can extract at an asteroid.</returns>
    public static bool HasMiningLaser(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.ShipType.Equals("SHIP_MINING_DRONE", StringComparison.OrdinalIgnoreCase)
            || ship.ShipType.Equals("SHIP_ORE_HOUND", StringComparison.OrdinalIgnoreCase)
            || (ship.MountSymbols ?? []).Any(mount => mount.Contains("MINING_LASER", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether the ship is a mining drone (slice 6.10b, D48): it can mine (<see cref="CanMine"/>) and can't survey, so not
    /// the command ship, whichever role it has. The mining plan keeps one per SCARCE or LIMITED ore.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a mining drone, or another ship that mines and doesn't survey.</returns>
    public static bool IsMiningDrone(ShipModel ship) => CanMine(ship) && !CanSurvey(ship);

    /// <summary>
    /// Whether the ship is a cargo ship, as the trading plan buys them (D21): a hold and a tank, and nothing
    /// to mine, siphon or survey with. Judged by what it carries rather than its cached type, which startup
    /// sync replaces with the registration role (B25). An explorer is none (slice 6.30): until startup sync records
    /// its gas siphon, a bought one shows only its hold and tank.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a shuttle or hauler.</returns>
    public static bool IsCargoShip(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.IsTradingCapable
            && !ship.HasMiningEquipment
            && !ship.HasGasSiphonEquipment
            && !ship.HasSurveyEquipment
            && !IsExplorer(ship);
    }

    /// <summary>
    /// Whether the ship is a probe, which the probe plan flies and nothing else uses (D29): a probe frame, or
    /// the type a probe is cached with, <c>SHIP_PROBE</c> when bought and its registration role
    /// <c>SATELLITE</c> after startup sync. The starting probe is one (B25).
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a probe.</returns>
    public static bool IsProbe(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.ShipType.Equals("SHIP_PROBE", StringComparison.OrdinalIgnoreCase)
            || ship.ShipType.Equals("SATELLITE", StringComparison.OrdinalIgnoreCase)
            || (ship.FrameJson ?? string.Empty).Contains("\"FRAME_PROBE\"", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the ship can only survey (<see cref="PotentialRoles"/>): a surveyor and no hold, such as a bought
    /// <c>SHIP_SURVEYOR</c> (D47). With nothing else to do, it surveys on once every ore has its stock (D52).
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship whose one role is surveying.</returns>
    public static bool CanOnlySurvey(ShipModel ship) => PotentialRoles(ship) is [FleetRole.Survey];

    /// <summary>
    /// The roles a ship could take, by what it carries (slice 6.9, D38), whichever plans are on: survey with a
    /// surveyor; mine with a mining laser, a hold and a tank; siphon with a gas siphon, a hold and a tank; trade with
    /// a hold and a tank; construct with a hold and a tank, unless it is a drone (slice 6.6, D65). A probe has none: the
    /// probe plan flies it. An explorer trades and nothing else (slice 6.30, D102): asked on 2026-10-06, "Explorers can trade
    /// with 40 cargo space, so they can trade at the location they are at until a new unexplored location comes up."
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>Its potential roles, in the order survey, mine, siphon, trade, construct.</returns>
    public static IReadOnlyList<FleetRole> PotentialRoles(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);

        var roles = new List<FleetRole>();
        if (IsProbe(ship))
        {
            return roles;
        }

        if (IsExplorer(ship))
        {
            return ship.IsTradingCapable ? [FleetRole.Trade] : roles;
        }

        if (CanSurvey(ship))
        {
            roles.Add(FleetRole.Survey);
        }

        if (CanMine(ship))
        {
            roles.Add(FleetRole.Mine);
        }

        if (CanSiphon(ship))
        {
            roles.Add(FleetRole.Siphon);
        }

        if (ship.IsTradingCapable)
        {
            roles.Add(FleetRole.Trade);
        }

        if (CanConstruct(ship))
        {
            roles.Add(FleetRole.Construct);
        }

        return roles;
    }

    /// <summary>
    /// Whether the ship can build the jump gate (slice 6.6, D65): a hold and a tank, to buy the materials and carry them
    /// there, and no drone, which gathers first (D58). The command ship and the cargo ships can; probes, drones, survey
    /// ships and explorers (D102) can't.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship that can take the construction role.</returns>
    public static bool CanConstruct(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.IsTradingCapable && !IsProbe(ship) && !IsMiningDrone(ship) && !IsSiphoner(ship) && !IsExplorer(ship);
    }

    /// <summary>
    /// Whether the ship can survey: a surveyor mount, or a bought <c>SHIP_SURVEYOR</c>, whose mounts are recorded only at
    /// the next startup sync.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship that can take the survey role.</returns>
    public static bool CanSurvey(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.HasSurveyEquipment || ship.ShipType.Equals("SHIP_SURVEYOR", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the ship can mine: a mining laser (<see cref="HasMiningLaser"/>), a hold and a tank.</summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship that can take the mining role.</returns>
    public static bool CanMine(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return HasMiningLaser(ship) && ship.IsTradingCapable;
    }

    /// <summary>Whether the ship can siphon: a gas siphon, a hold and a tank.</summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a ship that can take the siphon role.</returns>
    public static bool CanSiphon(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.HasGasSiphonEquipment && ship.IsTradingCapable;
    }

    /// <summary>
    /// The engine's speed, from the cached engine; <paramref name="whenUnknown"/> for a ship bought since the last
    /// restart, whose engine startup sync hasn't recorded yet.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="whenUnknown">The speed to assume without a cached engine.</param>
    /// <returns>The speed, more than 0.</returns>
    public static int EngineSpeed(ShipModel ship, int whenUnknown)
    {
        ArgumentNullException.ThrowIfNull(ship);
        if (string.IsNullOrWhiteSpace(ship.EngineJson))
        {
            return whenUnknown;
        }

        try
        {
            using var engine = JsonDocument.Parse(ship.EngineJson);
            return engine.RootElement.TryGetProperty("speed", out var speed) && speed.TryGetInt32(out var value) && value > 0
                ? value
                : whenUnknown;
        }
        catch (JsonException)
        {
            return whenUnknown;
        }
    }

    /// <summary>
    /// Whether a ship is free for new work: not in transit, no goal (or one that is done or blocked), and no
    /// open assignment.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="goal">Its active goal, if any.</param>
    /// <param name="hasOpenAssignment">Whether it has an open assignment (scout or contract).</param>
    /// <returns>True when a plan may give it work.</returns>
    public static bool IsFree(ShipModel ship, ShipGoal? goal, bool hasOpenAssignment)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.LocalStatus != ShipLocalStatus.InTransit
            && !string.IsNullOrWhiteSpace(ship.WaypointSymbol)
            && !hasOpenAssignment
            && (goal is null || goal.Status is GoalStatus.Completed or GoalStatus.Blocked);
    }
}
