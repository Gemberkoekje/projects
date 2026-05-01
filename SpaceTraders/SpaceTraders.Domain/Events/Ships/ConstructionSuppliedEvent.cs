namespace SpaceTraders.Domain.Events.Ships;

public sealed record ConstructionSuppliedEvent
{
    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid CausationId { get; init; }

    public string ShipSymbol { get; init; }

    public string SystemSymbol { get; init; }

    public string WaypointSymbol { get; init; }

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
    {
        EventId = Guid.NewGuid();
        OccurredAt = occurredAt;
        CorrelationId = correlationId == Guid.Empty ? EventId : correlationId;
        CausationId = causationId;
        ShipSymbol = shipSymbol;
        SystemSymbol = systemSymbol;
        WaypointSymbol = waypointSymbol;
        TradeSymbol = tradeSymbol;
        UnitsSupplied = unitsSupplied;
        IsComplete = isComplete;
    }
}
