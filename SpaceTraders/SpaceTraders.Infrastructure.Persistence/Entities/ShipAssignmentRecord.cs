namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class ShipAssignmentRecord
{
    public required string ShipSymbol { get; set; }

    public required string Type { get; set; }

    public string? OriginWaypoint { get; set; }

    public string? DestWaypoint { get; set; }

    public string? CargoSymbol { get; set; }

    public string? ContractId { get; set; }

    public int StepIndex { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
