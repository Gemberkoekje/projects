using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="WarpGoal"/> (PLAN.md slice 6.31, D100, D101): an explorer warps to a waypoint of another system,
/// as every warp goes (<see cref="GoalWarps"/>): fuel-safe, its tank filled first at a market, in BURN where the fuel pays for
/// it and in CRUISE otherwise. The goal ends in the destination's system, and the explore plan chooses the next step:
/// <list type="bullet">
///   <item>a warp the API refuses (<see cref="WarpRefusedException"/>) blocks the goal with <see cref="RefusedReason"/>; the
///   plan clears it and chooses again, and no warp goes to that system for an hour;</item>
///   <item>a warp that can't go fuel-safe, or into a system whose position isn't known, ends the goal, and the plan chooses
///   again: a goal that waited would look stuck (<c>ShipStuck</c>).</item>
/// </list>
/// </summary>
public sealed class WarpGoalExecutor(
    IShipGoalRepository goals,
    IGoalWarps warps,
    ILogger<WarpGoalExecutor> logger) : IShipGoalExecutor
{
    /// <summary>The <see cref="ShipGoal.StatusReason"/> of a warp the API refused.</summary>
    public const string RefusedReason = GoalWarps.RefusedReason;

    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is WarpGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var warp = (WarpGoal)goal;
        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival($"In transit to {warp.DestinationWaypointSymbol}.");
        }

        var destinationSystem = WaypointSymbols.SystemOf(warp.DestinationWaypointSymbol);
        if (string.Equals(ship.SystemSymbol, destinationSystem, StringComparison.OrdinalIgnoreCase))
        {
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            return GoalExecutionResult.Completed($"In {destinationSystem}.");
        }

        var step = await warps.WarpAsync(ship, warp.DestinationWaypointSymbol, ct);
        switch (step.Outcome)
        {
            case JumpStepOutcome.Refused:
                // Sent again, the warp would be refused again on every step: the explore plan chooses again.
                await goals.BlockGoalAsync(ship.Symbol, warp.GoalId, RefusedReason, ct);
                return step.Result;

            case JumpStepOutcome.NoWay:
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
                logger.LogDebug(
                    "WarpGoalExecutor: ship {ShipSymbol} stops its warp to {WaypointSymbol} ({Reason}); the explore plan chooses again.",
                    ship.Symbol,
                    warp.DestinationWaypointSymbol,
                    step.Result.Reason);
                return GoalExecutionResult.Progressing($"{step.Result.Reason} The explore plan chooses again.");

            default:
                return step.Result;
        }
    }
}
