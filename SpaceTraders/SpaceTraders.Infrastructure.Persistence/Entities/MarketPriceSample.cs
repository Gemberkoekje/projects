namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class MarketPriceSample
{
    public long Id { get; init; }

    public string AgentToken { get; init; } = string.Empty;

    required public string WaypointSymbol { get; init; }

    required public string GoodSymbol { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public int PurchasePrice { get; init; }

    public int SellPrice { get; init; }

    public string? Supply { get; init; }

    public string? Activity { get; init; }

    public int TradeVolume { get; init; }
}
