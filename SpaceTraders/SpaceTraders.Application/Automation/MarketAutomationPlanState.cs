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

/// <summary>
/// A low-supply opening in the mining plan's view (slice 6.4, D22): a market with an ore in low supply, and
/// the asteroid nearest it that yields the ore.
/// </summary>
public sealed record MiningAutomationOpportunityState
{
    /// <summary>The opening's key: sell market and ore.</summary>
    public required string OpportunityKey { get; init; }

    public required string TradeSymbol { get; init; }

    public required string SellWaypointSymbol { get; init; }

    /// <summary>The asteroid nearest the market whose traits yield the ore.</summary>
    public string SourceWaypointSymbol { get; init; } = string.Empty;

    /// <summary>Assigned while a miner's trip sells the ore there; Pending otherwise.</summary>
    public required MarketAutomationOpportunityStatus Status { get; init; }

    public string? AssignedShipSymbol { get; init; }

    /// <summary>
    /// For a pending opening: the miners without a trip that could reach its asteroid. The <c>ShipLeftIdle</c>
    /// rule reads it (D13).
    /// </summary>
    public IReadOnlyList<string> CandidateShipSymbols { get; init; } = [];

    public required DateTimeOffset FirstObservedAt { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    public string? StopReason { get; init; }
}

/// <summary>
/// The trading plan's view after its last pass (slice 6.5): the routes its traders hold, and the
/// lucrative routes no trader holds. Written only when it changes.
/// </summary>
public sealed record TradingAutomationPlanState
{
    public required Guid PlanId { get; init; }

    /// <summary>The held routes (Assigned) and the lucrative routes without a trader (Pending).</summary>
    public required IReadOnlyList<TradingAutomationOpportunityState> Opportunities { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One trade route in the trading plan's view.</summary>
public sealed record TradingAutomationOpportunityState
{
    /// <summary>The route's key: buy market, sell market and good.</summary>
    public required string OpportunityKey { get; init; }

    public required string TradeSymbol { get; init; }

    public required string BuyWaypointSymbol { get; init; }

    public required string SellWaypointSymbol { get; init; }

    /// <summary>Assigned while a trader holds the route; Pending while it is lucrative and no trader holds it.</summary>
    public required MarketAutomationOpportunityStatus Status { get; init; }

    public string? AssignedShipSymbol { get; init; }

    public required DateTimeOffset FirstObservedAt { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    public string? StopReason { get; init; }

    /// <summary>The units a trip carries: for a held route, the trader's plan; for a pending one, its best candidate's.</summary>
    public int Units { get; init; }

    /// <summary>What a trip earns after fuel, as estimated: for a pending route, for its best candidate.</summary>
    public long ExpectedProfit { get; init; }

    /// <summary>The pricier good the sell market makes from the good, or empty (D15).</summary>
    public string FeedsTradeSymbol { get; init; } = string.Empty;

    /// <summary>
    /// For a pending route: the ships the plan could have given it, being free traders that can fly it
    /// and find it lucrative from where they are. The <c>ShipLeftIdle</c> rule reads it (D13).
    /// </summary>
    public IReadOnlyList<string> CandidateShipSymbols { get; init; } = [];
}
