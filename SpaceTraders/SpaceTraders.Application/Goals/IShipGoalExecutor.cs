using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Goals;

/// <summary>
/// Autonomously executes one step toward a concrete ship goal.
/// Each call issues at most one API action and returns the outcome so the
/// <see cref="IShipGoalExecutorService"/> can handle goal lifecycle events.
/// </summary>
/// <remarks>Phase 10: replaces <c>IShipPlanner</c> for the goal layer.</remarks>
public interface IShipGoalExecutor
{
    /// <summary>Returns <c>true</c> when this executor handles the given goal type.</summary>
    bool CanExecute(ShipGoal goal);

    /// <summary>
    /// Evaluates the ship's current state against the goal and issues one API action if
    /// appropriate. Returns a <see cref="GoalExecutionResult"/> describing what happened.
    /// </summary>
    Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct);
}

/// <summary>
/// Orchestrates goal selection, context building, executor dispatch, and goal lifecycle events.
/// </summary>
/// <remarks>Phase 10: replaces <c>IShipPlannerService</c> when an active goal is present.</remarks>
public interface IShipGoalExecutorService
{
    /// <summary>
    /// Executes the next step for the named ship's active goal. Falls back to the legacy planner
    /// service when the ship has no active goal (backward-compatibility bridge).
    /// </summary>
    Task<GoalExecutionResult?> ExecuteAsync(string shipSymbol, CancellationToken ct);
}
