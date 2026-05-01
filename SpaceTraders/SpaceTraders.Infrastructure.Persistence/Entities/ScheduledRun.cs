namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class ScheduledRun
{
    public Guid Id { get; init; }

    public string AgentToken { get; init; } = string.Empty;

    required public string Name { get; init; }

    required public string StrategyLabel { get; init; }

    public string? ScheduledSettingsJson { get; init; }

    public DateTimeOffset? ActivatesAt { get; init; }

    public bool ActivatesOnNextRestart { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
