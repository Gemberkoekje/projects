namespace Curator.Core.Content;

/// <summary>A note from the Board that arrives on a given morning.</summary>
public sealed record TutorialLetter
{
    public required int Day { get; init; }

    public required string From { get; init; }

    public string Title { get; init; } = "";

    public required string Text { get; init; }
}
