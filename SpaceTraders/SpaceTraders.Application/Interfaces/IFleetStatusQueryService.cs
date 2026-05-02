using SpaceTraders.Application.Orchestration;

namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Read-model service that returns a point-in-time view of every active fleet goal,
/// including resource-delivery progress and which ships are assigned to each need.
/// </summary>
/// <remarks>Phase 15a: introduced as part of the observability read-model layer.</remarks>
public interface IFleetStatusQueryService
{
    /// <summary>
    /// Returns the list of active fleet goal chains sorted by priority ascending.
    /// Each chain describes what the orchestrator is working towards and, for Contract /
    /// Construction goals, the per-resource delivery progress.
    /// </summary>
    Task<IReadOnlyList<OrchestratorGoalChain>> GetGoalChainsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a snapshot of every ship's current assignment, including the fleet goal
    /// each ship is serving (if any).
    /// </summary>
    /// <remarks>Phase 15c: introduced as part of the observability read-model layer.</remarks>
    Task<IReadOnlyList<ShipAssignmentSnapshot>> GetAssignmentsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a live activity snapshot for every ship in the fleet.
    /// </summary>
    /// <remarks>Phase 15e: introduced as part of the observability read-model layer.</remarks>
    Task<IReadOnlyList<ShipActivitySnapshot>> GetShipActivitiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a live activity snapshot for the ship identified by <paramref name="shipSymbol"/>,
    /// or <c>null</c> if no such ship is known.
    /// </summary>
    /// <remarks>Phase 15e: introduced as part of the observability read-model layer.</remarks>
    Task<ShipActivitySnapshot?> GetShipActivityAsync(string shipSymbol, CancellationToken ct = default);
}
