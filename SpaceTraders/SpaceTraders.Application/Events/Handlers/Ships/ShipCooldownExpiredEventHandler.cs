using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles <see cref="ShipCooldownExpiredEvent"/> fired by <c>ShipEventScheduler</c>.
/// Verifies that the <see cref="ShipCooldownExpiredEvent.GoalId"/> still matches the ship's active
/// goal (stale wake-ups are silently ignored), clears the persisted cooldown timestamp, then
/// calls the goal executor directly.
/// </summary>
public sealed class ShipCooldownExpiredEventHandler(
    IShipGoalRepository goals,
    IShipRepository ships,
    ILogger<ShipCooldownExpiredEventHandler> logger)
{
    public async Task Handle(ShipCooldownExpiredEvent @event, IShipGoalExecutorService goalExecutor, CancellationToken cancellationToken)
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

        var result = await goalExecutor.ExecuteAsync(@event.ShipSymbol, cancellationToken);

        if (result is not null)
        {
            logger.LogInformation(
                "ShipCooldownExpiredEventHandler: ship {Ship} resumed after cooldown; outcome={Outcome} reason={Reason}",
                @event.ShipSymbol,
                result.Outcome,
                result.Reason);
        }
    }
}
