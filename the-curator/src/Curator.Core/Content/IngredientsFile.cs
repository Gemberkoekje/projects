namespace Curator.Core.Content;

/// <summary>game/content/ingredients.json.</summary>
public sealed record IngredientsFile
{
    public required IReadOnlyList<Ingredient> Ingredients { get; init; }

    public required bool Placeholder { get; init; }
}
