namespace Curator.Core.Content;

/// <summary>A question that is always on offer.</summary>
public sealed record StandardQuestion
{
    public required string Id { get; init; }

    /// <summary>The wording when the patron asked for a particular book.</summary>
    public required string Text { get; init; }

    /// <summary>The wording when the patron asked for "something about" a topic.</summary>
    public required string TextTopic { get; init; }
}
