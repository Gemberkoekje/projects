namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class Run
{
    public Guid Id { get; init; }

    public string AgentToken { get; init; } = string.Empty;

    required public string Name { get; init; }

    required public string StrategyLabel { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; set; }

    public string? SettingsSnapshotJson { get; init; }

    public long StartingCredits { get; init; }

    public long? EndingCredits { get; set; }
}
