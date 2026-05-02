using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Phase 14d: handles <see cref="ShipCooldownExpiredEvent"/> fired by <c>ShipEventScheduler</c>.
/// Verifies that the <see cref="ShipCooldownExpiredEvent.GoalId"/> still matches the ship's active
/// goal (stale wake-ups are silently ignored), clears the persisted cooldown timestamp, then
/// publishes a <see cref="ShipAutomationTickEvent"/> to resume goal execution.
/// </summary>
public sealed class ShipCooldownExpiredEventHandler(
    IShipGoalRepository goals,
    IShipRepository ships,
    ILogger<ShipCooldownExpiredEventHandler> logger)
{
    public async Task Handle(ShipCooldownExpiredEvent @event, IMessageBus bus, CancellationToken cancellationToken)
    {
        var activeGoal = await goals.GetActiveGoalAsync(@event.ShipSymbol, cancellationToken);

        if (activeGoal is null || activeGoal.GoalId != @event.GoalId)
        {
            logger.LogDebug(
                "ShipCooldownExpiredEventHandler: stale wake-up ignored for ship {Ship} (event GoalId={EventGoal}, active GoalId={ActiveGoal}).",
                @event.ShipSymbol,
                @event.GoalId,
                activeGoal?.GoalId);
            return;
        }

        // Clear the persisted cooldown so the next tick sees no active cooldown.
        await ships.UpdateCooldownAsync(@event.ShipSymbol, null, cancellationToken);

        logger.LogInformation(
            "ShipCooldownExpiredEventHandler: ship {Ship} cooldown expired; resuming goal execution.",
            @event.ShipSymbol);

        await bus.PublishAsync(new ShipAutomationTickEvent(
            @event.ShipSymbol,
            "CooldownExpired",
            @event.OccurredAt,
            @event.EventId,
            Guid.Empty));
    }
}
