using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Phase 10: executor for <see cref="IdleGoal"/>. Always returns <see cref="GoalExecutionOutcome.WaitingForArrival"/>
/// so the ship stays idle until the orchestrator assigns a new goal.
/// </summary>
public sealed class IdleGoalExecutor : IShipGoalExecutor
{
    public bool CanExecute(ShipGoal goal) => goal is IdleGoal;

    public Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
        => Task.FromResult(GoalExecutionResult.WaitingForArrival("Ship is idle; waiting for a new goal."));
}
