namespace SpaceTraders.Application.Automation;

public static partial class PlanTypes
{
    public const string MiningAutomation = "MiningAutomation";
    public const string TradingAutomation = "TradingAutomation";
}

public enum MarketAutomationOpportunityStatus
{
    None = 0,
    Pending = 1,
    Assigned = 2,
    Cancelled = 3,
}

public sealed record MiningAutomationPlanState
{
    public required Guid PlanId { get; init; }

    public required IReadOnlyList<MiningAutomationOpportunityState> Opportunities { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record MiningAutomationOpportunityState
{
    public required string OpportunityKey { get; init; }

    public required string TradeSymbol { get; init; }

    public required string SellWaypointSymbol { get; init; }

    public required MarketAutomationOpportunityStatus Status { get; init; }

    public string? AssignedShipSymbol { get; init; }

    public required DateTimeOffset FirstObservedAt { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    public string? StopReason { get; init; }
}

public sealed record TradingAutomationPlanState
{
    public required Guid PlanId { get; init; }

    public required IReadOnlyList<TradingAutomationOpportunityState> Opportunities { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record TradingAutomationOpportunityState
{
    public required string OpportunityKey { get; init; }

    public required string TradeSymbol { get; init; }

    public required string BuyWaypointSymbol { get; init; }

    public required string SellWaypointSymbol { get; init; }

    public required MarketAutomationOpportunityStatus Status { get; init; }

    public string? AssignedShipSymbol { get; init; }

    public required DateTimeOffset FirstObservedAt { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    public string? StopReason { get; init; }
}
