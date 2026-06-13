namespace AdventureEngine.Application.Agents;

/// <summary>
/// Streams narrative text for each player action.
/// </summary>
public interface INarratorAgent
{
    IAsyncEnumerable<string> StreamResponseAsync(
        NarratorContext ctx,
        Action<AgentUsage>? onComplete = null,
        CancellationToken ct = default);

    Task<(string Summary, AgentUsage Usage)> GenerateChapterSummaryAsync(
        WorldManifest world,
        int chapterIndex,
        IReadOnlyList<(string PlayerInput, string NarratorResponse)> chapterHistory,
        CancellationToken ct = default);
}
