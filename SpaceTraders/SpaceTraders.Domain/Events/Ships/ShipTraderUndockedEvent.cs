namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipTraderUndockedEvent : ShipInOrbitEvent
{
    public ShipTraderUndockedEvent(
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
