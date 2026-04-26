namespace SpaceTraders.Domain.Events;

public abstract record ChainOfCommandEvent
{
    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid CausationId { get; init; }

    protected ChainOfCommandEvent(Guid correlationId, Guid causationId, DateTimeOffset occurredAt)
    {
        EventId = Guid.NewGuid();
        OccurredAt = occurredAt;
        CorrelationId = correlationId == Guid.Empty ? EventId : correlationId;
        CausationId = causationId;
    }
}
