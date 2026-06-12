namespace AdventureEngine.Application.Agents;

/// <summary>
/// Streams narrative text for each player action.
/// </summary>
public interface INarratorAgent
{
    IAsyncEnumerable<string> StreamResponseAsync(
        NarratorContext ctx,
        CancellationToken ct = default);

    Task<string> GenerateChapterSummaryAsync(
        WorldManifest world,
        int chapterIndex,
        IReadOnlyList<(string PlayerInput, string NarratorResponse)> chapterHistory,
        CancellationToken ct = default);
}
