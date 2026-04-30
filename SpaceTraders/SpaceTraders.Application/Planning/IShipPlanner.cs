using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Planning;

/// <summary>
/// Phase 3: ship planner boundary.
/// A ship planner converts a single ship's current state and assignment into exactly one
/// next command (or no command at all). Planners must not have side effects: they only return
/// a <see cref="ShipPlannerDecision"/>. Translating that decision into a command is the
/// responsibility of <see cref="IShipPlannerExecutor"/>.
/// </summary>
public interface IShipPlanner
{
    /// <summary>
    /// True if this planner handles the supplied ship/assignment combination.
    /// </summary>
    bool CanPlan(ShipModel ship, ShipAssignmentDto assignment);

    /// <summary>
    /// Decide the single next command for the ship based on its current state and assignment.
    /// </summary>
    ShipPlannerDecision Plan(ShipModel ship, ShipAssignmentDto assignment, ShipPlannerContext context);
}

/// <summary>
/// Phase 3: ship planner boundary.
/// Read-only contextual data a planner may need to make a decision. Loaded by the
/// <see cref="IShipPlannerService"/> before invoking the planner so the planner itself
/// remains pure and easy to unit test.
/// </summary>
public sealed record ShipPlannerContext
{
    public int ActiveSurveyCount { get; init; }

    public string RecommendedFlightMode { get; init; } = string.Empty;

    /// <summary>Phase 4b: nearest known fuel-selling market in the ship's current system, if any.</summary>
    public string FuelMarketWaypoint { get; init; } = string.Empty;

    /// <summary>Phase 4b: true when the current docked waypoint sells fuel (used by the docked refuel planner).</summary>
    public bool CurrentWaypointSellsFuel { get; init; }

    /// <summary>Phase 4b: maintenance decision for the ship under its current assignment, if available.</summary>
    public FleetMaintenanceDecision? Maintenance { get; init; }

    /// <summary>Phase 4b: true when the ship's builder construction site is complete.</summary>
    public bool ConstructionComplete { get; init; }
}
