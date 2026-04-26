using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public record ShipInTransitEvent : ChainOfCommandEvent
{
    public string ShipSymbol { get; init; }

    public string OriginWaypointSymbol { get; init; }

    public string DestinationWaypointSymbol { get; init; }

    public DateTimeOffset ArrivalTime { get; init; }

    public ShipInTransitEvent(
        string shipSymbol,
        string originWaypointSymbol,
        string destinationWaypointSymbol,
        DateTimeOffset arrivalTime,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(correlationId, causationId, occurredAt)
    {
        ShipSymbol = shipSymbol;
        OriginWaypointSymbol = originWaypointSymbol;
        DestinationWaypointSymbol = destinationWaypointSymbol;
        ArrivalTime = arrivalTime;
    }
}
