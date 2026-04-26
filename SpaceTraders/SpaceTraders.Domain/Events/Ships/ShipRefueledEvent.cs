using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipRefueledEvent : ChainOfCommandEvent
{
    public string ShipSymbol { get; init; }

    public int FuelCurrent { get; init; }

    public int FuelCapacity { get; init; }

    public long CostPaid { get; init; }

    public ShipRefueledEvent(
        string shipSymbol,
        int fuelCurrent,
        int fuelCapacity,
        long costPaid,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(correlationId, causationId, occurredAt)
    {
        ShipSymbol = shipSymbol;
        FuelCurrent = fuelCurrent;
        FuelCapacity = fuelCapacity;
        CostPaid = costPaid;
    }
}
