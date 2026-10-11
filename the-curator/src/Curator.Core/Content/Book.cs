namespace Curator.Core.Content;

/// <summary>A book in game/content/books/&lt;id&gt;.json.</summary>
public sealed record Book
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string SpineTag { get; init; }

    public required Rarity Rarity { get; init; }

    /// <summary>Overrides the rarity fee when set.</summary>
    public int? Fee { get; init; }

    /// <summary>A previous owner's note, or empty.</summary>
    public string Summary { get; init; } = "";

    /// <summary>The chance, 0 to 1, that an Identify fails outright.</summary>
    public double IdentifyResistance { get; init; }

    public required IReadOnlyList<Page> Pages { get; init; }

    public required bool Placeholder { get; init; }
}
