namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class AgentCreditsSample
{
    public long Id { get; init; }

    public string AgentToken { get; init; } = string.Empty;

    public DateTimeOffset ObservedAt { get; init; }

    public long Credits { get; init; }
}
