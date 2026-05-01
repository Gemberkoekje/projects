using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Planning;

/// <summary>
/// Phase 4: scouting-role ship planner migrated out of <c>ShipInOrbitScoutEventHandler</c>.
/// Pure decision logic for ships with the "Scout" or "MarketProbe" assignment while in
/// orbit. Side effects performed by the legacy handler (waypoint visit marking, market and
/// shipyard refresh, chart creation) intentionally remain in the chain handler — this
/// planner only chooses the single navigation/dock command for the next decision point.
/// </summary>
public sealed class ScoutingShipPlanner : IShipPlanner
{
    private const string MarketProbeAssignmentType = "MarketProbe";

    public bool CanPlan(ShipModel ship, ShipAssignmentDto assignment)
    {
        if (ship is null || assignment is null)
        {
            return false;
        }

        return assignment.AssignmentType.Equals("Scout", StringComparison.OrdinalIgnoreCase)
            || assignment.AssignmentType.Equals(MarketProbeAssignmentType, StringComparison.OrdinalIgnoreCase);
    }

    public ShipPlannerDecision Plan(ShipModel ship, ShipAssignmentDto assignment, ShipPlannerContext context)
    {
        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Ship is in transit; waiting for arrival.");
        }

        // Phase 6.5b: when docked, refresh market/shipyard data and orbit (unless this is a
        // market probe already parked at its target, which the executor handles).
        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            return ShipPlannerDecision.RefreshScoutData(ship.Symbol, "Docked at waypoint; refreshing data.");
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Scouting planner cannot handle current ship status.");
        }

        if (string.IsNullOrWhiteSpace(assignment.OriginWaypoint))
        {
            return ShipPlannerDecision.AssignIdle(ship.Symbol, "Scout assignment has no target waypoint.");
        }

        var target = assignment.OriginWaypoint;
        if (string.Equals(ship.WaypointSymbol, target, StringComparison.OrdinalIgnoreCase))
        {
            return ShipPlannerDecision.Dock(ship.Symbol, "At scout target; docking.");
        }

        if (ship.FuelCapacity <= 0 && !string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            return ShipPlannerDecision.PatchFlightMode(ship.Symbol, "DRIFT", "Ship has no fuel tank; switching to DRIFT.");
        }

        return ShipPlannerDecision.Navigate(ship.Symbol, target, "Navigating to scout target.");
    }
}
