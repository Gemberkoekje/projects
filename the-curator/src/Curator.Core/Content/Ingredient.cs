namespace Curator.Core.Content;

/// <summary>An ingredient. Known ones render as translated ink, unknown ones as untranslated cursive — always.</summary>
public sealed record Ingredient
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required bool Known { get; init; }
}
