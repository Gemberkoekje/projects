using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipMovingEventHandler : IChainOfCommandEventHandler<ShipMovingEvent>
{
    public int Priority => 100;

    public Task<ChainOfCommandHandlerResult> HandleAsync(ShipMovingEvent @event, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var systemSymbol = ExtractSystemSymbol(@event.DestinationWaypointSymbol);
        var arrivedEvent = new ShipArrivedEvent(
            @event.ShipSymbol,
            systemSymbol,
            @event.DestinationWaypointSymbol,
            @event.ArrivalTime,
            @event.CorrelationId,
            @event.EventId,
            now);

        if (@event.ArrivalTime <= now)
        {
            return Task.FromResult(ChainOfCommandHandlerResult.Handled(arrivedEvent));
        }

        return Task.FromResult(ChainOfCommandHandlerResult.Scheduled(arrivedEvent, @event.ArrivalTime));
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}
