using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="MoveToWaypointGoal"/>: one flight to a waypoint, and the goal ends there. The survey plan moves a
/// ship that can only survey to the area where most drones mine (D54): a market out of its CRUISE reach, which it drifts to
/// (<see cref="GoalFlight.DriftAsync"/>, D45); its arrival docks it there, and the survey plan gives it its next survey.
/// </summary>
public sealed class MoveToWaypointGoalExecutor(
    IShipGoalRepository goals,
    ITradeContextReader tradeContexts,
    IDockSubCommand dock,
    IMessageBus bus,
    ILogger<MoveToWaypointGoalExecutor> logger) : IShipGoalExecutor
{
    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is MoveToWaypointGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var move = (MoveToWaypointGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival($"In transit to {move.TargetWaypointSymbol}.");
        }

        if (string.Equals(ship.WaypointSymbol, move.TargetWaypointSymbol, StringComparison.OrdinalIgnoreCase))
        {
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            return GoalExecutionResult.Completed($"Arrived at {move.TargetWaypointSymbol}.");
        }

        if (move.Drifting)
        {
            var drifting = await GoalFlight.DriftAsync(ship, move.TargetWaypointSymbol, bus, ct);
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} drifts from {WaypointSymbol} to {Destination}, out of its CRUISE reach, to work from there.",
                JournalEvents.DriftStarted,
                ship.Symbol,
                ship.WaypointSymbol ?? string.Empty,
                move.TargetWaypointSymbol);
            return drifting;
        }

        var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
        return await GoalFlight.TowardsAsync(context.Map, ship, move.TargetWaypointSymbol, dock, bus, ct);
    }
}
