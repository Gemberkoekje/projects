using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Which plan a ship works for, by what it can do (PLAN.md slice 6.4), so every plan sees the fleet alike:
/// <list type="bullet">
///   <item>with the survey plan on, a ship that can survey surveys, and nothing else (D20): the command
///   ship's mining laser stays unused;</item>
///   <item>a ship that can mine, and doesn't survey, mines: the contract first, every free miner (D23), then
///   the mining plan's trips. Only a miner neither has work for may trade;</item>
///   <item>any other ship with a hold and a tank trades.</item>
/// </list>
/// </summary>
public static class FleetRoles
{
    /// <summary>Whether the ship surveys, and only surveys (D20).</summary>
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
    /// Whether the ship is a cargo ship, as the trading plan buys them (D21): a hold and a tank, and nothing
    /// to mine, siphon or survey with. Judged by what it carries rather than its cached type, which startup
    /// sync replaces with the registration role (B25).
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True for a shuttle or hauler.</returns>
    public static bool IsCargoShip(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.IsTradingCapable
            && !ship.HasMiningEquipment
            && !ship.HasGasSiphonEquipment
            && !ship.HasSurveyEquipment;
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
