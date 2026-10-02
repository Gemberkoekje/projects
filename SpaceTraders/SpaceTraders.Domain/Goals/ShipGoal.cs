using System.Text.Json.Serialization;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Domain.Goals;

/// <summary>
/// Base record for all ship goals. A goal is a complete, self-contained objective for a single ship.
/// Each subtype carries the concrete parameters the executor needs; the ship handles all prerequisite
/// actions (refuel, navigate, handle cooldown) to reach the goal without external coordination.
/// </summary>
/// <remarks>
/// Phase 8: goal model introduced as part of the goal-driven architecture migration.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(IdleGoal), "Idle")]
[JsonDerivedType(typeof(MoveToWaypointGoal), "MoveToWaypoint")]
[JsonDerivedType(typeof(MineResourceGoal), "MineResource")]
[JsonDerivedType(typeof(SiphonResourceGoal), "SiphonResource")]
[JsonDerivedType(typeof(SellCargoGoal), "SellCargo")]
[JsonDerivedType(typeof(DeliverCargoGoal), "DeliverCargo")]
[JsonDerivedType(typeof(SupplyConstructionGoal), "SupplyConstruction")]
[JsonDerivedType(typeof(ScoutWaypointGoal), "ScoutWaypoint")]
[JsonDerivedType(typeof(PatrolMarketGoal), "PatrolMarket")]
[JsonDerivedType(typeof(DeployProbeGoal), "DeployProbe")]
[JsonDerivedType(typeof(MineAndSellGoal), "MineAndSell")]
[JsonDerivedType(typeof(TradeBetweenMarketsGoal), "TradeBetweenMarkets")]
[JsonDerivedType(typeof(SurveyWaypointGoal), "SurveyWaypoint")]
public abstract record ShipGoal
{
    /// <summary>Correlation token that links orchestrator assignment, goal execution, and completion events.</summary>
    public Guid GoalId { get; init; } = Guid.NewGuid();

    /// <summary>Current lifecycle status of this goal.</summary>
    public GoalStatus Status { get; init; } = GoalStatus.Assigned;

    /// <summary>Why the goal has its current status, for example <c>runaway</c> for a goal the circuit breaker blocked.</summary>
    public string? StatusReason { get; init; }

    /// <summary>
    /// UTC timestamp at which this goal was created/assigned. Persisted with the goal payload so that
    /// <see cref="ShipGoalHistoryEntry"/> can record accurate start times after a process restart.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; } = TimeProvider.System.GetUtcNow();

    /// <summary>Discriminator for the goal type; derived from the concrete subtype.</summary>
    [JsonIgnore]
    public abstract ShipGoalKind Kind { get; }
}

/// <summary>The ship has no active objective and is available for a new assignment.</summary>
public sealed record IdleGoal : ShipGoal
{
    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.Idle;
}

/// <summary>The ship navigates to a target waypoint without any further objective.</summary>
public sealed record MoveToWaypointGoal : ShipGoal
{
    public required string TargetWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.MoveToWaypoint;
}

/// <summary>The ship extracts a specific trade good from a source asteroid waypoint.</summary>
public sealed record MineResourceGoal : ShipGoal
{
    public required string TradeSymbol { get; init; }

    public required string SourceWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.MineResource;
}

/// <summary>The ship siphons a specific trade good from a gas-giant waypoint.</summary>
public sealed record SiphonResourceGoal : ShipGoal
{
    public required string TradeSymbol { get; init; }

    public required string SourceWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.SiphonResource;
}

/// <summary>The ship travels to a destination market and sells the listed trade goods.</summary>
public sealed record SellCargoGoal : ShipGoal
{
    public required string DestinationWaypointSymbol { get; init; }

    public required IReadOnlyList<string> TradeSymbols { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.SellCargo;
}

/// <summary>The ship delivers a specific trade good to a contract delivery waypoint.</summary>
public sealed record DeliverCargoGoal : ShipGoal
{
    public required string ContractId { get; init; }

    public required string TradeSymbol { get; init; }

    public required string DeliveryWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.DeliverCargo;
}

/// <summary>The ship delivers a trade good to an active construction site.</summary>
public sealed record SupplyConstructionGoal : ShipGoal
{
    public required string TradeSymbol { get; init; }

    public required string ConstructionSiteWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.SupplyConstruction;
}

/// <summary>The ship travels to a target waypoint to chart it and refresh its market / waypoint data.</summary>
public sealed record ScoutWaypointGoal : ShipGoal
{
    public required string TargetWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.ScoutWaypoint;
}

/// <summary>The ship repeatedly visits a market waypoint to keep market data fresh.</summary>
public sealed record PatrolMarketGoal : ShipGoal
{
    public required string TargetWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.PatrolMarket;
}

/// <summary>The probe navigates to <see cref="TargetWaypointSymbol"/>, sets DRIFT flight mode, and stays docked to collect market/shipyard data.</summary>
public sealed record DeployProbeGoal : ShipGoal
{
    public required string TargetWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.DeployProbe;
}

/// <summary>
/// The ship mines <see cref="TradeSymbol"/> at <see cref="SourceWaypointSymbol"/> and sells it at
/// <see cref="SellWaypointSymbol"/> while the target market opportunity remains active.
/// </summary>
public sealed record MineAndSellGoal : ShipGoal
{
    public required string TradeSymbol { get; init; }

    public required string SourceWaypointSymbol { get; init; }

    public required string SellWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.MineAndSell;
}

/// <summary>
/// One trip of a trade route: the ship buys <see cref="TradeSymbol"/> at <see cref="BuyWaypointSymbol"/>
/// and sells it at <see cref="SellWaypointSymbol"/>. It checks the trip again with the newest prices when
/// it gets to each market: before it buys, and before it sells (PLAN.md slice 6.5).
/// </summary>
public sealed record TradeBetweenMarketsGoal : ShipGoal
{
    /// <summary>The good the trip carries.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>Where the trip buys the good; for cargo the ship already held, where it was when the trip began.</summary>
    public required string BuyWaypointSymbol { get; init; }

    /// <summary>Where the trip sells the good.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>The units the trip planned to carry when it was chosen; 0 for a goal from before slice 6.5.</summary>
    public int Units { get; init; }

    /// <summary>What the trip was expected to earn after fuel, in credits, when it was chosen.</summary>
    public long ExpectedProfit { get; init; }

    /// <summary>
    /// The pricier good the sell market makes from <see cref="TradeSymbol"/>, or empty when the trip
    /// feeds no production there.
    /// </summary>
    public string FeedsTradeSymbol { get; init; } = string.Empty;

    /// <summary>
    /// True once the cargo is aboard: after the purchase, or from the start for a trip that sells cargo
    /// the ship already held.
    /// </summary>
    public bool CargoBought { get; init; }

    /// <summary>What one unit cost at the buy market; 0 for cargo the ship already held.</summary>
    public long PricePaidPerUnit { get; init; }

    /// <summary>True once the trip has moved its sale to another market. It does so at most once.</summary>
    public bool SellWaypointChanged { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.TradeBetweenMarkets;
}

/// <summary>
/// The ship surveys <see cref="TargetWaypointSymbol"/> for <see cref="TargetDepositSymbol"/> deposits.
/// Survey results are stored for later use by mining operations to maximize extraction efficiency.
/// </summary>
public sealed record SurveyWaypointGoal : ShipGoal
{
    public required string TargetWaypointSymbol { get; init; }

    public required string TargetDepositSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.SurveyWaypoint;
}
