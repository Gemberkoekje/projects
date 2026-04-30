using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles trading-role ships that are docked. Buys or sells cargo based on current position,
/// then orbits to continue the trading loop.
/// </summary>
public sealed class ShipDockedTraderEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IContractRepository contracts,
    ISettingsRepository settings,
    IFleetMaintenancePlanner maintenance,
    IDockedCommandAcceptor dockedCommands) : IChainOfCommandEventHandler<ShipDockedEvent>
{

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var atBuyWaypoint = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        var atSellWaypoint = !string.IsNullOrWhiteSpace(assignment.DestWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        if (atBuyWaypoint && !string.IsNullOrWhiteSpace(assignment.CargoSymbol))
        {
            var unitsToBuy = ship.CargoCapacity - ship.CargoCurrent;
            if (unitsToBuy > 0)
            {
                await dockedCommands.BuyCargoAsync(@event.ShipSymbol, assignment.CargoSymbol, unitsToBuy, cancellationToken);
            }
        }
        else if (atSellWaypoint)
        {
            var deliveredAnyContractCargo = await TryDeliverContractCargoAtCurrentWaypointAsync(ship, cancellationToken);
            if (!deliveredAnyContractCargo && !string.IsNullOrWhiteSpace(assignment.CargoSymbol) && ship.CargoCurrent > 0)
            {
                await dockedCommands.SellCargoAsync(@event.ShipSymbol, assignment.CargoSymbol, ship.CargoCurrent, cancellationToken);
            }
        }

        var maintenanceDecision = await maintenance.DecideAsync(ship, assignment.AssignmentType, cancellationToken);
        if (maintenanceDecision.ShouldScrap)
        {
            await dockedCommands.ScrapAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        if (maintenanceDecision.ShouldRepair)
        {
            await dockedCommands.RepairAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        await dockedCommands.OrbitAsync(@event.ShipSymbol, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }

    private async Task<bool> TryDeliverContractCargoAtCurrentWaypointAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        if (ship.CargoInventory is null || string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            return false;
        }

        var activeContracts = await contracts.GetActiveAsync(cancellationToken);
        var deliveredAny = false;

        foreach (var contract in activeContracts.Where(c => c.IsAccepted && !c.IsFulfilled && !string.IsNullOrWhiteSpace(c.DeliverablesJson)))
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

                await dockedCommands.DeliverContractAsync(
                    contract.Id,
                    ship.Symbol,
                    deliverable.TradeSymbol,
                    unitsToDeliver,
                    ship.WaypointSymbol,
                    cancellationToken);

                deliveredAny = true;
            }
        }

        return deliveredAny;
    }

    private static IReadOnlyList<ContractDeliverableDto> DeserializeDeliverables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<ContractDeliverableDto>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
