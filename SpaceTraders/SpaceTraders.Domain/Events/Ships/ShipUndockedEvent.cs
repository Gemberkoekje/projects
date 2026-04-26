using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipUndockedEvent : ShipInOrbitEvent
{
    public ShipUndockedEvent(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(shipSymbol, systemSymbol, waypointSymbol, correlationId, causationId, occurredAt)
    {
    }
}
