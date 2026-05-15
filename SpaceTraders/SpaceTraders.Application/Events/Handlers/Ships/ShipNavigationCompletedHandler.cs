using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles <see cref="ShipNavigationCompletedEvent"/> by resuming goal execution.
/// Published by <see cref="NavigateToWaypointArrivedHandler"/> once the ship has
/// docked at its destination; this triggers the goal executor to advance the goal
/// (e.g. mark completed, begin next step).
/// </summary>
public sealed class ShipNavigationCompletedHandler(
    IShipGoalExecutorService goalExecutor,
    IDashboardNotifier dashboardNotifier,
    ILogger<ShipNavigationCompletedHandler> logger)
{
    public async Task Handle(ShipNavigationCompletedEvent @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "ShipNavigationCompletedHandler: navigation complete for ship {Ship} at {Destination}; resuming goal.",
            @event.ShipSymbol,
            @event.DestinationWaypoint);

        var result = await goalExecutor.ExecuteAsync(@event.ShipSymbol, cancellationToken);

        dashboardNotifier.Notify("ships", @event.ShipSymbol);
        dashboardNotifier.Notify("fleet-activity", @event.ShipSymbol);
        dashboardNotifier.Notify("activity", @event.ShipSymbol);
        dashboardNotifier.Notify("fleet-assignments", @event.ShipSymbol);
        dashboardNotifier.Notify("fleet-goal-chains", @event.ShipSymbol);

        if (result is not null)
        {
            logger.LogInformation(
                "ShipNavigationCompletedHandler: ship {Ship} goal resumed; outcome={Outcome} reason={Reason}.",
                @event.ShipSymbol,
                result.Outcome,
                result.Reason);
        }
    }
}
