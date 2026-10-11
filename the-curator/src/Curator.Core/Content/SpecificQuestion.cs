namespace Curator.Core.Content;

/// <summary>A question unlocked by investigation.</summary>
public sealed record SpecificQuestion
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    /// <summary>readThoughts, identifiedPage:&lt;pageId&gt;, cardBook:&lt;bookId&gt; or flag:&lt;flag&gt;.</summary>
    public required string UnlockedBy { get; init; }

    /// <summary>Answers by trust level.</summary>
    public required IReadOnlyDictionary<int, string> Answers { get; init; }

    /// <summary>Trust change on asking.</summary>
    public int Trust { get; init; }

    public IReadOnlyList<string> SetFlags { get; init; } = [];

    /// <summary>Patience cost; the balance's question cost when absent.</summary>
    /// <remarks>Nullable because "absent" (use the balance) differs from an explicit 0.</remarks>
    public int? PatienceCost { get; init; }
}
