using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipStateMismatchEventHandler(
    IShipRepository ships,
    IInOrbitCommandAcceptor inOrbitCommands,
    IMessageBus bus) : IChainOfCommandEventHandler<ShipStateMismatchEvent>
{
    public int Priority => 1000;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipStateMismatchEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is not null && string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        await bus.InvokeAsync(new AssignShipCommand(
            @event.ShipSymbol,
            "Idle",
            SystemSymbol: ship?.SystemSymbol ?? string.Empty,
            WaypointSymbol: ship?.WaypointSymbol ?? string.Empty,
            CorrelationId: @event.CorrelationId,
            CausationId: @event.EventId), cancellationToken);

        return ChainOfCommandHandlerResult.Handled();
    }
}
