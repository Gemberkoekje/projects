using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipInTransitEventHandler : IChainOfCommandEventHandler<ShipInTransitEvent>
{
    public int Priority => 100;

    public Task<ChainOfCommandHandlerResult> HandleAsync(ShipInTransitEvent @event, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var systemSymbol = ExtractSystemSymbol(@event.DestinationWaypointSymbol);
        var inOrbitEvent = new ShipInOrbitEvent(
            @event.ShipSymbol,
            systemSymbol,
            @event.DestinationWaypointSymbol,
            @event.CorrelationId,
            @event.EventId,
            now);

        if (@event.ArrivalTime <= now)
        {
            return Task.FromResult(ChainOfCommandHandlerResult.Handled(inOrbitEvent));
        }

        return Task.FromResult(ChainOfCommandHandlerResult.Scheduled(inOrbitEvent, @event.ArrivalTime));
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}
