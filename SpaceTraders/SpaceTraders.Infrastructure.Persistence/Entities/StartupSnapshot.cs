namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class StartupSnapshot
{
    public int Id { get; init; }

    public string AgentId { get; init; } = string.Empty;

    public string SnapshotJson { get; init; } = string.Empty;

    public DateTimeOffset CapturedAt { get; init; }

    public bool IsInitialSnapshot { get; init; }
}
