namespace AdventureEngine.Application.Agents;

/// <summary>
/// Context passed to the Narrator agent for each player action.
/// </summary>
public sealed class NarratorContext
{
    public required WorldManifest WorldManifest { get; init; }
    public required int CurrentChapterIndex { get; init; }
    public required string CurrentSceneId { get; init; }
    public required IReadOnlyList<(string PlayerInput, string NarratorResponse)> RecentHistory { get; init; }
    public required IReadOnlyDictionary<int, string> ChapterSummaries { get; init; }
    public required string PlayerInput { get; init; }
}
