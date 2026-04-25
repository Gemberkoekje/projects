namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedMarket
{
    public required string WaypointSymbol { get; set; }

    public required string SystemSymbol { get; set; }

    public string? ImportsJson { get; set; }

    public string? ExportsJson { get; set; }

    public string? ExchangeJson { get; set; }

    public string? TradeGoodsJson { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }
}
