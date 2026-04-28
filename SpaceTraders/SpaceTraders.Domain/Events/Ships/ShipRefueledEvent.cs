using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipRefueledEvent : ShipDockedEvent
{
    public int FuelCurrent { get; init; }

    public int FuelCapacity { get; init; }

    public long CostPaid { get; init; }

    public ShipRefueledEvent(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        int fuelCurrent,
        int fuelCapacity,
        long costPaid,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(shipSymbol, systemSymbol, waypointSymbol, correlationId, causationId, occurredAt)
    {
        FuelCurrent = fuelCurrent;
        FuelCapacity = fuelCapacity;
        CostPaid = costPaid;
    }
}
