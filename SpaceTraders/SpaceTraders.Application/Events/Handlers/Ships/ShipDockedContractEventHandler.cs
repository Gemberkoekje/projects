using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles contract-role ships while docked.
/// </summary>
public sealed class ShipDockedContractEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    ISettingsRepository settings,
    IFleetMaintenancePlanner maintenance,
    IDockedCommandAcceptor dockedCommands) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 150;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Contract", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var atOrigin = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        var atDestination = !string.IsNullOrWhiteSpace(assignment.DestWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        if (atOrigin && !string.IsNullOrWhiteSpace(assignment.CargoSymbol))
        {
            var cargoUnits = ship.CargoInventory?
                .FirstOrDefault(i => i.Symbol.Equals(assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase))?
                .Units ?? 0;

            var remainingRequired = assignment.RequiredUnits > 0 ? assignment.RequiredUnits - cargoUnits : ship.CargoCapacity - ship.CargoCurrent;
            var unitsToBuy = Math.Min(ship.CargoCapacity - ship.CargoCurrent, Math.Max(0, remainingRequired));

            if (unitsToBuy > 0)
            {
                await dockedCommands.BuyCargoAsync(@event.ShipSymbol, assignment.CargoSymbol, unitsToBuy, cancellationToken);
            }
        }
        else if (atDestination && !string.IsNullOrWhiteSpace(assignment.CargoSymbol) && !string.IsNullOrWhiteSpace(assignment.ContractId))
        {
            var cargoUnits = ship.CargoInventory?
                .FirstOrDefault(i => i.Symbol.Equals(assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase))?
                .Units ?? 0;

            var unitsToDeliver = assignment.RequiredUnits > 0 ? Math.Min(cargoUnits, assignment.RequiredUnits) : cargoUnits;
            if (unitsToDeliver > 0)
            {
                await dockedCommands.DeliverContractAsync(
                    assignment.ContractId,
                    @event.ShipSymbol,
                    assignment.CargoSymbol,
                    unitsToDeliver,
                    assignment.DestWaypoint ?? ship.WaypointSymbol ?? string.Empty,
                    cancellationToken);
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
}
