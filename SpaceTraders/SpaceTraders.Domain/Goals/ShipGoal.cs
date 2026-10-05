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
[JsonDerivedType(typeof(SiphonAndSellGoal), "SiphonAndSell")]
[JsonDerivedType(typeof(GatherAndSellGoal), "GatherAndSell")]
[JsonDerivedType(typeof(TradeBetweenMarketsGoal), "TradeBetweenMarkets")]
[JsonDerivedType(typeof(SurveyWaypointGoal), "SurveyWaypoint")]
[JsonDerivedType(typeof(JumpGoal), "Jump")]
[JsonDerivedType(typeof(ExploreSystemGoal), "ExploreSystem")]
[JsonDerivedType(typeof(MineForShuttleGoal), "MineForShuttle")]
[JsonDerivedType(typeof(CollectOreGoal), "CollectOre")]
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

/// <summary>
/// The ship navigates to a target waypoint without any further objective; the goal ends there. The survey plan moves a
/// ship that can only survey to the area where most drones mine this way (D54), drifting when it is out of CRUISE reach
/// (<see cref="Drifting"/>).
/// </summary>
public sealed record MoveToWaypointGoal : ShipGoal
{
    public required string TargetWaypointSymbol { get; init; }

    /// <summary>
    /// True for a move to a market out of the ship's CRUISE reach (D45, D54): it drifts there, 1 fuel whatever the distance,
    /// about ten times slower.
    /// </summary>
    public bool Drifting { get; init; }

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

/// <summary>
/// One construction trip (PLAN.md slice 6.6): the ship buys <see cref="Units"/> of <see cref="TradeSymbol"/> at
/// <see cref="BuyWaypointSymbol"/> in batches of the market's trade volume (D81), flies them to the construction site
/// <see cref="ConstructionSiteWaypointSymbol"/>, the jump gate, and supplies them there; then the goal ends, and the
/// construction plan chooses the next trip. Supplying pays nothing, so what the cargo cost is booked as the trip's loss
/// (D46). A trip for materials the ship already holds starts with its cargo aboard (<see cref="CargoBought"/>).
/// </summary>
public sealed record SupplyConstructionGoal : TripGoal
{
    /// <summary>The material the trip carries.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>The construction site it supplies: the jump gate.</summary>
    public required string ConstructionSiteWaypointSymbol { get; init; }

    /// <summary>Where the trip buys the material; for materials the ship already held, where it was when the trip began.</summary>
    public string BuyWaypointSymbol { get; init; } = string.Empty;

    /// <summary>
    /// The units the trip planned to carry when it was chosen: a full hold, or what the site still needed (D81); once bought,
    /// what it bought, which the market's supply or the credits may have cut short.
    /// </summary>
    public int Units { get; init; }

    /// <summary>
    /// The credits the trip holds back for its cargo from the moment it starts towards the buy market until the cargo is
    /// aboard, as a trade trip does (D57, D64): its units at the price it was chosen with. Other trips and ship purchases
    /// leave them. 0 for materials the ship already held.
    /// </summary>
    public long ReservedCredits { get; init; }

    /// <summary>True once the cargo is aboard: after the purchase, or from the start for materials the ship already held.</summary>
    public bool CargoBought { get; init; }

    /// <summary>What one unit cost at the buy market; 0 for materials the ship already held.</summary>
    public long PricePaidPerUnit { get; init; }

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

/// <summary>
/// One flight of a probe (PLAN.md slice 6.3): it flies to <see cref="TargetWaypointSymbol"/> in CRUISE, where
/// its arrival fetches the market and the shipyard; then the goal ends, and the probe plan chooses again. A
/// probe with no goal stays where it is, and the market watch keeps that market fresh.
/// </summary>
public sealed record DeployProbeGoal : ShipGoal
{
    public required string TargetWaypointSymbol { get; init; }

    /// <summary>True when a shipyard called for it, because a purchase there waits for one of our ships (D30).</summary>
    public bool ForPurchase { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.DeployProbe;
}

/// <summary>
/// A trip that buys or sells cargo: a trade, mining, siphon or spare-time trip. It keeps what its sales brought in and
/// what its cargo cost, so that what it made can be booked when it ends (D46), with the fuel its ship bought meanwhile.
/// A trip stored before it kept them loads with neither.
/// </summary>
public abstract record TripGoal : ShipGoal
{
    /// <summary>The credits the trip's sales brought in so far.</summary>
    public long Earned { get; init; }

    /// <summary>The credits the trip paid for cargo so far.</summary>
    public long Spent { get; init; }
}

/// <summary>
/// One mining trip (PLAN.md slice 6.4): the ship mines <see cref="TradeSymbol"/> at
/// <see cref="SourceWaypointSymbol"/> until its hold is full, with the best survey there when there is
/// one, then sells it at <see cref="SellWaypointSymbol"/>; then the goal ends, and the mining plan
/// chooses the next trip. A trip to a market out of the ship's CRUISE reach drifts there first
/// (<see cref="Drifting"/>, slice 6.10c).
/// </summary>
public sealed record MineAndSellGoal : TripGoal
{
    /// <summary>The ore the trip mines; other ores are jettisoned.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>Where it mines.</summary>
    public required string SourceWaypointSymbol { get; init; }

    /// <summary>Where it sells.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>
    /// True while the trip drifts to <see cref="SellWaypointSymbol"/>, out of the ship's CRUISE reach (D45): 1 fuel
    /// whatever the distance, about ten times slower. Once there, it mines from that market in CRUISE.
    /// </summary>
    public bool Drifting { get; init; }

    /// <summary>
    /// True once the trip sells: its hold is full, or it was given ore the ship already held to sell.
    /// </summary>
    public bool Selling { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.MineAndSell;
}

/// <summary>
/// One siphon trip (PLAN.md slice 6.7), as a mining trip: the ship siphons at the gas giant
/// <see cref="SourceWaypointSymbol"/> until its hold is full, keeping every gas a market buys (D33), then
/// sells <see cref="TradeSymbol"/> at <see cref="SellWaypointSymbol"/>; then the goal ends, and the siphon
/// plan sells the other gases and chooses the next trip. A siphon takes no survey: the API's siphon call
/// has none. A trip to a market out of the ship's CRUISE reach drifts there first (<see cref="Drifting"/>,
/// slice 6.10c).
/// </summary>
public sealed record SiphonAndSellGoal : TripGoal
{
    /// <summary>The gas the trip is for; the other gases it siphons are kept too (D33).</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>The gas giant it siphons at.</summary>
    public required string SourceWaypointSymbol { get; init; }

    /// <summary>Where it sells <see cref="TradeSymbol"/>.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>
    /// True while the trip drifts to <see cref="SellWaypointSymbol"/>, out of the ship's CRUISE reach (D45): 1 fuel
    /// whatever the distance, about ten times slower. Once there, it siphons from that market in CRUISE.
    /// </summary>
    public bool Drifting { get; init; }

    /// <summary>
    /// True once the trip sells: its hold is full, or it was given gas the ship already held to sell.
    /// </summary>
    public bool Selling { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.SiphonAndSell;
}

/// <summary>
/// One spare-time trip (PLAN.md slice 6.8): a ship that has nothing to survey and no trade (D34) mines or
/// siphons at <see cref="SourceWaypointSymbol"/>, the nearest place it can (D35), keeping whatever a market buys,
/// until its hold is full; then it sells each good where it fetches most after fuel (D36), and the goal ends. A
/// survey or a trade may take the ship off the trip while it fills (D37).
/// </summary>
public sealed record GatherAndSellGoal : TripGoal
{
    /// <summary>The asteroid it mines at, or the gas giant it siphons at.</summary>
    public required string SourceWaypointSymbol { get; init; }

    /// <summary>True at a gas giant, where the trip siphons; false at an asteroid, where it mines.</summary>
    public bool Siphoning { get; init; }

    /// <summary>True once the trip sells: its hold is full.</summary>
    public bool Selling { get; init; }

    /// <summary>The good the trip is selling now; empty before it has chosen the next sale.</summary>
    public string SellTradeSymbol { get; init; } = string.Empty;

    /// <summary>Where it sells <see cref="SellTradeSymbol"/>; empty before it has chosen the next sale.</summary>
    public string SellWaypointSymbol { get; init; } = string.Empty;

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.GatherAndSell;
}

/// <summary>
/// A drone parked at a far asteroid (PLAN.md slice 6.18, D83): an asteroid out of every drone's CRUISE round trip of the
/// market that buys its ores, where a shuttle collects (<see cref="CollectOreGoal"/>). The drone gets there once: it drifts
/// to <see cref="SellWaypointSymbol"/> first when that market is out of its CRUISE reach (<see cref="Drifting"/>, D45),
/// fills its tank there and flies on. At the asteroid it mines with the best survey for <see cref="TradeSymbol"/>, keeping
/// every ore a market buys within one tank (D71), and hands what it holds to a collecting shuttle there; with its hold full
/// and no shuttle there, it waits. It never sells: the shuttle does. The goal lasts while the mining plan keeps the
/// asteroid's collection open.
/// </summary>
public sealed record MineForShuttleGoal : ShipGoal
{
    /// <summary>The ore the surveys are chosen for: the market's scarcest that the asteroid yields.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>Where the drone is parked and mines.</summary>
    public required string AsteroidWaypointSymbol { get; init; }

    /// <summary>The market the shuttle sells at, where the drone fills its tank on the way.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>
    /// True while the drone drifts to <see cref="SellWaypointSymbol"/>, out of its CRUISE reach (D45): 1 fuel whatever the
    /// distance, about ten times slower. Once there, it flies on to the asteroid in CRUISE.
    /// </summary>
    public bool Drifting { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.MineForShuttle;
}

/// <summary>
/// One round of a collecting shuttle (PLAN.md slice 6.18, D83): it flies to <see cref="AsteroidWaypointSymbol"/> and waits
/// in orbit while the parked drones hand it their ore (<see cref="MineForShuttleGoal"/>), until its hold is full; then it
/// sells everything aboard at <see cref="SellWaypointSymbol"/> (<see cref="Selling"/>), and the goal ends. The mining plan
/// gives the next round. It also leaves with a part hold when no drone is left there. The round is booked as a trip (D46).
/// </summary>
public sealed record CollectOreGoal : TripGoal
{
    /// <summary>Where the drones are parked.</summary>
    public required string AsteroidWaypointSymbol { get; init; }

    /// <summary>Where it sells what they mine.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>True once it sells: its hold is full, or no drone is left at the asteroid.</summary>
    public bool Selling { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.CollectOre;
}

/// <summary>
/// One trip of a trade route: the ship buys <see cref="TradeSymbol"/> at <see cref="BuyWaypointSymbol"/>
/// and sells it at <see cref="SellWaypointSymbol"/>. It checks the trip again with the newest prices when
/// it gets to each market: before it buys, and before it sells (PLAN.md slice 6.5).
/// </summary>
public sealed record TradeBetweenMarketsGoal : TripGoal
{
    /// <summary>The good the trip carries.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>Where the trip buys the good; for cargo the ship already held, where it was when the trip began.</summary>
    public required string BuyWaypointSymbol { get; init; }

    /// <summary>Where the trip sells the good.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>
    /// The units the trip planned to carry when it was chosen (D79); once bought, what it bought, batch by batch. 0 for a goal
    /// from before slice 6.5.
    /// </summary>
    public int Units { get; init; }

    /// <summary>What the trip was expected to earn after fuel, in credits, when it was chosen.</summary>
    public long ExpectedProfit { get; init; }

    /// <summary>
    /// The pricier good the sell market makes from <see cref="TradeSymbol"/>, or empty when the trip
    /// feeds no production there.
    /// </summary>
    public string FeedsTradeSymbol { get; init; } = string.Empty;

    /// <summary>
    /// The credits the trip holds back for its cargo from the moment it starts towards the buy market until the cargo is
    /// aboard (D57): what its units were expected to cost when it was chosen, each batch a step dearer (D79), less what the
    /// batches bought so far cost. Other trips and ship purchases leave them. 0 for cargo the ship already held, and for a trip
    /// stored before D57.
    /// </summary>
    public long ReservedCredits { get; init; }

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

/// <summary>
/// One jump of the command ship's exploring (asked on 2026-10-04): it flies to <see cref="GateWaypointSymbol"/>, the jump
/// gate of the system it is in, and jumps to <see cref="DestinationGateWaypointSymbol"/>, a gate that gate connects to,
/// which buys one ANTIMATTER at the gate's market. The explore plan gives it only while the jump leaves the credit floor every
/// ship purchase keeps. The goal ends in the destination's system; the explore plan chooses the next step.
/// </summary>
public sealed record JumpGoal : ShipGoal
{
    /// <summary>The gate the ship jumps from, in the system it is in.</summary>
    public required string GateWaypointSymbol { get; init; }

    /// <summary>The gate it jumps to, in another system.</summary>
    public required string DestinationGateWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.Jump;
}

/// <summary>
/// The command ship scouts a system it explores (asked on 2026-10-04): it visits each of <see cref="Stops"/>, the system's
/// markets and shipyards, once, as the scout plan does at home, and the visit stores what the market and the shipyard
/// there sell. The goal keeps its own progress (<see cref="Visited"/>), so the tick and an arrival that step it one after
/// the other can't skip a stop (B45). It ends after the last stop; the explore plan then counts the system as explored.
/// </summary>
public sealed record ExploreSystemGoal : ShipGoal
{
    /// <summary>The system it scouts.</summary>
    public required string SystemSymbol { get; init; }

    /// <summary>The markets and shipyards to visit, in the order the ship flies to them.</summary>
    public required IReadOnlyList<string> Stops { get; init; }

    /// <summary>How many of <see cref="Stops"/> it has visited: the next stop is <c>Stops[Visited]</c>.</summary>
    public int Visited { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.ExploreSystem;
}
