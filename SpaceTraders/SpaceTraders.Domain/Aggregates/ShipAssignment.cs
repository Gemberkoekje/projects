using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.ValueObjects;

namespace SpaceTraders.Domain.Aggregates;

public sealed record ShipAssignment(
    AssignmentType Type,
    WaypointSymbol? Origin,
    WaypointSymbol? Destination,
    TradeSymbol? Cargo,
    string? ContractId,
    DateTimeOffset AssignedAt);
