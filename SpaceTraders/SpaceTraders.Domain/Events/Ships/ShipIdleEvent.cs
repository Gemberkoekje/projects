using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipIdleEvent : ChainOfCommandEvent
{
    public string ShipSymbol { get; init; }

    public string Reason { get; init; }

    public ShipIdleEvent(
        string shipSymbol,
        string reason,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(correlationId, causationId, occurredAt)
    {
        ShipSymbol = shipSymbol;
        Reason = reason;
    }
}
