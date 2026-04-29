using SpaceTraders.Application.Interfaces.Repositories;
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
    ISettingsRepository settings,
    IFleetMaintenancePlanner maintenance,
    IDockedCommandAcceptor dockedCommands) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 200;

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
        else if (atSellWaypoint && !string.IsNullOrWhiteSpace(assignment.CargoSymbol) && ship.CargoCurrent > 0)
        {
            await dockedCommands.SellCargoAsync(@event.ShipSymbol, assignment.CargoSymbol, ship.CargoCurrent, cancellationToken);
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

        var preferredCargoModule = await settings.GetAsync<string>("Outfitting.TraderCargoModule", cancellationToken);
        if (!string.IsNullOrWhiteSpace(preferredCargoModule) &&
            (ship.ModulesJson?.Contains(preferredCargoModule, StringComparison.OrdinalIgnoreCase) != true))
        {
            await dockedCommands.InstallModuleAsync(@event.ShipSymbol, preferredCargoModule, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        await dockedCommands.OrbitAsync(@event.ShipSymbol, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }
}
