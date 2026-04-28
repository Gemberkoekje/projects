namespace SpaceTraders.Domain.Events.Ships;

/// <summary>
/// Emitted after a ship's automation assignment type has been explicitly updated in the repository.
/// Derives from <see cref="ShipDockedEvent"/> so it re-enters the docked chain-of-command,
/// allowing role-specific handlers to act on the new assignment type.
/// </summary>
public sealed record ShipAssignmentTypeSetEvent : ShipDockedEvent
{
    public string AssignmentType { get; init; }

    public ShipAssignmentTypeSetEvent(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        string assignmentType,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(shipSymbol, systemSymbol, waypointSymbol, correlationId, causationId, occurredAt)
    {
        AssignmentType = assignmentType;
    }
}
