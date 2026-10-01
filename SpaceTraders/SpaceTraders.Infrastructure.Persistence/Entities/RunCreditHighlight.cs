namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class RunCreditHighlight
{
    public long Id { get; init; }

    public string AgentId { get; init; } = string.Empty;

    public Guid RunId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public long Credits { get; init; }

    public long DeltaCredits { get; init; }

    required public string EventKind { get; init; }

    public string? Label { get; init; }
}
