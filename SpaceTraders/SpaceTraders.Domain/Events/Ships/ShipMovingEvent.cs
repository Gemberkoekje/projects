using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipMovingEvent : ChainOfCommandEvent
{
    public string ShipSymbol { get; init; }

    public string OriginWaypointSymbol { get; init; }

    public string DestinationWaypointSymbol { get; init; }

    public DateTimeOffset DepartureTime { get; init; }

    public DateTimeOffset ArrivalTime { get; init; }

    public int FuelConsumed { get; init; }

    public ShipMovingEvent(
        string shipSymbol,
        string originWaypointSymbol,
        string destinationWaypointSymbol,
        DateTimeOffset departureTime,
        DateTimeOffset arrivalTime,
        int fuelConsumed,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(correlationId, causationId, occurredAt)
    {
        ShipSymbol = shipSymbol;
        OriginWaypointSymbol = originWaypointSymbol;
        DestinationWaypointSymbol = destinationWaypointSymbol;
        DepartureTime = departureTime;
        ArrivalTime = arrivalTime;
        FuelConsumed = fuelConsumed;
    }
}
