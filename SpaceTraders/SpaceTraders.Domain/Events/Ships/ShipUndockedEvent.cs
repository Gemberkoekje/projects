using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipUndockedEvent : ChainOfCommandEvent
{
    public string ShipSymbol { get; init; }

    public string SystemSymbol { get; init; }

    public string WaypointSymbol { get; init; }

    public ShipUndockedEvent(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(correlationId, causationId, occurredAt)
    {
        ShipSymbol = shipSymbol;
        SystemSymbol = systemSymbol;
        WaypointSymbol = waypointSymbol;
    }
}
