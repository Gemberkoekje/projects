using Curator.Core.Text;

namespace Curator.Core.Projections;

/// <summary>A page where the curator saw an ingredient: one they identified.</summary>
/// <param name="BookId">The book.</param>
/// <param name="BookTitle">Its title.</param>
/// <param name="PageNumber">The page, from 1.</param>
/// <param name="SpellName">The spell's name, in partial translation.</param>
public sealed record IngredientSighting(
    string BookId,
    string BookTitle,
    int PageNumber,
    IReadOnlyList<TextSpan> SpellName);
