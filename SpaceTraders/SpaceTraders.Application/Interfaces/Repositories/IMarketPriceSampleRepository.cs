namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IMarketPriceSampleRepository
{
    Task AppendSamplesAsync(string waypointSymbol, string tradeGoodsJson, CancellationToken cancellationToken = default);

    /// <summary>Returns price samples for the given good symbol, optionally filtered by waypoint, in the given time range.</summary>
    Task<IReadOnlyList<MarketPriceSampleDto>> GetGoodPricesAsync(
        string goodSymbol,
        string? waypointSymbol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all price samples for all goods at the given waypoint in the given time range.</summary>
    Task<IReadOnlyList<MarketPriceSampleDto>> GetWaypointPricesAsync(
        string waypointSymbol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prunes old market price samples.
    /// Rows between <paramref name="aggregateRetentionCutoff"/> and <paramref name="rawRetentionCutoff"/>
    /// are downsampled to one row per (waypoint, good, hour) (the earliest in each bucket is kept).
    /// Rows older than <paramref name="aggregateRetentionCutoff"/> are deleted entirely.
    /// </summary>
    /// <returns>Total number of rows deleted.</returns>
    Task<int> PruneAsync(DateTimeOffset rawRetentionCutoff, DateTimeOffset aggregateRetentionCutoff, CancellationToken cancellationToken = default);
}

public sealed record MarketPriceSampleDto(
    DateTimeOffset ObservedAt,
    string WaypointSymbol,
    string GoodSymbol,
    int PurchasePrice,
    int SellPrice,
    string? Supply,
    string? Activity,
    int TradeVolume);
