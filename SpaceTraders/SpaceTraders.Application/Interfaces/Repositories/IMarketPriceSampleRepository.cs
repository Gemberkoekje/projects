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
