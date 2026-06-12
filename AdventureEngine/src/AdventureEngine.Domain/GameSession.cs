namespace AdventureEngine.Domain;

/// <summary>
/// Marten aggregate root for a game session.
/// Reconstructed from the event stream on each load.
/// </summary>
public sealed class GameSession
{
    public Guid Id { get; private set; }
    public string PlayerId { get; private set; } = string.Empty;
    public WorldManifest? WorldManifest { get; private set; }
    public SessionStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public int CurrentChapterIndex { get; private set; }
    public string CurrentSceneId { get; private set; } = string.Empty;

    /// <summary>Completed chapter summaries, keyed by chapter index.</summary>
    public Dictionary<int, string> ChapterSummaries { get; private set; } = new();

    /// <summary>The last six player/narrator exchange pairs for the sliding context window.</summary>
    public List<(string PlayerInput, string NarratorResponse)> RecentHistory { get; private set; } = new();

    // Marten requires a public parameterless constructor for aggregate rehydration.
    public GameSession() { }

    public void Apply(Events.SessionCreated e)
    {
        Id = e.SessionId;
        PlayerId = e.PlayerId;
        WorldManifest = e.WorldManifest;
        Status = SessionStatus.Active;
        CreatedAt = e.OccurredAt;
        CurrentChapterIndex = 1;
        CurrentSceneId = e.WorldManifest.Chapters
            .FirstOrDefault()?.Scenes.FirstOrDefault()?.Id ?? string.Empty;
    }

    public void Apply(Events.PlayerActed e)
    {
        RecentHistory.Add((e.Input, e.NarratorResponse));
        if (RecentHistory.Count > 6)
            RecentHistory.RemoveAt(0);
    }

    public void Apply(Events.ChapterCompleted e)
    {
        ChapterSummaries[e.ChapterIndex] = e.OutcomeSummary;
        var nextChapter = WorldManifest?.Chapters
            .FirstOrDefault(c => c.Index == e.ChapterIndex + 1);
        if (nextChapter is not null)
        {
            CurrentChapterIndex = nextChapter.Index;
            CurrentSceneId = nextChapter.Scenes.FirstOrDefault()?.Id ?? string.Empty;
        }
    }

    public void Apply(Events.SceneEntered e)
    {
        CurrentSceneId = e.SceneId;
    }

    public void Apply(Events.GameEnded e)
    {
        Status = e.Won ? SessionStatus.Completed : SessionStatus.Abandoned;
    }
}
