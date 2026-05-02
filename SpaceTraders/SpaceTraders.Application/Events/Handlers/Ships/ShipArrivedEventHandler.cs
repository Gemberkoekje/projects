using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Phase 14d: handles <see cref="ShipArrivedEvent"/> fired by <c>ShipEventScheduler</c>.
/// Verifies that the <see cref="ShipArrivedEvent.GoalId"/> still matches the ship's active goal
/// (stale wake-ups are silently ignored), then publishes a <see cref="ShipAutomationTickEvent"/>
/// to resume goal execution.
/// </summary>
public sealed class ShipArrivedEventHandler(
    IShipGoalRepository goals,
    ILogger<ShipArrivedEventHandler> logger)
{
    public async Task Handle(ShipArrivedEvent @event, IMessageBus bus, CancellationToken cancellationToken)
    {
        var activeGoal = await goals.GetActiveGoalAsync(@event.ShipSymbol, cancellationToken);

        if (activeGoal is null || activeGoal.GoalId != @event.GoalId)
        {
            logger.LogDebug(
                "ShipArrivedEventHandler: stale wake-up ignored for ship {Ship} (event GoalId={EventGoal}, active GoalId={ActiveGoal}).",
                @event.ShipSymbol,
                @event.GoalId,
                activeGoal?.GoalId);
            return;
        }

        logger.LogInformation(
            "ShipArrivedEventHandler: ship {Ship} arrived; resuming goal execution.",
            @event.ShipSymbol);

        await bus.PublishAsync(new ShipAutomationTickEvent(
            @event.ShipSymbol,
            "Arrived",
            @event.OccurredAt,
            @event.EventId,
            Guid.Empty));
    }
}
