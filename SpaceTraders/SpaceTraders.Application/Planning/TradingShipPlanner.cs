using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Planning;

/// <summary>
/// Phase 4: trading-role ship planner migrated out of <c>ShipInOrbitTraderEventHandler</c>.
/// Pure decision logic for ships with the "Trade" assignment while in orbit. Mirrors the
/// destination resolution (cargo aware) and dock/navigate/assign-idle decisions of the
/// legacy chain handler, plus DRIFT and recommended-flight-mode patching.
/// </summary>
public sealed class TradingShipPlanner : IShipPlanner
{
    public bool CanPlan(ShipModel ship, ShipAssignmentDto assignment)
    {
        if (ship is null || assignment is null)
        {
            return false;
        }

        return assignment.AssignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase);
    }

    public ShipPlannerDecision Plan(ShipModel ship, ShipAssignmentDto assignment, ShipPlannerContext context)
    {
        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Ship is in transit; waiting for arrival.");
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Trading planner only handles in-orbit decisions.");
        }

        var destination = ResolveDestination(assignment, ship);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return ShipPlannerDecision.AssignIdle(ship.Symbol, "Trade assignment has no usable origin or destination.");
        }

        if (string.Equals(ship.WaypointSymbol, destination, StringComparison.OrdinalIgnoreCase))
        {
            return ShipPlannerDecision.Dock(ship.Symbol, "At trade destination; docking to buy/sell.");
        }

        var flightModeDecision = TryAdjustFlightMode(ship, destination, context);
        if (flightModeDecision is not null)
        {
            return flightModeDecision;
        }

        return ShipPlannerDecision.Navigate(ship.Symbol, destination, "Navigating to trade destination.");
    }

    private static string ResolveDestination(ShipAssignmentDto assignment, ShipModel ship)
    {
        if (ship.CargoCurrent > 0 && !string.IsNullOrWhiteSpace(assignment.DestWaypoint))
        {
            return assignment.DestWaypoint;
        }

        if (!string.IsNullOrWhiteSpace(assignment.OriginWaypoint))
        {
            return assignment.OriginWaypoint;
        }

        return assignment.DestWaypoint ?? string.Empty;
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
