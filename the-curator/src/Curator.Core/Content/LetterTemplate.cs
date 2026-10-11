namespace Curator.Core.Content;

/// <summary>A letter's sender and text.</summary>
public sealed record LetterTemplate
{
    public required string From { get; init; }

    public string Title { get; init; } = "";

    public required string Text { get; init; }
}
