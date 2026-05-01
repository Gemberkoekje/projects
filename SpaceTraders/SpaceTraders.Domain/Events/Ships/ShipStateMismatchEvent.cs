namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipStateMismatchEvent
{
    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid CausationId { get; init; }

    public string ShipSymbol { get; init; }

    public string CommandName { get; init; }

    public string RequiredState { get; init; }

    public string ActualState { get; init; }

    public string Reason { get; init; }

    public ShipStateMismatchEvent(
        string shipSymbol,
        string commandName,
        string requiredState,
        string actualState,
        string reason,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
    {
        EventId = Guid.NewGuid();
        OccurredAt = occurredAt;
        CorrelationId = correlationId == Guid.Empty ? EventId : correlationId;
        CausationId = causationId;
        ShipSymbol = shipSymbol;
        CommandName = commandName;
        RequiredState = requiredState;
        ActualState = actualState;
        Reason = reason;
    }
}
