using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using System.Text.Json;

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

        // Phase 6.5b: handle docked state — buy at origin, deliver/sell at destination, then orbit.
        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            return PlanDocked(ship, assignment, context);
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Trading planner cannot handle current ship status.");
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

    private static ShipPlannerDecision PlanDocked(ShipModel ship, ShipAssignmentDto assignment, ShipPlannerContext context)
    {
        var atBuyWaypoint = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        var atSellWaypoint = !string.IsNullOrWhiteSpace(assignment.DestWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        if (atBuyWaypoint && !string.IsNullOrWhiteSpace(assignment.CargoSymbol))
        {
            var unitsToBuy = ship.CargoCapacity - ship.CargoCurrent;
            if (unitsToBuy > 0)
            {
                return ShipPlannerDecision.BuyCargo(
                    ship.Symbol,
                    assignment.CargoSymbol,
                    unitsToBuy,
                    $"Buying {unitsToBuy}x {assignment.CargoSymbol} at trade origin.");
            }
        }
        else if (atSellWaypoint)
        {
            // Try to deliver contract cargo first.
            var deliverDecision = TryPlanContractDelivery(ship, context);
            if (deliverDecision is not null)
            {
                return deliverDecision;
            }

            // Sell the assignment cargo if any is on board.
            if (!string.IsNullOrWhiteSpace(assignment.CargoSymbol) && ship.CargoCurrent > 0)
            {
                return ShipPlannerDecision.SellCargo(
                    ship.Symbol,
                    assignment.CargoSymbol,
                    ship.CargoCurrent,
                    $"Selling {ship.CargoCurrent}x {assignment.CargoSymbol} at trade destination.");
            }
        }

        return ShipPlannerDecision.Orbit(ship.Symbol, "Docked trader: no buy/sell action needed; orbiting.");
    }

    private static ShipPlannerDecision? TryPlanContractDelivery(ShipModel ship, ShipPlannerContext context)
    {
        if (ship.CargoInventory is null || string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            return null;
        }

        foreach (var contract in context.ActiveContracts.Where(c => c.IsAccepted && !c.IsFulfilled && !string.IsNullOrWhiteSpace(c.DeliverablesJson)))
        {
            var deliverables = DeserializeDeliverables(contract.DeliverablesJson);
            foreach (var deliverable in deliverables)
            {
                if (!deliverable.DestinationSymbol.Equals(ship.WaypointSymbol, StringComparison.OrdinalIgnoreCase)
                    || deliverable.UnitsRequired <= deliverable.UnitsFulfilled)
                {
                    continue;
                }

                var cargoUnits = ship.CargoInventory
                    .FirstOrDefault(i => i.Symbol.Equals(deliverable.TradeSymbol, StringComparison.OrdinalIgnoreCase))?
                    .Units ?? 0;
                if (cargoUnits <= 0)
                {
                    continue;
                }

                var pending = deliverable.UnitsRequired - deliverable.UnitsFulfilled;
                var unitsToDeliver = Math.Min(cargoUnits, pending);
                if (unitsToDeliver <= 0)
                {
                    continue;
                }

                return ShipPlannerDecision.DeliverContractCargo(
                    ship.Symbol,
                    contract.Id,
                    deliverable.TradeSymbol,
                    unitsToDeliver,
                    ship.WaypointSymbol,
                    $"Delivering {unitsToDeliver}x {deliverable.TradeSymbol} for contract {contract.Id}.");
            }
        }

        return null;
    }

    private static IReadOnlyList<ContractDeliverableDto> DeserializeDeliverables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ContractDeliverableDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
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
