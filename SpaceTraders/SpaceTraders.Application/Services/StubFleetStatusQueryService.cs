using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Orchestration;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Stub implementation of <see cref="IFleetStatusQueryService"/> that always returns empty lists.
/// The concrete implementation is provided in Phase 15b / 15d.
/// </summary>
/// <remarks>Phase 15a: stub registered so the application can start without a full implementation.</remarks>
internal sealed class StubFleetStatusQueryService : IFleetStatusQueryService
{
    public Task<IReadOnlyList<OrchestratorGoalChain>> GetGoalChainsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrchestratorGoalChain>>(Array.Empty<OrchestratorGoalChain>());

    public Task<IReadOnlyList<ShipAssignmentSnapshot>> GetAssignmentsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ShipAssignmentSnapshot>>(Array.Empty<ShipAssignmentSnapshot>());

    public Task<IReadOnlyList<ShipActivitySnapshot>> GetShipActivitiesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ShipActivitySnapshot>>(Array.Empty<ShipActivitySnapshot>());

    public Task<ShipActivitySnapshot?> GetShipActivityAsync(string shipSymbol, CancellationToken ct = default)
        => Task.FromResult<ShipActivitySnapshot?>(null);
}
