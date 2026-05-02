using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Phase 10: executor for <see cref="ScoutWaypointGoal"/>.
/// Navigates to the target waypoint, docks, and refreshes market/shipyard/waypoint data.
/// Completes after a single successful data-refresh cycle.
/// </summary>
public sealed class ScoutWaypointGoalExecutor(
    IInOrbitCommandAcceptor inOrbitCommands,
    IDockedCommandAcceptor dockedCommands,
    IWaypointVisitService waypointVisit,
    IMarketRefreshService marketRefresh,
    IShipyardRefreshService shipyardRefresh,
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

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            if (atTarget)
            {
                await waypointVisit.MarkVisitedAsync(ship.WaypointSymbol ?? string.Empty, ct);
                await marketRefresh.RefreshIfApplicableAsync(ship.WaypointSymbol ?? string.Empty, ct);
                await shipyardRefresh.RefreshIfApplicableAsync(ship.WaypointSymbol ?? string.Empty, ct);
                return GoalExecutionResult.Completed("Scout data refreshed at target waypoint.");
            }

            await dockedCommands.OrbitAsync(ship.Symbol, ct);
        }
        else if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return GoalExecutionResult.Blocked($"Unexpected ship status: {ship.Status}.");
        }

        // Effectively in orbit now.
        if (atTarget)
        {
            await inOrbitCommands.DockAsync(ship.Symbol, ct);
            await waypointVisit.MarkVisitedAsync(scoutGoal.TargetWaypointSymbol, ct);
            await marketRefresh.RefreshIfApplicableAsync(scoutGoal.TargetWaypointSymbol, ct);
            await shipyardRefresh.RefreshIfApplicableAsync(scoutGoal.TargetWaypointSymbol, ct);
            return GoalExecutionResult.Completed("Scout data refreshed at target waypoint.");
        }

        if (ship.FuelCapacity <= 0 && !string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, "DRIFT"), ct);
        }
        else if (ship.FuelCapacity > 0
            && !string.IsNullOrWhiteSpace(ctx.RecommendedFlightMode)
            && !string.Equals(ship.FlightMode, ctx.RecommendedFlightMode, StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, ctx.RecommendedFlightMode), ct);
        }

        await inOrbitCommands.NavigateAsync(ship.Symbol, scoutGoal.TargetWaypointSymbol, ct);
        return GoalExecutionResult.WaitingForArrival($"Navigating to scout target {scoutGoal.TargetWaypointSymbol}.");
    }
}
