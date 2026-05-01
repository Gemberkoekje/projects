using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using System.Text.Json;

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

        // Phase 6.5b: handle docked state — deliver, sell, jettison, refuel, then orbit.
        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            return PlanDocked(ship, context);
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Mining planner cannot handle current ship status.");
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

    private static ShipPlannerDecision PlanDocked(ShipModel ship, ShipPlannerContext context)
    {
        // 1. Deliver contract cargo at the current waypoint if possible.
        var deliverDecision = TryPlanContractDelivery(ship, context);
        if (deliverDecision is not null)
        {
            return deliverDecision;
        }

        // 2. Sell eligible non-protected cargo.
        if (ship.CargoInventory is not null)
        {
            var protectedCargo = GetProtectedCargo(context);
            foreach (var cargo in ship.CargoInventory.Where(c => c.Units > 0))
            {
                if (protectedCargo.Contains(cargo.Symbol))
                {
                    continue;
                }

                var isHydrocarbon = cargo.Symbol.Equals("HYDROCARBON", StringComparison.OrdinalIgnoreCase);
                if (isHydrocarbon && cargo.Units <= context.MiningReserveHydrocarbonUnits)
                {
                    continue;
                }

                var sellPrice = context.CurrentMarketSnapshot?.TradeGoods
                    .FirstOrDefault(g => g.Symbol.Equals(cargo.Symbol, StringComparison.OrdinalIgnoreCase))?.SellPrice ?? 0;

                if (sellPrice > 0 && sellPrice >= context.MiningMinimumSellPrice)
                {
                    var sellUnits = isHydrocarbon
                        ? Math.Max(0, cargo.Units - context.MiningReserveHydrocarbonUnits)
                        : cargo.Units;

                    if (sellUnits > 0)
                    {
                        return ShipPlannerDecision.SellCargo(
                            ship.Symbol,
                            cargo.Symbol,
                            sellUnits,
                            $"Selling {sellUnits}x {cargo.Symbol} at market.");
                    }
                }

                // 3. Jettison low-value cargo when full.
                if (context.MiningJettisonLowValueWhenFull && ship.CargoCurrent >= ship.CargoCapacity)
                {
                    var jettisonUnits = isHydrocarbon
                        ? Math.Max(0, cargo.Units - context.MiningReserveHydrocarbonUnits)
                        : cargo.Units;

                    if (jettisonUnits > 0)
                    {
                        return ShipPlannerDecision.JettisonCargo(
                            ship.Symbol,
                            cargo.Symbol,
                            jettisonUnits,
                            $"Jettisoning {jettisonUnits}x {cargo.Symbol}: low-value cargo when hold is full.");
                    }
                }
            }
        }

        // 4. Refuel from hydrocarbon cargo if the hold has enough hydrocarbons above the reserve and the tank is not full.
        if (ship.FuelCapacity > 0 && ship.FuelCurrent < ship.FuelCapacity)
        {
            var hydrocarbonUnits = ship.CargoInventory?
                .FirstOrDefault(c => c.Symbol.Equals("HYDROCARBON", StringComparison.OrdinalIgnoreCase))?
                .Units ?? 0;
            if (hydrocarbonUnits > context.MiningReserveHydrocarbonUnits)
            {
                return ShipPlannerDecision.RefuelFromCargo(ship.Symbol, "Refueling from hydrocarbon cargo reserves.");
            }
        }

        // 5. Nothing more to do docked; orbit to continue the mining cycle.
        return ShipPlannerDecision.Orbit(ship.Symbol, "Docked miner: no cargo action needed; orbiting to continue cycle.");
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

    private static HashSet<string> GetProtectedCargo(ShipPlannerContext context)
    {
        return context.ActiveContracts
            .Where(c => c.IsAccepted && !c.IsFulfilled)
            .SelectMany(c => DeserializeDeliverables(c.DeliverablesJson))
            .Where(d => d.UnitsRequired > d.UnitsFulfilled)
            .Select(d => d.TradeSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
