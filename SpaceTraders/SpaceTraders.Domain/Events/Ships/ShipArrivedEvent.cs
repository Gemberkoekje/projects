using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipArrivedEvent : ChainOfCommandEvent
{
    public string ShipSymbol { get; init; }

    public string SystemSymbol { get; init; }

    public string WaypointSymbol { get; init; }

    public DateTimeOffset ArrivedAt { get; init; }

    public ShipArrivedEvent(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        DateTimeOffset arrivedAt,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(correlationId, causationId, occurredAt)
    {
        ShipSymbol = shipSymbol;
        SystemSymbol = systemSymbol;
        WaypointSymbol = waypointSymbol;
        ArrivedAt = arrivedAt;
    }
}
