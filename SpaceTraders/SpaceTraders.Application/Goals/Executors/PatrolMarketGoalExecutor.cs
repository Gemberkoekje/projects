using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Phase 10: executor for <see cref="PatrolMarketGoal"/>.
/// Continuously visits the target market waypoint to keep its data fresh.
/// Never completes; the orchestrator must replace the goal to stop patrolling.
/// </summary>
public sealed class PatrolMarketGoalExecutor(
    IInOrbitCommandAcceptor inOrbitCommands,
    IDockedCommandAcceptor dockedCommands,
    IWaypointVisitService waypointVisit,
    IMarketRefreshService marketRefresh,
    IShipyardRefreshService shipyardRefresh,
    IMessageBus bus) : IShipGoalExecutor
{
    public bool CanExecute(ShipGoal goal) => goal is PatrolMarketGoal;

    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var patrolGoal = (PatrolMarketGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Ship is in transit.");
        }

        var atTarget = string.Equals(ship.WaypointSymbol, patrolGoal.TargetWaypointSymbol, StringComparison.OrdinalIgnoreCase);

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            if (!atTarget)
            {
                await dockedCommands.OrbitAsync(ship.Symbol, ct);
                return GoalExecutionResult.Progressing("Orbiting to navigate to patrol target.");
            }

            // At target and docked: refresh data then orbit to continue patrol.
            await waypointVisit.MarkVisitedAsync(ship.WaypointSymbol ?? string.Empty, ct);
            await marketRefresh.RefreshIfApplicableAsync(ship.WaypointSymbol ?? string.Empty, ct);
            await shipyardRefresh.RefreshIfApplicableAsync(ship.WaypointSymbol ?? string.Empty, ct);
            await dockedCommands.OrbitAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing("Market data refreshed; orbiting to continue patrol.");
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return GoalExecutionResult.Blocked($"Unexpected ship status: {ship.Status}.");
        }

        if (atTarget)
        {
            await inOrbitCommands.DockAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing("At patrol target; docking to refresh data.");
        }

        if (ship.FuelCapacity <= 0 && !string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, "DRIFT"), ct);
            return GoalExecutionResult.Progressing("No fuel tank; switching to DRIFT.");
        }

        if (ship.FuelCapacity > 0
            && !string.IsNullOrWhiteSpace(ctx.RecommendedFlightMode)
            && !string.Equals(ship.FlightMode, ctx.RecommendedFlightMode, StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, ctx.RecommendedFlightMode), ct);
            return GoalExecutionResult.Progressing($"Adjusting flight mode to {ctx.RecommendedFlightMode}.");
        }

        await inOrbitCommands.NavigateAsync(ship.Symbol, patrolGoal.TargetWaypointSymbol, ct);
        return GoalExecutionResult.Progressing($"Navigating to patrol target {patrolGoal.TargetWaypointSymbol}.");
    }
}
