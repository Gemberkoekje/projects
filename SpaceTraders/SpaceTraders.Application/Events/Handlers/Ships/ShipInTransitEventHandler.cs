using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Schedules or publishes a <see cref="ShipArrivedEvent"/> based on the ship's arrival time.
/// If the arrival is in the future, the event is scheduled; otherwise it is published immediately.
/// </summary>
public sealed class ShipInTransitEventHandler(IMessageBus bus) : IChainOfCommandEventHandler<ShipInTransitEvent>
{
    public int Priority => 90;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInTransitEvent @event, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var systemSymbol = ExtractSystemSymbol(@event.DestinationWaypointSymbol);
        if (string.IsNullOrEmpty(systemSymbol))
        {
            systemSymbol = ExtractSystemSymbol(@event.OriginWaypointSymbol);
        }

        var arrived = new ShipArrivedEvent(
            @event.ShipSymbol,
            systemSymbol,
            @event.DestinationWaypointSymbol,
            @event.ArrivalTime,
            @event.CorrelationId,
            @event.EventId,
            now);

        // Phase 5b: explicit automation tick replaces chain-routed inheritance dispatch
        // (ShipArrivedEvent -> ShipInOrbitEvent). When arrival is in the future the tick
        // is scheduled; otherwise it is published immediately.
        var tick = new ShipAutomationTickEvent(
            @event.ShipSymbol,
            "Arrived",
            @event.ArrivalTime,
            @event.CorrelationId,
            @event.EventId);

        if (@event.ArrivalTime > now)
        {
            await bus.ScheduleAsync(tick, @event.ArrivalTime);
        }
        else
        {
            await bus.PublishAsync(tick);
        }

#pragma warning disable CS0618 // chain handlers still rely on result-based scheduling/publishing during migration
        if (@event.ArrivalTime > now)
        {
            return ChainOfCommandHandlerResult.Scheduled(arrived, @event.ArrivalTime);
        }

        return ChainOfCommandHandlerResult.Handled(arrived);
#pragma warning restore CS0618
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        if (string.IsNullOrWhiteSpace(waypointSymbol))
        {
            return string.Empty;
        }

        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}
