using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IMarketRepository
{
    Task<DateTimeOffset?> GetLastObservedAtAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    Task UpsertAsync(MarketDataModel market, CancellationToken cancellationToken = default);

    Task<MarketSnapshot?> FindSnapshotByWaypointAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketSnapshot>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default);
}
