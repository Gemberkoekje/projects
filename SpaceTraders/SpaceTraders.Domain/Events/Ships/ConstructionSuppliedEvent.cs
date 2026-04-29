namespace SpaceTraders.Domain.Events.Ships;

public sealed record ConstructionSuppliedEvent : ShipInOrbitEvent
{
    public string TradeSymbol { get; init; }

    public int UnitsSupplied { get; init; }

    public bool IsComplete { get; init; }

    public ConstructionSuppliedEvent(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        string tradeSymbol,
        int unitsSupplied,
        bool isComplete,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(shipSymbol, systemSymbol, waypointSymbol, correlationId, causationId, occurredAt)
    {
        TradeSymbol = tradeSymbol;
        UnitsSupplied = unitsSupplied;
        IsComplete = isComplete;
    }
}
