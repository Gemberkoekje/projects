using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles <see cref="ShipArrivedEvent"/> fired by <c>ShipEventScheduler</c>.
/// Verifies that the <see cref="ShipArrivedEvent.GoalId"/> still matches the ship's active goal
/// (stale wake-ups are silently ignored), then publishes <see cref="NavigateToWaypointArrivedCommand"/>
/// to continue the navigate journey's post-arrival phase.
/// </summary>
public sealed class ShipArrivedEventHandler(
    IShipGoalRepository goals,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<ShipArrivedEventHandler> logger)
{
    public async Task Handle(ShipArrivedEvent @event, CancellationToken cancellationToken)
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
            "ShipArrivedEventHandler: ship {Ship} arrived; dispatching post-arrival command.",
            @event.ShipSymbol);

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        var destination = ship?.WaypointSymbol ?? string.Empty;

        await bus.PublishAsync(new NavigateToWaypointArrivedCommand(
            @event.ShipSymbol,
            destination,
            @event.GoalId));
    }
}
