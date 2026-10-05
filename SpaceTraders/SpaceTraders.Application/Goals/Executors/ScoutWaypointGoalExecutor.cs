using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="ScoutWaypointGoal"/>.
/// Flies to the target waypoint (<see cref="GoalFlight"/>: through refuelling stops, B47, in BURN where that strands nothing, D84),
/// then marks the waypoint visited once docked.
/// </summary>
public sealed class ScoutWaypointGoalExecutor(
    IWaypointVisitService waypointVisit,
    ITradeContextReader tradeContexts,
    IDockSubCommand dock,
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
            await dock.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing($"Docking at scout target {scoutGoal.TargetWaypointSymbol}.");
        }

        var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
        return await GoalFlight.TowardsAsync(context.Map, ship, scoutGoal.TargetWaypointSymbol, dock, bus, ct);
    }
}
