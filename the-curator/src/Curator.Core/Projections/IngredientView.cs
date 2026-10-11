namespace Curator.Core.Projections;

/// <summary>An ingredient as the page shows it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Known">Translated ink when known, untranslated cursive when not.</param>
public sealed record IngredientView(
    string Name,
    bool Known);
