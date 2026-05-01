using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Planning;

/// <summary>
/// Phase 4b: cross-cutting maintenance planner that wraps <see cref="Services.IFleetMaintenancePlanner"/>.
/// Migrates the repair/scrap branches that were duplicated across the docked role handlers
/// into a single planner that runs ahead of the role planners.
/// Phase 7b: legacy docked role handlers deleted; planner is the sole implementation.
///
/// The planner returns <see cref="ShipPlannerCommandKind.None"/> when no maintenance action
/// is needed so <see cref="ShipPlannerService"/> can fall through to the role planner.
/// </summary>
public sealed class MaintenanceShipPlanner : IShipPlanner
{
    public bool CanPlan(ShipModel ship, ShipAssignmentDto assignment) => ship is not null && assignment is not null;

    public ShipPlannerDecision Plan(ShipModel ship, ShipAssignmentDto assignment, ShipPlannerContext context)
    {
        if (ship.LocalStatus != ShipLocalStatus.Docked)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Maintenance planner only handles docked decisions.");
        }

        if (context.Maintenance is null)
        {
            return ShipPlannerDecision.None(ship.Symbol, "No maintenance decision available.");
        }

        if (context.Maintenance.ShouldScrap)
        {
            return ShipPlannerDecision.Scrap(ship.Symbol, context.Maintenance.Reason ?? "Integrity below scrap threshold.");
        }

        if (context.Maintenance.ShouldRepair)
        {
            return ShipPlannerDecision.Repair(ship.Symbol, context.Maintenance.Reason ?? "Condition below repair threshold.");
        }

        return ShipPlannerDecision.None(ship.Symbol, "No maintenance action required.");
    }
}
