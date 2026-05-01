using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Schedules or publishes a <see cref="ShipAutomationTickEvent"/> based on the ship's arrival time.
/// If the arrival is in the future, the tick is scheduled; otherwise it is published immediately.
/// </summary>
public sealed class ShipInTransitEventHandler(IMessageBus bus) : IChainOfCommandEventHandler<ShipInTransitEvent>
{

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInTransitEvent @event, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();

        // Phase 7c: ShipArrivedEvent deleted; arrival is signalled by ShipAutomationTickEvent only.
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

        return ChainOfCommandHandlerResult.Handled();
    }
}
