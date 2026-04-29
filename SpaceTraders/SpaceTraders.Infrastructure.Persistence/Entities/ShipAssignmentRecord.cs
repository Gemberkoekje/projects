namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class ShipAssignmentRecord
{
    public string AgentToken { get; init; } = string.Empty;

    required public string ShipSymbol { get; init; }

    required public string Type { get; init; }

    public string? OriginWaypoint { get; init; }

    public string? DestWaypoint { get; init; }

    public string? CargoSymbol { get; init; }

    public string? ContractId { get; init; }

    public int StepIndex { get; init; }

    public DateTimeOffset AssignedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public int PurchaseUnitPrice { get; init; }

    public int RequiredUnits { get; init; }
}
