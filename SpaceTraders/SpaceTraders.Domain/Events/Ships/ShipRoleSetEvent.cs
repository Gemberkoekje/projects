using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipRoleSetEvent : ShipDockedEvent
{
    public ShipRole Role { get; init; }

    public ShipRoleSetEvent(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        ShipRole role,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(shipSymbol, systemSymbol, waypointSymbol, correlationId, causationId, occurredAt)
    {
        Role = role;
    }
}
