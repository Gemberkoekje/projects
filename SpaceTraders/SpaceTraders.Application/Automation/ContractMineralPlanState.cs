namespace SpaceTraders.Application.Automation;

public enum ContractMineralPlanStatus
{
    None = 0,
    Active = 1,
    PendingBudget = 2,
    DeferredUnsupported = 3,
    Completed = 4,
}

public sealed record ContractMineralPlanState
{
    public required Guid PlanId { get; init; }

    public required string ContractId { get; init; }

    public required string ShipSymbol { get; init; }

    public required string TradeSymbol { get; init; }

    public required string SourceWaypoint { get; init; }

    public required string DestinationWaypoint { get; init; }

    public required int UnitsRequired { get; init; }

    public required int UnitsFulfilled { get; init; }

    public required ContractMineralPlanStatus Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public string? StopReason { get; init; }
}
