namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IWaypointRepository
{
    Task<IReadOnlyList<WaypointCacheModel>> GetBySystemAsync(string systemSymbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WaypointCacheModel>> GetUnscoutedOrStaleAsync(string systemSymbol, TimeSpan staleness, CancellationToken cancellationToken = default);
    Task MarkVisitedAsync(string waypointSymbol, CancellationToken cancellationToken = default);
}

public record WaypointCacheModel(
    string Symbol,
    string SystemSymbol,
    string Type,
    int X,
    int Y,
    bool HasMarket,
    bool HasShipyard,
    DateTimeOffset LastObservedAt);
