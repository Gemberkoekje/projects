using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
namespace SpaceTraders.Application.Planning;

/// <summary>
/// Phase 4: contract-role ship planner migrated out of <c>ShipInOrbitContractEventHandler</c>.
/// Pure decision logic for ships with the "Contract" assignment while in orbit. The legacy
/// handler also performs side effects (refresh metadata) which remain in the chain handler;
/// this planner only emits the next dock / navigate / idle / patch-flight-mode command.
///
/// Destination resolution: if the ship currently carries cargo of the contract good,
/// head to the delivery waypoint. Otherwise return to origin to load.
/// </summary>
public sealed class ContractShipPlanner : IShipPlanner
{
    public bool CanPlan(ShipModel ship, ShipAssignmentDto assignment)
    {
        if (ship is null || assignment is null)
        {
            return false;
        }

        return assignment.AssignmentType.Equals("Contract", StringComparison.OrdinalIgnoreCase);
    }

    public ShipPlannerDecision Plan(ShipModel ship, ShipAssignmentDto assignment, ShipPlannerContext context)
    {
        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Ship is in transit; waiting for arrival.");
        }

        // Phase 6.5b: handle docked state — buy at origin, deliver at destination, then orbit.
        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            return PlanDocked(ship, assignment);
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Contract planner cannot handle current ship status.");
        }

        var destination = ResolveDestination(assignment, ship);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return ShipPlannerDecision.AssignIdle(ship.Symbol, "Contract assignment has no usable origin or destination.");
        }

        if (string.Equals(ship.WaypointSymbol, destination, StringComparison.OrdinalIgnoreCase))
        {
            return ShipPlannerDecision.Dock(ship.Symbol, "At contract waypoint; docking to deliver/load.");
        }

        var flightModeDecision = TryAdjustFlightMode(ship, destination, context);
        if (flightModeDecision is not null)
        {
            return flightModeDecision;
        }

        return ShipPlannerDecision.Navigate(ship.Symbol, destination, "Navigating to contract waypoint.");
    }

    private static ShipPlannerDecision PlanDocked(ShipModel ship, ShipAssignmentDto assignment)
    {
        var atOrigin = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        var atDestination = !string.IsNullOrWhiteSpace(assignment.DestWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        if (atOrigin && !string.IsNullOrWhiteSpace(assignment.CargoSymbol))
        {
            var cargoUnits = ship.CargoInventory?
                .FirstOrDefault(i => i.Symbol.Equals(assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase))?
                .Units ?? 0;

            var remainingRequired = assignment.RequiredUnits > 0
                ? assignment.RequiredUnits - cargoUnits
                : ship.CargoCapacity - ship.CargoCurrent;
            var unitsToBuy = Math.Min(ship.CargoCapacity - ship.CargoCurrent, Math.Max(0, remainingRequired));

            if (unitsToBuy > 0)
            {
                return ShipPlannerDecision.BuyCargo(
                    ship.Symbol,
                    assignment.CargoSymbol,
                    unitsToBuy,
                    $"Buying {unitsToBuy}x {assignment.CargoSymbol} for contract at origin.");
            }
        }
        else if (atDestination && !string.IsNullOrWhiteSpace(assignment.CargoSymbol) && !string.IsNullOrWhiteSpace(assignment.ContractId))
        {
            var cargoUnits = ship.CargoInventory?
                .FirstOrDefault(i => i.Symbol.Equals(assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase))?
                .Units ?? 0;

            var unitsToDeliver = assignment.RequiredUnits > 0
                ? Math.Min(cargoUnits, assignment.RequiredUnits)
                : cargoUnits;

            if (unitsToDeliver > 0)
            {
                return ShipPlannerDecision.DeliverContractCargo(
                    ship.Symbol,
                    assignment.ContractId,
                    assignment.CargoSymbol,
                    unitsToDeliver,
                    assignment.DestWaypoint ?? ship.WaypointSymbol ?? string.Empty,
                    $"Delivering {unitsToDeliver}x {assignment.CargoSymbol} for contract {assignment.ContractId}.");
            }
        }

        return ShipPlannerDecision.Orbit(ship.Symbol, "Docked contract ship: no cargo action needed; orbiting.");
    }

    private static string ResolveDestination(ShipAssignmentDto assignment, ShipModel ship)
    {
        var cargoSymbol = assignment.CargoSymbol ?? string.Empty;
        var cargoUnits = ship.CargoInventory?
            .FirstOrDefault(i => i.Symbol.Equals(cargoSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        if (cargoUnits > 0 && !string.IsNullOrWhiteSpace(assignment.DestWaypoint))
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
