using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipMovingEvent : ShipInTransitEvent
{
    public DateTimeOffset DepartureTime { get; init; }

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
        : base(shipSymbol, originWaypointSymbol, destinationWaypointSymbol, arrivalTime, correlationId, causationId, occurredAt)
    {
        DepartureTime = departureTime;
        FuelConsumed = fuelConsumed;
    }
}
