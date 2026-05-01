using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Wolverine handler that schedules or publishes a <see cref="ShipAutomationTickEvent"/> based on the ship's arrival time.
/// If the arrival is in the future, the tick is scheduled; otherwise it is published immediately.
/// Phase 7d: converted from IChainOfCommandEventHandler to a plain Wolverine handler.
/// </summary>
public sealed class ShipInTransitEventHandler
{
    public async Task Handle(ShipInTransitEvent @event, IMessageBus bus, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();

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
    }
}
