using SpaceTraders.Application.Orchestration;

namespace SpaceTraders.Application.Interfaces.Repositories;

/// <summary>
/// Persists and retrieves fleet-level goals in the <c>fleet_goals</c> table,
/// ensuring active goals survive process restarts.
/// </summary>
/// <remarks>Phase 11e: introduced as part of the goal-driven architecture migration.</remarks>
public interface IFleetGoalRepository
{
    /// <summary>
    /// Inserts or updates the fleet goal record identified by <see cref="FleetGoal.Id"/>.
    /// </summary>
    Task UpsertAsync(FleetGoal goal, CancellationToken ct = default);

    /// <summary>
    /// Sets <c>completed_at</c> to the current UTC time for the goal with the given identifier.
    /// </summary>
    Task MarkCompletedAsync(Guid goalId, CancellationToken ct = default);

    /// <summary>
    /// Returns all goals that have not yet been marked completed.
    /// </summary>
    Task<IReadOnlyList<FleetGoal>> GetActiveAsync(CancellationToken ct = default);
}
