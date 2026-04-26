using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles <see cref="ShipRefueledEvent"/> by emitting a <see cref="ShipIdleEvent"/>.
/// After refuelling the ship is ready for its next assignment.
/// </summary>
public sealed class ShipRefueledEventHandler : IChainOfCommandEventHandler<ShipRefueledEvent>
{
    public int Priority => 1000;

    public Task<ChainOfCommandHandlerResult> HandleAsync(ShipRefueledEvent @event, CancellationToken cancellationToken)
    {
        var idleEvent = new ShipIdleEvent(
            @event.ShipSymbol,
            "Ship refuelled; awaiting next action.",
            @event.CorrelationId,
            @event.EventId,
            TimeProvider.System.GetUtcNow());

        return Task.FromResult(ChainOfCommandHandlerResult.Handled(idleEvent));
    }
}
