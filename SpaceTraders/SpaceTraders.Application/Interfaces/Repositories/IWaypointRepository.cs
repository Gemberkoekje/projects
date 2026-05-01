namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IWaypointRepository
{
    Task<WaypointCacheModel?> FindAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WaypointCacheModel>> GetBySystemAsync(string systemSymbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WaypointCacheModel>> GetUnscoutedOrStaleAsync(string systemSymbol, TimeSpan staleness, CancellationToken cancellationToken = default);
    Task UpsertRangeAsync(IReadOnlyList<WaypointCacheModel> waypoints, CancellationToken cancellationToken = default);
    Task MarkVisitedAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    /// <summary>Returns the distinct system symbols for which at least one waypoint has been cached.</summary>
    Task<IReadOnlyList<string>> GetVisitedSystemSymbolsAsync(CancellationToken cancellationToken = default);
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

    public string? TraitsJson { get; init; }

    public string? ModifiersJson { get; init; }

    public string? OrbitalsJson { get; init; }

    public string? ParentSymbol { get; init; }

    public bool IsUnderConstruction { get; init; }

    public string? ChartJson { get; init; }

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
        DateTimeOffset LastObservedAt,
        string? TraitsJson = null,
        string? ModifiersJson = null,
        string? OrbitalsJson = null,
        string? ParentSymbol = null,
        bool IsUnderConstruction = false,
        string? ChartJson = null)
    {
        this.Symbol = Symbol;
        this.SystemSymbol = SystemSymbol;
        this.Type = Type;
        this.X = X;
        this.Y = Y;
        this.HasMarket = HasMarket;
        this.HasShipyard = HasShipyard;
        this.LastObservedAt = LastObservedAt;
        this.TraitsJson = TraitsJson;
        this.ModifiersJson = ModifiersJson;
        this.OrbitalsJson = OrbitalsJson;
        this.ParentSymbol = ParentSymbol;
        this.IsUnderConstruction = IsUnderConstruction;
        this.ChartJson = ChartJson;
    }
}
