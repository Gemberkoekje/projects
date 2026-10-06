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
/// ship that can only survey to the area where most drones mine (D54): a market out of its CRUISE reach, which it gets to the
/// fastest way, cruising as far as it can and drifting the rest (<see cref="GoalFlight.LegTowardsAsync"/>, D45, D84); its
/// arrival docks it there, and the survey plan gives it its next survey. The explore plan sends the command ship to the
/// shipyard that sells explorers this way (PLAN.md slice 6.30, D98): a waypoint in another system, which it gets to through the
/// built gates, as every flight between systems does (<see cref="GoalJumps"/>, D101). When no way is known any more, or a jump
/// would leave less than the credit floor, the goal ends and its plan chooses again; a jump the API refuses blocks the goal
/// (<see cref="GoalJumps.RefusedReason"/>), and the ways leave that gate alone for an hour.
/// </summary>
public sealed class MoveToWaypointGoalExecutor(
    IShipGoalRepository goals,
    ITradeContextReader tradeContexts,
    GoalJumps jumps,
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

        if (!string.Equals(ship.SystemSymbol, WaypointSymbols.SystemOf(move.TargetWaypointSymbol), StringComparison.OrdinalIgnoreCase))
        {
            return await AbroadAsync(ship, move, ct);
        }

        var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
        var (flown, leg) = await GoalFlight.LegTowardsAsync(context.Map, ship, move.TargetWaypointSymbol, dock, bus, ct);
        if (flown.Outcome == GoalExecutionOutcome.WaitingForArrival && leg.FlightMode == TradeRoutePlanner.DriftMode)
        {
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} drifts from {WaypointSymbol} to {Leg} on its way to {Destination}, out of its CRUISE reach, to work from there.",
                JournalEvents.DriftStarted,
                ship.Symbol,
                ship.WaypointSymbol ?? string.Empty,
                leg.WaypointSymbol,
                move.TargetWaypointSymbol);
        }

        return flown;
    }

    /// <summary>A step of the flight to a waypoint in another system: to the gate, or the jump from it (<see cref="GoalJumps"/>).</summary>
    private async Task<GoalExecutionResult> AbroadAsync(ShipModel ship, MoveToWaypointGoal move, CancellationToken ct)
    {
        var step = await jumps.TowardsAsync(ship, move.TargetWaypointSymbol, ct);
        switch (step.Outcome)
        {
            case JumpStepOutcome.NoWay:
            case JumpStepOutcome.ShortOfCredits:
                // A goal that waited here would look stuck (ShipStuck): its plan chooses again.
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
                logger.LogDebug(
                    "MoveToWaypointGoalExecutor: ship {ShipSymbol} stops its flight to {WaypointSymbol} ({Outcome}); its plan chooses again.",
                    ship.Symbol,
                    move.TargetWaypointSymbol,
                    step.Outcome);
                return GoalExecutionResult.Progressing($"{step.Result.Reason} Its plan chooses again.");

            case JumpStepOutcome.Refused:
                await goals.BlockGoalAsync(ship.Symbol, move.GoalId, GoalJumps.RefusedReason, ct);
                return step.Result;

            default:
                return step.Result;
        }
    }
}
