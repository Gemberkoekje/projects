namespace SpaceTraders.Infrastructure.Persistence.Entities;

/// <summary>Another agent's transaction a market listed (PLAN.md slice 6.47, D133).</summary>
public sealed class MarketTrade
{
    public long Id { get; init; }

    public string AgentId { get; init; } = string.Empty;

    required public string SystemSymbol { get; init; }

    required public string WaypointSymbol { get; init; }

    required public string ShipSymbol { get; init; }

    required public string TradeSymbol { get; init; }

    required public string Type { get; init; }

    public int Units { get; init; }

    public long PricePerUnit { get; init; }

    public DateTimeOffset Timestamp { get; init; }
}
