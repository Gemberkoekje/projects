using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles ShipInTransitEvent (general in-transit), scheduling arrivals appropriately.
/// </summary>
public sealed class ShipInTransitEventHandler : IChainOfCommandEventHandler<ShipInTransitEvent>
{
    public int Priority => 90;

    public Task<ChainOfCommandHandlerResult> HandleAsync(ShipInTransitEvent @event, CancellationToken cancellationToken)
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
