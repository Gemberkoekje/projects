using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipUndockedEventHandler : IChainOfCommandEventHandler<ShipUndockedEvent>
{
    public int Priority => 1000;

    public Task<ChainOfCommandHandlerResult> HandleAsync(ShipUndockedEvent @event, CancellationToken cancellationToken)
    {
        var nextEvent = new ShipIdleEvent(
            @event.ShipSymbol,
            "No specialized undocked handler matched.",
            @event.CorrelationId,
            @event.EventId,
            TimeProvider.System.GetUtcNow());

        return Task.FromResult(ChainOfCommandHandlerResult.Handled(nextEvent));
    }
}
