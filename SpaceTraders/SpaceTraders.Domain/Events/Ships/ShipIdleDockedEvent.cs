using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipIdleDockedEvent : ShipDockedEvent
{
    public string Reason { get; init; }

    public ShipIdleDockedEvent(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        string reason,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(shipSymbol, systemSymbol, waypointSymbol, correlationId, causationId, occurredAt)
    {
        Reason = reason;
    }
}
