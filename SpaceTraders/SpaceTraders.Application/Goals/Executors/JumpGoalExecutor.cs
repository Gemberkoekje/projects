using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="JumpGoal"/> (exploring, asked on 2026-10-04): the ship flies to the gate of the system it is in,
/// and jumps from there to the destination gate, which buys one ANTIMATTER at the gate's market. The jump is every flight's
/// (<see cref="GoalJumps"/>, PLAN.md slice 6.28):
/// <list type="bullet">
///   <item>It jumps only while the credits after the jump stay at or above the floor every ship purchase keeps
///   (<see cref="CreditReserve.FloorSetting"/>, 60,000 seeded). The explore plan gives the goal only then; should the
///   price have risen since, the goal ends, and the plan holds the jump until the credits allow it.</item>
///   <item>It waits out the ship's cooldown: the API refuses a jump during one.</item>
///   <item>Docked at a gate whose market sells fuel, it fills the tank first, for the system it goes to.</item>
///   <item>A jump the API refuses (<see cref="JumpRefusedException"/>) blocks the goal with <see cref="RefusedReason"/>: the
///   explore plan then leaves that gate alone for a while and chooses again.</item>
/// </list>
/// The goal ends in the destination's system.
/// </summary>
public sealed class JumpGoalExecutor(
    IShipGoalRepository goals,
    ITradeContextReader tradeContexts,
    GoalJumps jumps,
    IDockSubCommand dock,
    IMessageBus bus) : IShipGoalExecutor
{
    /// <summary>The <see cref="ShipGoal.StatusReason"/> of a jump the API refused.</summary>
    public const string RefusedReason = GoalJumps.RefusedReason;

    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is JumpGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var jump = (JumpGoal)goal;
        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival($"In transit to {jump.GateWaypointSymbol}.");
        }

        var destinationSystem = WaypointSymbols.SystemOf(jump.DestinationGateWaypointSymbol);
        if (string.Equals(ship.SystemSymbol, destinationSystem, StringComparison.OrdinalIgnoreCase))
        {
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            return GoalExecutionResult.Completed($"In {destinationSystem}.");
        }

        if (!string.Equals(ship.WaypointSymbol, jump.GateWaypointSymbol, StringComparison.OrdinalIgnoreCase))
        {
            var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
            return await GoalFlight.TowardsAsync(context.Map, ship, jump.GateWaypointSymbol, dock, bus, ct);
        }

        var step = await jumps.JumpAsync(ship, jump.GateWaypointSymbol, jump.DestinationGateWaypointSymbol, ct);
        switch (step.Outcome)
        {
            case JumpStepOutcome.ShortOfCredits:
                // A goal that waited here would look stuck (ShipStuck): the explore plan holds the jump instead, and says so.
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
                return GoalExecutionResult.Progressing($"Not enough credits to jump to {jump.DestinationGateWaypointSymbol}; the explore plan waits for them.");

            case JumpStepOutcome.Refused:
                // Sent again, the jump would be refused again on every step: the explore plan chooses again.
                await goals.BlockGoalAsync(ship.Symbol, jump.GoalId, RefusedReason, ct);
                return step.Result;

            case JumpStepOutcome.Jumped:
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
                return GoalExecutionResult.Completed($"Jumped to {jump.DestinationGateWaypointSymbol}.");

            default:
                return step.Result;
        }
    }
}
