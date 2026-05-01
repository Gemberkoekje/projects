namespace SpaceTraders.Application.Ports;

/// <summary>Minimal projection of an active <c>Run</c> used by the application layer.</summary>
public sealed record ActiveRunInfo
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string StrategyLabel { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required long StartingCredits { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ActiveRunInfo(Guid Id, string Name, string StrategyLabel, DateTimeOffset StartedAt, long StartingCredits)
    {
        this.Id = Id;
        this.Name = Name;
        this.StrategyLabel = StrategyLabel;
        this.StartedAt = StartedAt;
        this.StartingCredits = StartingCredits;
    }
}

/// <summary>Minimal projection of a <c>ScheduledRun</c> that is ready to be promoted.</summary>
public sealed record PendingScheduledRunInfo
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string StrategyLabel { get; init; }

    public string? ScheduledSettingsJson { get; init; }

    public DateTimeOffset? ActivatesAt { get; init; }

    public bool ActivatesOnNextRestart { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PendingScheduledRunInfo(Guid Id, string Name, string StrategyLabel, string? ScheduledSettingsJson, DateTimeOffset? ActivatesAt, bool ActivatesOnNextRestart)
    {
        this.Id = Id;
        this.Name = Name;
        this.StrategyLabel = StrategyLabel;
        this.ScheduledSettingsJson = ScheduledSettingsJson;
        this.ActivatesAt = ActivatesAt;
        this.ActivatesOnNextRestart = ActivatesOnNextRestart;
    }
}
