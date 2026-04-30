using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Planning;

/// <summary>
/// Phase 3: first ship planner migrated out of the chain-of-command handlers.
/// Pure decision logic for mining and siphon assignments. Mirrors the behaviour previously
/// implemented in <c>ShipInOrbitMineEventHandler</c>, but emits a single
/// <see cref="ShipPlannerDecision"/> per call rather than invoking the message bus directly.
/// </summary>
public sealed class MiningShipPlanner : IShipPlanner
{
    public bool CanPlan(ShipModel ship, ShipAssignmentDto assignment)
    {
        if (ship is null || assignment is null)
        {
            return false;
        }

        return assignment.AssignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase)
            || assignment.AssignmentType.Equals("Siphon", StringComparison.OrdinalIgnoreCase);
    }

    public ShipPlannerDecision Plan(ShipModel ship, ShipAssignmentDto assignment, ShipPlannerContext context)
    {
        // Ships in transit cannot accept any command.
        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Ship is in transit; waiting for arrival.");
        }

        // Only orbit-state decisions are migrated in Phase 3.
        // Docked transitions are still handled by the docked chain handlers for now.
        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Mining planner only handles in-orbit decisions in Phase 3.");
        }

        var atOrigin = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
            && string.Equals(ship.WaypointSymbol, assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase);

        // At mining origin and cargo not full: extract / survey / siphon.
        if (atOrigin && ship.CargoCurrent < ship.CargoCapacity)
        {
            if (assignment.AssignmentType.Equals("Siphon", StringComparison.OrdinalIgnoreCase))
            {
                return ShipPlannerDecision.Siphon(ship.Symbol, "At siphon origin with cargo space.");
            }

            if (ship.HasSurveyEquipment && context.ActiveSurveyCount == 0)
            {
                return ShipPlannerDecision.Survey(ship.Symbol, "Surveying before extraction.");
            }

            return ShipPlannerDecision.Extract(ship.Symbol, "Extracting at mining origin.");
        }

        // Has cargo: head to sell destination.
        if (ship.CargoCurrent > 0)
        {
            if (string.IsNullOrWhiteSpace(assignment.DestWaypoint))
            {
                return ShipPlannerDecision.AssignIdle(ship.Symbol, "Ship has cargo but no sell destination.");
            }

            var atSellDest = string.Equals(ship.WaypointSymbol, assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase);
            if (atSellDest)
            {
                return ShipPlannerDecision.Dock(ship.Symbol, "At sell destination; docking to deliver/sell.");
            }

            var flightModeDecision = TryAdjustFlightMode(ship, assignment.DestWaypoint, context);
            if (flightModeDecision is not null)
            {
                return flightModeDecision;
            }

            return ShipPlannerDecision.Navigate(ship.Symbol, assignment.DestWaypoint!, "Navigating to sell destination.");
        }

        // No cargo: return to mining origin.
        if (string.IsNullOrWhiteSpace(assignment.OriginWaypoint))
        {
            return ShipPlannerDecision.AssignIdle(ship.Symbol, "Ship has no cargo and no mining origin assigned.");
        }

        if (atOrigin)
        {
            return ShipPlannerDecision.Dock(ship.Symbol, "At mining origin with empty cargo; docking.");
        }

        var originFlightMode = TryAdjustFlightMode(ship, assignment.OriginWaypoint!, context);
        if (originFlightMode is not null)
        {
            return originFlightMode;
        }

        return ShipPlannerDecision.Navigate(ship.Symbol, assignment.OriginWaypoint!, "Navigating to mining origin.");
    }

    private static ShipPlannerDecision? TryAdjustFlightMode(ShipModel ship, string destinationWaypoint, ShipPlannerContext context)
    {
        if (ship.FuelCapacity <= 0)
        {
            if (!string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
            {
                return ShipPlannerDecision.PatchFlightMode(ship.Symbol, "DRIFT", "Ship has no fuel tank; switching to DRIFT.");
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(context.RecommendedFlightMode))
        {
            return null;
        }

        if (!IsSameSystem(ship.SystemSymbol, destinationWaypoint))
        {
            return null;
        }

        if (string.Equals(ship.FlightMode, context.RecommendedFlightMode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ShipPlannerDecision.PatchFlightMode(
            ship.Symbol,
            context.RecommendedFlightMode,
            $"Adjusting flight mode to {context.RecommendedFlightMode} before navigating.");
    }

    private static bool IsSameSystem(string shipSystem, string destinationWaypoint)
    {
        if (string.IsNullOrWhiteSpace(shipSystem) || string.IsNullOrWhiteSpace(destinationWaypoint))
        {
            return false;
        }

        var lastDash = destinationWaypoint.LastIndexOf('-');
        var destinationSystem = lastDash > 0 ? destinationWaypoint[..lastDash] : destinationWaypoint;
        return string.Equals(shipSystem, destinationSystem, StringComparison.OrdinalIgnoreCase);
    }
}
