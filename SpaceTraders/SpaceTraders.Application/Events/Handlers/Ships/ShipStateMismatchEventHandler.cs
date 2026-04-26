using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipStateMismatchEventHandler(
    IShipRepository ships,
    IInOrbitCommandAcceptor inOrbitCommands) : IChainOfCommandEventHandler<ShipStateMismatchEvent>
{
    public int Priority => 1000;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipStateMismatchEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is not null && string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);

            var now = TimeProvider.System.GetUtcNow();
            var dockedEvent = new ShipDockedEvent(
                @event.ShipSymbol,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty,
                @event.CorrelationId,
                @event.EventId,
                now);

            return ChainOfCommandHandlerResult.Handled(dockedEvent);
        }

        var idleEvent = new ShipIdleEvent(
            @event.ShipSymbol,
            $"State mismatch recovery paused: {@event.Reason}",
            @event.CorrelationId,
            @event.EventId,
            TimeProvider.System.GetUtcNow());

        return ChainOfCommandHandlerResult.Handled(idleEvent);
    }
}
