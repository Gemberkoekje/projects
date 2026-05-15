using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="ScoutWaypointGoal"/>.
/// Navigates to the target waypoint via <see cref="NavigateToWaypointCommand"/>, 
/// then marks the waypoint visited once docked.
/// </summary>
public sealed class ScoutWaypointGoalExecutor(
    IWaypointVisitService waypointVisit,
    IMessageBus bus) : IShipGoalExecutor
{
    public bool CanExecute(ShipGoal goal) => goal is ScoutWaypointGoal;

    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var scoutGoal = (ScoutWaypointGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Ship is in transit.");
        }

        var atTarget = string.Equals(ship.WaypointSymbol, scoutGoal.TargetWaypointSymbol, StringComparison.OrdinalIgnoreCase);

        if (atTarget && ship.LocalStatus == ShipLocalStatus.Docked)
        {
            await waypointVisit.MarkVisitedAsync(ship.WaypointSymbol ?? string.Empty, ct);
            return GoalExecutionResult.Completed("Scout waypoint visited.");
        }

        if (atTarget && ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, scoutGoal.TargetWaypointSymbol), ct);
            return GoalExecutionResult.WaitingForArrival($"Docking at scout target {scoutGoal.TargetWaypointSymbol}.");
        }

        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, scoutGoal.TargetWaypointSymbol), ct);
        return GoalExecutionResult.WaitingForArrival($"Navigating to scout target {scoutGoal.TargetWaypointSymbol}.");
    }
}
