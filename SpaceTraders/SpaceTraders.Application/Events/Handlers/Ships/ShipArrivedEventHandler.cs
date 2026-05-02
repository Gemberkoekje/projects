using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles <see cref="ShipArrivedEvent"/> fired by <c>ShipEventScheduler</c>.
/// Verifies that the <see cref="ShipArrivedEvent.GoalId"/> still matches the ship's active goal
/// (stale wake-ups are silently ignored), then calls the goal executor directly.
/// </summary>
public sealed class ShipArrivedEventHandler(
    IShipGoalRepository goals,
    ILogger<ShipArrivedEventHandler> logger)
{
    public async Task Handle(ShipArrivedEvent @event, IShipGoalExecutorService goalExecutor, CancellationToken cancellationToken)
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

        var result = await goalExecutor.ExecuteAsync(@event.ShipSymbol, cancellationToken);

        if (result is not null)
        {
            logger.LogInformation(
                "ShipArrivedEventHandler: ship {Ship} resumed after arrival; outcome={Outcome} reason={Reason}",
                @event.ShipSymbol,
                result.Outcome,
                result.Reason);
        }
    }
}
