using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IMarketRepository
{
    /// <summary>
    /// When the bot last stored the market's prices; null when the cache has none: never fetched, or fetched only without
    /// them (B62). So the market watch fetches such a market as soon as one of our ships is there.
    /// </summary>
    Task<DateTimeOffset?> GetLastObservedAtAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    Task UpsertAsync(MarketDataModel market, CancellationToken cancellationToken = default);

    Task<MarketSnapshot?> FindSnapshotByWaypointAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketSnapshot>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns freshness data (last observed timestamp) for all known market waypoints.</summary>
    Task<IReadOnlyList<MarketFreshnessRecord>> GetAllFreshnessAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Lightweight freshness record for the /markets/freshness endpoint: when the market was last stored, and whether the cache
/// holds its prices. The probe plan counts a market without them as never seen (B62).
/// </summary>
public sealed record MarketFreshnessRecord
{
    /// <summary>The market's waypoint.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>The market's system.</summary>
    public required string SystemSymbol { get; init; }

    /// <summary>When the bot last stored the market.</summary>
    public required DateTimeOffset LastObservedAt { get; init; }

    /// <summary>Whether the cache holds the market's prices.</summary>
    public bool HasPrices { get; init; } = true;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MarketFreshnessRecord(string WaypointSymbol, string SystemSymbol, DateTimeOffset LastObservedAt, bool HasPrices = true)
    {
        this.WaypointSymbol = WaypointSymbol;
        this.SystemSymbol = SystemSymbol;
        this.LastObservedAt = LastObservedAt;
        this.HasPrices = HasPrices;
    }
}
