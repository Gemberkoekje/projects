using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Interfaces.Repositories;

/// <summary>
/// Persists the active goal for each ship, enabling goal execution to survive restarts and
/// allowing the orchestrator to query which ships have active goals.
/// </summary>
/// <remarks>Phase 8: introduced as part of the goal-driven architecture migration.</remarks>
public interface IShipGoalRepository
{
    /// <summary>Returns the active goal for the given ship, or <c>null</c> if the ship has no active goal.</summary>
    Task<ShipGoal?> GetActiveGoalAsync(string shipSymbol, CancellationToken cancellationToken = default);

    /// <summary>Persists <paramref name="goal"/> as the active goal for the given ship, replacing any prior goal.</summary>
    Task SetActiveGoalAsync(string shipSymbol, ShipGoal goal, CancellationToken cancellationToken = default);

    /// <summary>Removes the active goal from the given ship, leaving it without an assigned goal.</summary>
    Task ClearActiveGoalAsync(string shipSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the <see cref="GoalStatus"/> of the active goal identified by <paramref name="goalId"/>.
    /// No-op if the ship has no active goal or if the active goal id does not match.
    /// </summary>
    Task UpdateGoalStatusAsync(string shipSymbol, Guid goalId, GoalStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the active goal identified by <paramref name="goalId"/> as <see cref="GoalStatus.Blocked"/>
    /// and records <paramref name="reason"/> as its <see cref="ShipGoal.StatusReason"/>.
    /// No-op if the ship has no active goal or if the active goal id does not match.
    /// </summary>
    Task BlockGoalAsync(string shipSymbol, Guid goalId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the set of waypoint symbols currently targeted by active <see cref="ScoutWaypointGoal"/> goals
    /// across all ships.
    /// </summary>
    Task<IReadOnlySet<string>> GetActiveScoutTargetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active <see cref="TradeBetweenMarketsGoal"/> of every ship that has one, by ship symbol, with its
    /// stored status: what the fleet's trade trips hold back for their cargo (D57).
    /// </summary>
    Task<IReadOnlyDictionary<string, TradeBetweenMarketsGoal>> GetActiveTradeGoalsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active <see cref="SupplyConstructionGoal"/> of every ship that has one, by ship symbol, with its stored
    /// status: what the fleet's construction trips carry or go to buy, and hold back for their cargo (slice 6.6, D59).
    /// </summary>
    Task<IReadOnlyDictionary<string, SupplyConstructionGoal>> GetActiveConstructionGoalsAsync(CancellationToken cancellationToken = default);
}
