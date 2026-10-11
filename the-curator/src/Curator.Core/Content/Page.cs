namespace Curator.Core.Content;

/// <summary>One page of a book: one spell. Only the text and ingredients are ever shown.</summary>
public sealed record Page
{
    public required string Id { get; init; }

    /// <summary>The spell's name, in page markup.</summary>
    public required string SpellName { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public required int Power { get; init; }

    public required int Danger { get; init; }

    /// <summary>The page text, in page markup.</summary>
    public required string Text { get; init; }

    public required IReadOnlyList<string> Ingredients { get; init; }

    public bool Unidentifiable { get; init; }

    /// <summary>A note for writers; never shown.</summary>
    public string AuthorNote { get; init; } = "";
}
