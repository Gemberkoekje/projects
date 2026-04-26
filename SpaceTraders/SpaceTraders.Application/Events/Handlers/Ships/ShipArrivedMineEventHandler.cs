using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipArrivedMineEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IAgentRepository agents,
    SpaceTraders.Application.Ports.ISpaceTradersPort port) : IChainOfCommandEventHandler<ShipArrivedEvent>
{
    public int Priority => 200;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipArrivedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            var missingShipIdle = new ShipIdleEvent(
                @event.ShipSymbol,
                "Mine arrival ship missing in cache.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(missingShipIdle);
        }

        var atMiningOrigin = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        if (atMiningOrigin)
        {
            if (ship.Status?.Equals("DOCKED", StringComparison.OrdinalIgnoreCase) == true)
            {
                var nav = await port.OrbitShipAsync(@event.ShipSymbol, cancellationToken);
                await ships.UpdateNavAsync(@event.ShipSymbol, nav, null, cancellationToken);
            }

            var extraction = await port.ExtractResourcesAsync(@event.ShipSymbol, cancellationToken);
            await ships.UpdateCargoAsync(@event.ShipSymbol, extraction.Cargo, cancellationToken);

            var idleAfterExtract = new ShipIdleEvent(
                @event.ShipSymbol,
                "Mine arrival extracted resources.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(idleAfterExtract);
        }

        var atSellDestination = !string.IsNullOrWhiteSpace(assignment.DestWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        if (!atSellDestination)
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        if (ship.Status?.Equals("DOCKED", StringComparison.OrdinalIgnoreCase) != true)
        {
            var nav = await port.DockShipAsync(@event.ShipSymbol, cancellationToken);
            await ships.UpdateNavAsync(@event.ShipSymbol, nav, null, cancellationToken);
        }

        var cargoItem = ship.CargoInventory?.FirstOrDefault(item => item.Units > 0);
        if (cargoItem is not null)
        {
            var sale = await port.SellCargoAsync(@event.ShipSymbol, cargoItem.Symbol, cargoItem.Units, cancellationToken);
            await ships.UpdateCargoAsync(@event.ShipSymbol, sale.Cargo, cancellationToken);

            var agent = await agents.GetAsync(cancellationToken);
            if (agent is not null)
            {
                await agents.UpsertAsync(agent with { Credits = sale.AgentCredits }, cancellationToken);
            }
        }

        var idleAfterSale = new ShipIdleEvent(
            @event.ShipSymbol,
            "Mine arrival processed sell stop.",
            @event.CorrelationId,
            @event.EventId,
            TimeProvider.System.GetUtcNow());

        return ChainOfCommandHandlerResult.Handled(idleAfterSale);
    }
}
