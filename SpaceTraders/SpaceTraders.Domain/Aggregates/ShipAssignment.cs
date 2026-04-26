using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.ValueObjects;

namespace SpaceTraders.Domain.Aggregates;

public sealed record ShipAssignment
{
    public required AssignmentType Type { get; init; }

    public WaypointSymbol? Origin { get; init; }

    public WaypointSymbol? Destination { get; init; }

    public TradeSymbol? Cargo { get; init; }

    public string? ContractId { get; init; }

    public required DateTimeOffset AssignedAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipAssignment(
        AssignmentType Type,
        WaypointSymbol? Origin,
        WaypointSymbol? Destination,
        TradeSymbol? Cargo,
        string? ContractId,
        DateTimeOffset AssignedAt)
    {
        this.Type = Type;
        this.Origin = Origin;
        this.Destination = Destination;
        this.Cargo = Cargo;
        this.ContractId = ContractId;
        this.AssignedAt = AssignedAt;
    }
}
