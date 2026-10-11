namespace Curator.Core.Projections;

/// <summary>One ingredient in the curator's ingredient log, with every identified page it appears on.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Known">Translated ink when known, untranslated cursive when not, as on the page.</param>
/// <param name="Seen">The pages it appears on, book by book.</param>
public sealed record IngredientLogEntry(
    string Name,
    bool Known,
    IReadOnlyList<IngredientSighting> Seen);
