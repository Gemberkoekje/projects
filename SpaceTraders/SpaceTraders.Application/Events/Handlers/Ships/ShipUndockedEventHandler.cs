using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipUndockedEventHandler(
    IShipRepository ships,
    IInOrbitCommandAcceptor inOrbitCommands) : IChainOfCommandEventHandler<ShipUndockedEvent>
{
    public int Priority => 1000;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipUndockedEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            var missingShipMismatch = new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipUndockedEventHandler),
                "IN_ORBIT",
                "UNKNOWN",
                "Fallback undocked handling could not load ship state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(missingShipMismatch);
        }

        if (string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
            var now = TimeProvider.System.GetUtcNow();

            var idleDockedEvent = new ShipIdleDockedEvent(
                @event.ShipSymbol,
                ship.SystemSymbol ?? @event.SystemSymbol,
                ship.WaypointSymbol ?? @event.WaypointSymbol,
                "Fallback undocked recovery docked ship due to missing or invalid role plan.",
                @event.CorrelationId,
                @event.EventId,
                now);

            return ChainOfCommandHandlerResult.Handled(idleDockedEvent);
        }

        var mismatchEvent = new ShipStateMismatchEvent(
            @event.ShipSymbol,
            nameof(ShipUndockedEventHandler),
            "IN_ORBIT",
            ship.Status ?? "UNKNOWN",
            "Fallback undocked handler received a ship outside orbit state.",
            @event.CorrelationId,
            @event.EventId,
            TimeProvider.System.GetUtcNow());

        return ChainOfCommandHandlerResult.Handled(mismatchEvent);
    }
}
