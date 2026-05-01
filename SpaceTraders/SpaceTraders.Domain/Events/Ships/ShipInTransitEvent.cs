namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipInTransitEvent
{
    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid CausationId { get; init; }

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
    {
        EventId = Guid.NewGuid();
        OccurredAt = occurredAt;
        CorrelationId = correlationId == Guid.Empty ? EventId : correlationId;
        CausationId = causationId;
        ShipSymbol = shipSymbol;
        OriginWaypointSymbol = originWaypointSymbol;
        DestinationWaypointSymbol = destinationWaypointSymbol;
        ArrivalTime = arrivalTime;
    }
}
