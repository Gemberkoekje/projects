using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IMarketRepository
{
    Task<DateTimeOffset?> GetLastObservedAtAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    Task UpsertAsync(MarketDataModel market, CancellationToken cancellationToken = default);

    Task<MarketSnapshot?> FindSnapshotByWaypointAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketSnapshot>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns freshness data (last observed timestamp) for all known market waypoints.</summary>
    Task<IReadOnlyList<MarketFreshnessRecord>> GetAllFreshnessAsync(CancellationToken cancellationToken = default);
}

/// <summary>Lightweight freshness record for the /markets/freshness endpoint.</summary>
public sealed record MarketFreshnessRecord(string WaypointSymbol, string SystemSymbol, DateTimeOffset LastObservedAt);
