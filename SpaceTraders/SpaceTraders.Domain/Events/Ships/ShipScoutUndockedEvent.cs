namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipScoutUndockedEvent : ShipInOrbitEvent
{
    public ShipScoutUndockedEvent(
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
