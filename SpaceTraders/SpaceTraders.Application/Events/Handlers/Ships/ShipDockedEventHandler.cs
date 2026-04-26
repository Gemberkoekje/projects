using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles <see cref="ShipDockedEvent"/> by emitting a <see cref="ShipIdleEvent"/>.
/// Docking itself ends a movement segment; the ship is idle until the next action.
/// </summary>
public sealed class ShipDockedEventHandler : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 1000;

    public Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var idleEvent = new ShipIdleEvent(
            @event.ShipSymbol,
            "Ship docked; awaiting next action.",
            @event.CorrelationId,
            @event.EventId,
            TimeProvider.System.GetUtcNow());

        return Task.FromResult(ChainOfCommandHandlerResult.Handled(idleEvent));
    }
}
