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
        long EstimatedCost = 0)
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
