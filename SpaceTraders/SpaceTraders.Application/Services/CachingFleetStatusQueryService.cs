using Microsoft.Extensions.Caching.Memory;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Orchestration;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Caching decorator for <see cref="IFleetStatusQueryService"/> that serves results from an
/// in-memory cache for up to <see cref="Ttl"/> seconds before delegating to the inner service.
/// </summary>
/// <remarks>Phase 16c: short-lived cache so dashboard polling does not overload the database.</remarks>
internal sealed class CachingFleetStatusQueryService(
    FleetStatusQueryService inner,
    IMemoryCache cache) : IFleetStatusQueryService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    private const string GoalChainsKey = "fleet:goal-chains";
    private const string AssignmentsKey = "fleet:assignments";
    private const string ActivitiesKey = "fleet:activities";
    private const string ActivityKeyPrefix = "fleet:activity:";

    /// <inheritdoc/>
    public Task<IReadOnlyList<OrchestratorGoalChain>> GetGoalChainsAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync(GoalChainsKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return inner.GetGoalChainsAsync(ct);
        }) ?? Task.FromResult<IReadOnlyList<OrchestratorGoalChain>>([]);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ShipAssignmentSnapshot>> GetAssignmentsAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync(AssignmentsKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return inner.GetAssignmentsAsync(ct);
        }) ?? Task.FromResult<IReadOnlyList<ShipAssignmentSnapshot>>([]);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ShipActivitySnapshot>> GetShipActivitiesAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync(ActivitiesKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return inner.GetShipActivitiesAsync(ct);
        }) ?? Task.FromResult<IReadOnlyList<ShipActivitySnapshot>>([]);

    /// <inheritdoc/>
    public Task<ShipActivitySnapshot?> GetShipActivityAsync(string shipSymbol, CancellationToken ct = default)
        => cache.GetOrCreateAsync(ActivityKeyPrefix + shipSymbol, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return inner.GetShipActivityAsync(shipSymbol, ct);
        });
}
