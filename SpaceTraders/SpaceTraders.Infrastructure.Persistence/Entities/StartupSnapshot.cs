namespace SpaceTraders.Infrastructure.Persistence.Entities;

/// <summary>
/// One JSON snapshot of the game state as the bot had cached it: one at every start, and one whenever the cached
/// shipyards or markets list a ship type or good no earlier snapshot of the run held (slice 2.15).
/// </summary>
public sealed class StartupSnapshot
{
    /// <summary>A snapshot taken at startup.</summary>
    public const string StartupReason = "Startup";

    /// <summary>A snapshot taken because the cache lists a ship type or good it didn't before.</summary>
    public const string DiscoveryReason = "Discovery";

    public int Id { get; init; }

    public string AgentId { get; init; } = string.Empty;

    public string SnapshotJson { get; init; } = string.Empty;

    public DateTimeOffset CapturedAt { get; init; }

    public bool IsInitialSnapshot { get; init; }

    /// <summary>Why it was taken: <see cref="StartupReason"/> or <see cref="DiscoveryReason"/>.</summary>
    public string Reason { get; init; } = StartupReason;

    /// <summary>For a discovery, what was new and where, such as <c>Goods: FAB_MATS (X1-FJ91-H59)</c>; else null.</summary>
    public string? Discovered { get; init; }
}
