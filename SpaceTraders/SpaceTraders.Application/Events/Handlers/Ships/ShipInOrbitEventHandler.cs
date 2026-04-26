using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipInOrbitEventHandler : IChainOfCommandEventHandler<ShipInOrbitEvent>
{
    public int Priority => 100;

    public Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        var undockedEvent = new ShipUndockedEvent(
            @event.ShipSymbol,
            @event.SystemSymbol,
            @event.WaypointSymbol,
            @event.CorrelationId,
            @event.EventId,
            TimeProvider.System.GetUtcNow());

        return Task.FromResult(ChainOfCommandHandlerResult.Handled(undockedEvent));
    }
}
