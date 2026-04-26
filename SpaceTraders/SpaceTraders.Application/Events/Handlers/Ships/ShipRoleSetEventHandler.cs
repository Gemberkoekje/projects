using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles <see cref="ShipRoleSetEvent"/> by re-emitting a <see cref="ShipUndockedEvent"/>
/// so that the undocked chain can run with the newly assigned role.
/// </summary>
public sealed class ShipRoleSetEventHandler(
    IShipRepository ships) : IChainOfCommandEventHandler<ShipRoleSetEvent>
{
    public int Priority => 100;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipRoleSetEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            var missingIdle = new ShipIdleEvent(
                @event.ShipSymbol,
                "Ship not found after role was set.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(missingIdle);
        }

        var now = TimeProvider.System.GetUtcNow();
        var undockedEvent = new ShipUndockedEvent(
            @event.ShipSymbol,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol ?? string.Empty,
            @event.CorrelationId,
            @event.EventId,
            now);

        return ChainOfCommandHandlerResult.Handled(undockedEvent);
    }
}
