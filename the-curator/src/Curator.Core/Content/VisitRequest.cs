namespace Curator.Core.Content;

/// <summary>What the patron asks for.</summary>
public sealed record VisitRequest
{
    /// <summary>The book asked for by name, or empty for "something about…" requests.</summary>
    public string BookId { get; init; } = "";

    /// <summary>The spine tag of what they asked for — not necessarily of the book they named.</summary>
    public required string Topic { get; init; }

    public required string Text { get; init; }

    /// <summary>Whether the patron named a particular book.</summary>
    public bool NamesBook => BookId.Length > 0;
}
