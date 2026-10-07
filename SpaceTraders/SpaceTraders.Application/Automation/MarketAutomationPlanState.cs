namespace SpaceTraders.Application.Automation;

public static partial class PlanTypes
{
    public const string MiningAutomation = "MiningAutomation";
    public const string SiphonAutomation = "SiphonAutomation";
    public const string TradingAutomation = "TradingAutomation";
}

public enum MarketAutomationOpportunityStatus
{
    None = 0,
    Pending = 1,
    Assigned = 2,
    Cancelled = 3,
}

/// <summary>
/// The mining plan's view after its last pass (slice 6.4): the low-supply openings, written only when they change.
/// The siphon plan keeps its view alike, under <see cref="PlanTypes.SiphonAutomation"/> (slice 6.7).
/// </summary>
public sealed record MiningAutomationPlanState
{
    public required Guid PlanId { get; init; }

    public required IReadOnlyList<MiningAutomationOpportunityState> Opportunities { get; init; }

    /// <summary>
    /// The far asteroids where a shuttle collects what parked drones mine (slice 6.18, D83), with the shuttles designated
    /// for each, which the role board keeps collecting, and the drones with a place there. Empty for the siphon plan, and in
    /// a state stored before slice 6.18.
    /// </summary>
    public IReadOnlyList<CollectionPointState> CollectionPoints { get; init; } = [];

    /// <summary>
    /// The drones bought for the jump gate's smelters (slice 6.25, D92), while they are in the fleet: each mines only its ore,
    /// for the smelters that make a metal the gate's materials need from it, until the gate needs nothing made from it; when
    /// each was bought spaces the next one for its ore. Empty for the siphon plan, and in a state stored before slice 6.25.
    /// </summary>
    public IReadOnlyList<GateMinerState> GateMiners { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>A drone the mining plan bought for the jump gate's smelters (slice 6.25, D92), and the ore it mines for them.</summary>
public sealed record GateMinerState
{
    /// <summary>The drone.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>The ore it was bought for, such as <c>IRON_ORE</c>.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>When it was bought: the next drone for the same ore waits <c>Mining.GateMinerIntervalMinutes</c> from then.</summary>
    public required DateTimeOffset BoughtAt { get; init; }
}

/// <summary>
/// A far asteroid in the mining plan's view (slice 6.18, D83): where a shuttle collects what the drones parked there mine,
/// and sells it at <see cref="SellWaypointSymbol"/>.
/// </summary>
public sealed record CollectionPointState
{
    /// <summary>Where the drones are parked.</summary>
    public required string AsteroidWaypointSymbol { get; init; }

    /// <summary>Where the shuttles sell.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>The market's ores below ABUNDANT that only this point serves.</summary>
    public IReadOnlyList<string> Ores { get; init; } = [];

    /// <summary>Those the market has SCARCE or LIMITED: a drone is kept for each (D48).</summary>
    public IReadOnlyList<string> ScarceOres { get; init; } = [];

    /// <summary>The shuttles designated for the point: bought for it, kept collecting by the role board.</summary>
    public IReadOnlyList<string> ShuttleSymbols { get; init; } = [];

    /// <summary>The drones with a place there: parked, or on their way.</summary>
    public IReadOnlyList<string> DroneSymbols { get; init; } = [];
}

/// <summary>
/// A low-supply opening in the mining plan's view (slice 6.4, D22): a market with an ore in low supply, and
/// the asteroid nearest it that yields the ore. The siphon plan lists its gases alike (slice 6.7), with the gas
/// giant nearest the market.
/// </summary>
public sealed record MiningAutomationOpportunityState
{
    /// <summary>The opening's key: sell market and ore (or gas).</summary>
    public required string OpportunityKey { get; init; }

    public required string TradeSymbol { get; init; }

    public required string SellWaypointSymbol { get; init; }

    /// <summary>The asteroid nearest the market whose traits yield the ore; for a gas, the gas giant nearest it.</summary>
    public string SourceWaypointSymbol { get; init; } = string.Empty;

    /// <summary>Assigned while a miner's (or siphoner's) trip sells the good there; Pending otherwise.</summary>
    public required MarketAutomationOpportunityStatus Status { get; init; }

    public string? AssignedShipSymbol { get; init; }

    /// <summary>
    /// For a pending opening: the miners (or siphoners) without a trip that could reach its asteroid (or gas
    /// giant). The <c>ShipLeftIdle</c> rule reads it (D13).
    /// </summary>
    public IReadOnlyList<string> CandidateShipSymbols { get; init; } = [];

    public required DateTimeOffset FirstObservedAt { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    public string? StopReason { get; init; }
}

/// <summary>
/// The trading plan's view after its last pass (slice 6.5): the routes its traders hold, and the
/// lucrative routes no trader holds; and why the other goods with a price gap aren't traded (slice 2.18). Written only when it
/// changes.
/// </summary>
public sealed record TradingAutomationPlanState
{
    public required Guid PlanId { get; init; }

    /// <summary>The held routes (Assigned) and the lucrative routes without a trader (Pending).</summary>
    public required IReadOnlyList<TradingAutomationOpportunityState> Opportunities { get; init; }

    /// <summary>
    /// For each good with a price gap that no route in <see cref="Opportunities"/> carries, why not (slice 2.18, D76): one
    /// market sells it for less than another pays for it. By system, the furthest first, then by good.
    /// </summary>
    public IReadOnlyList<TradingAutomationGoodNotTradedState> NotTraded { get; init; } = [];

    /// <summary>
    /// When the plan last bought a cargo ship, of the list or beyond it (slice 6.34, D116): the next beyond the list goes before
    /// the probes once <c>Trade.ShipPurchaseIntervalMinutes</c> have passed since. Null before its first, and in a state stored
    /// before slice 6.34.
    /// </summary>
    public DateTimeOffset? LastShipBoughtAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Why a good with a price gap isn't traded (slice 2.18, D76): the check its route got furthest with failed, for the free trader
/// that got furthest with it, at the trading plan's last pass with a free trader in its system. While every trader there is on a
/// trip the plan checks no route, and this stays, until a route of the good is listed.
/// </summary>
public sealed record TradingAutomationGoodNotTradedState
{
    public required string SystemSymbol { get; init; }

    public required string TradeSymbol { get; init; }

    /// <summary>
    /// The check that failed: buy_market_out_of_reach, sell_market_out_of_reach, not_full_hold (D56, D74), too_few_credits
    /// (D56) or not_lucrative (D14); below_the_listed_routes when the route was lucrative, but the state keeps only the best
    /// waiting routes.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>The free trader that got furthest with the good.</summary>
    public required string ShipSymbol { get; init; }

    public required string BuyWaypointSymbol { get; init; }

    public required string SellWaypointSymbol { get; init; }

    /// <summary>The reason in a sentence, with the figures of the check that failed.</summary>
    public required string Why { get; init; }

    /// <summary>When the trading plan found it: its last pass with a free trader in the system, when the state was written.</summary>
    public required DateTimeOffset JudgedAt { get; init; }
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

    /// <summary>
    /// How long a trip takes, in seconds, as estimated from where the ship is (D95): for a held route, when its trader set off;
    /// for a pending one, for its best candidate. 0 for cargo a trader already held, and for a route stored before slice 6.27.
    /// </summary>
    public int ExpectedSeconds { get; init; }

    /// <summary>The jumps through the gates a trip takes, to the buy market and on to the sell market (slice 6.29, D96); 0 within a system.</summary>
    public int Jumps { get; init; }

    /// <summary>The pricier good the sell market makes from the good, or empty (D15).</summary>
    public string FeedsTradeSymbol { get; init; } = string.Empty;

    /// <summary>
    /// For a pending route: the ships the plan could have given it, being free traders that can fly it
    /// and find it lucrative from where they are. The <c>ShipLeftIdle</c> rule reads it (D13).
    /// </summary>
    public IReadOnlyList<string> CandidateShipSymbols { get; init; } = [];
}
