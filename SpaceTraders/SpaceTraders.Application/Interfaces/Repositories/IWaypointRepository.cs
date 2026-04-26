namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IWaypointRepository
{
    Task<WaypointCacheModel?> FindAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WaypointCacheModel>> GetBySystemAsync(string systemSymbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WaypointCacheModel>> GetUnscoutedOrStaleAsync(string systemSymbol, TimeSpan staleness, CancellationToken cancellationToken = default);
    Task UpsertRangeAsync(IReadOnlyList<WaypointCacheModel> waypoints, CancellationToken cancellationToken = default);
    Task MarkVisitedAsync(string waypointSymbol, CancellationToken cancellationToken = default);
}

public sealed record WaypointCacheModel
{
    public required string Symbol { get; init; }

    public required string SystemSymbol { get; init; }

    public required string Type { get; init; }

    public required int X { get; init; }

    public required int Y { get; init; }

    public required bool HasMarket { get; init; }

    public required bool HasShipyard { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public WaypointCacheModel(
        string Symbol,
        string SystemSymbol,
        string Type,
        int X,
        int Y,
        bool HasMarket,
        bool HasShipyard,
        DateTimeOffset LastObservedAt)
    {
        this.Symbol = Symbol;
        this.SystemSymbol = SystemSymbol;
        this.Type = Type;
        this.X = X;
        this.Y = Y;
        this.HasMarket = HasMarket;
        this.HasShipyard = HasShipyard;
        this.LastObservedAt = LastObservedAt;
    }
}
