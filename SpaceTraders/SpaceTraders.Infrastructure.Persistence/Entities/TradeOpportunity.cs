namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class TradeOpportunity
{
    public int Id { get; init; }

    public string AgentToken { get; init; } = string.Empty;

    required public string TradeSymbol { get; init; }

    required public string BuyWaypoint { get; init; }

    required public string SellWaypoint { get; init; }

    public int BuyPrice { get; init; }

    public int SellPrice { get; init; }

    public int ProfitPerUnit { get; init; }

    public int DistanceJumps { get; init; }

    public decimal ProfitPerJump { get; init; }

    public bool SupportsSupplyChain { get; init; }

    public int SupplyChainDepth { get; init; }

    public string BuyType { get; init; } = string.Empty;

    public string SellType { get; init; } = string.Empty;

    public int EffectiveTradeVolume { get; init; }

    public decimal EstimatedFuelCost { get; init; }

    public decimal EstimatedTravelTimeMinutes { get; init; }

    public decimal OpportunityCostPenalty { get; init; }

    public decimal CooldownPenalty { get; init; }

    public decimal RateLimitPenalty { get; init; }

    public decimal RouteScore { get; init; }

    public DateTimeOffset ComputedAt { get; init; }
}
