using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipRoleSetEvent : ChainOfCommandEvent
{
    public string ShipSymbol { get; init; }

    public ShipRole Role { get; init; }

    public ShipRoleSetEvent(
        string shipSymbol,
        ShipRole role,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(correlationId, causationId, occurredAt)
    {
        ShipSymbol = shipSymbol;
        Role = role;
    }
}
