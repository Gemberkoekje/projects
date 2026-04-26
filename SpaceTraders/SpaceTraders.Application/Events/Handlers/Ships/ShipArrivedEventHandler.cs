using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipArrivedEventHandler : IChainOfCommandEventHandler<ShipArrivedEvent>
{
    public int Priority => 1000;

    public Task<ChainOfCommandHandlerResult> HandleAsync(ShipArrivedEvent @event, CancellationToken cancellationToken)
    {
        var idleEvent = new ShipIdleEvent(
            @event.ShipSymbol,
            "No specialized arrival handler matched.",
            @event.CorrelationId,
            @event.EventId,
            TimeProvider.System.GetUtcNow());

        return Task.FromResult(ChainOfCommandHandlerResult.Handled(idleEvent));
    }
}
