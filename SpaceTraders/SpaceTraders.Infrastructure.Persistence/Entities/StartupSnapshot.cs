namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class StartupSnapshot
{
    public int Id { get; set; }

    public string AgentToken { get; set; } = string.Empty;

    public string SnapshotJson { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; }

    public bool IsInitialSnapshot { get; set; }
}
