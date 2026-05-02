namespace SpaceTraders.Application.Orchestration;

/// <summary>
/// Strategic goal types evaluated by the fleet orchestrator.
/// </summary>
public enum FleetGoalKind
{
    None = 0,
    Contract,
    Construction,
    MarketCoverage,
    FleetExpansion,
}

/// <summary>
/// A single strategic goal produced by a goal evaluator.
/// </summary>
public sealed record FleetGoal
{
    public required FleetGoalKind Kind { get; init; }

    public required string Description { get; init; }

    public required int Priority { get; init; }

    public string? ContractId { get; init; }

    public string? TradeSymbol { get; init; }

    public string? OriginWaypoint { get; init; }

    public string? DestinationWaypoint { get; init; }

    public int RemainingUnits { get; init; }

    public DateTimeOffset? Deadline { get; init; }

    public long EstimatedCost { get; init; }

    /// <summary>
    /// For <see cref="FleetGoalKind.FleetExpansion"/> goals: the ship type to purchase (e.g. "SHIP_MINING_DRONE").
    /// </summary>
    public string? ExpansionShipType { get; init; }

    /// <summary>
    /// For <see cref="FleetGoalKind.FleetExpansion"/> goals: the waypoint symbol of the shipyard where the ship will be purchased.
    /// </summary>
    public string? ExpansionShipyardWaypoint { get; init; }

    /// <summary>
    /// For <see cref="FleetGoalKind.FleetExpansion"/> goals: the goal that the newly-purchased ship should immediately be assigned to.
    /// </summary>
    public FleetGoal? ExpansionTargetGoal { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public FleetGoal(
        FleetGoalKind Kind,
        string Description,
        int Priority,
        string? ContractId = null,
        string? TradeSymbol = null,
        string? OriginWaypoint = null,
        string? DestinationWaypoint = null,
        int RemainingUnits = 0,
        DateTimeOffset? Deadline = null,
        long EstimatedCost = 0,
        string? ExpansionShipType = null,
        string? ExpansionShipyardWaypoint = null,
        FleetGoal? ExpansionTargetGoal = null)
    {
        this.Kind = Kind;
        this.Description = Description;
        this.Priority = Priority;
        this.ContractId = ContractId;
        this.TradeSymbol = TradeSymbol;
        this.OriginWaypoint = OriginWaypoint;
        this.DestinationWaypoint = DestinationWaypoint;
        this.RemainingUnits = RemainingUnits;
        this.Deadline = Deadline;
        this.EstimatedCost = EstimatedCost;
        this.ExpansionShipType = ExpansionShipType;
        this.ExpansionShipyardWaypoint = ExpansionShipyardWaypoint;
        this.ExpansionTargetGoal = ExpansionTargetGoal;
    }
}

/// <summary>
/// Aggregated capacity estimate for the active fleet, used by the orchestrator
/// to decide whether existing ships can complete goals or if expansion is needed.
/// </summary>
public sealed record FleetCapacityEstimate
{
    public required int TotalShips { get; init; }

    public required int MiningShips { get; init; }

    public required int HaulingShips { get; init; }

    public required int TradingShips { get; init; }

    public required int ScoutingShips { get; init; }

    public required int IdleShips { get; init; }

    public required int TotalCargoCapacity { get; init; }

    public required int IdleCargoCapacity { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public FleetCapacityEstimate(
        int TotalShips,
        int MiningShips,
        int HaulingShips,
        int TradingShips,
        int ScoutingShips,
        int IdleShips,
        int TotalCargoCapacity,
        int IdleCargoCapacity)
    {
        this.TotalShips = TotalShips;
        this.MiningShips = MiningShips;
        this.HaulingShips = HaulingShips;
        this.TradingShips = TradingShips;
        this.ScoutingShips = ScoutingShips;
        this.IdleShips = IdleShips;
        this.TotalCargoCapacity = TotalCargoCapacity;
        this.IdleCargoCapacity = IdleCargoCapacity;
    }
}

/// <summary>
/// Describes how the output of a <see cref="ResourceProductionGoal"/> will be consumed.
/// </summary>
/// <remarks>Phase 9: introduced as part of the goal-driven architecture migration.</remarks>
public enum ResourceProductionPurpose
{
    /// <summary>Resource is needed to fulfil a player contract.</summary>
    Contract = 0,

    /// <summary>Resource is needed to supply an active construction site.</summary>
    Construction = 1,

    /// <summary>Resource will be sold on the open market.</summary>
    Market = 2,
}

/// <summary>
/// An abstract resource need used by the fleet orchestrator to assign ships.
/// The assignment resolver translates this into a concrete <see cref="SpaceTraders.Domain.Goals.ShipGoal"/>
/// with specific waypoints; the orchestrator never references waypoints directly.
/// </summary>
/// <remarks>Phase 9: introduced as part of the goal-driven architecture migration.</remarks>
public sealed record ResourceProductionGoal
{
    /// <summary>The SpaceTraders trade-symbol of the resource to produce (e.g. "BAUXITE").</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>Number of units that still need to be produced to satisfy the goal.</summary>
    public required int UnitsNeeded { get; init; }

    /// <summary>Why the resource is being produced; used by the resolver to pick the right goal type.</summary>
    public required ResourceProductionPurpose PurposeKind { get; init; }

    /// <summary>
    /// Optional identifier that qualifies the purpose:
    /// a contract id when <see cref="PurposeKind"/> is <see cref="ResourceProductionPurpose.Contract"/>;
    /// the construction-site waypoint symbol when it is <see cref="ResourceProductionPurpose.Construction"/>.
    /// </summary>
    public string? PurposeId { get; init; }

    /// <summary>Optional deadline by which the units are needed.</summary>
    public DateTimeOffset? Deadline { get; init; }

    /// <summary>Higher values are processed first by the orchestrator.</summary>
    public required int Priority { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ResourceProductionGoal(
        string TradeSymbol,
        int UnitsNeeded,
        ResourceProductionPurpose PurposeKind,
        int Priority,
        string? PurposeId = null,
        DateTimeOffset? Deadline = null)
    {
        this.TradeSymbol = TradeSymbol;
        this.UnitsNeeded = UnitsNeeded;
        this.PurposeKind = PurposeKind;
        this.Priority = Priority;
        this.PurposeId = PurposeId;
        this.Deadline = Deadline;
    }
}

/// <summary>
/// Budget decision describing whether a proposed cost can be afforded
/// while keeping the configured credit reserve untouched.
/// </summary>
public sealed record BudgetDecision
{
    public required bool CanAfford { get; init; }

    public required long AvailableCredits { get; init; }

    public required long ReservedCredits { get; init; }

    public required long SpendableCredits { get; init; }

    public string? Reason { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public BudgetDecision(
        bool CanAfford,
        long AvailableCredits,
        long ReservedCredits,
        long SpendableCredits,
        string? Reason = null)
    {
        this.CanAfford = CanAfford;
        this.AvailableCredits = AvailableCredits;
        this.ReservedCredits = ReservedCredits;
        this.SpendableCredits = SpendableCredits;
        this.Reason = Reason;
    }
}
