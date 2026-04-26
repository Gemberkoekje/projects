namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedMarket
{
    public string AgentToken { get; init; } = string.Empty;

    required public string WaypointSymbol { get; init; }

    required public string SystemSymbol { get; init; }

    public string? ImportsJson { get; init; }

    public string? ExportsJson { get; init; }

    public string? ExchangeJson { get; init; }

    public string? TradeGoodsJson { get; init; }

    public DateTimeOffset LastObservedAt { get; init; }
}
