using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipDockedEvent : ChainOfCommandEvent
{
    public string ShipSymbol { get; init; }

    public string SystemSymbol { get; init; }

    public string WaypointSymbol { get; init; }

    public ShipDockedEvent(
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
