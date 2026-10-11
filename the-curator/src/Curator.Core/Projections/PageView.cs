using Curator.Core.Text;

namespace Curator.Core.Projections;

/// <summary>An identified page in partial translation.</summary>
/// <param name="PageId">The page.</param>
/// <param name="Number">Its page number, from 1.</param>
/// <param name="SpellName">The spell's name.</param>
/// <param name="Text">The page text.</param>
/// <param name="Ingredients">Its ingredients.</param>
public sealed record PageView(
    string PageId,
    int Number,
    IReadOnlyList<TextSpan> SpellName,
    IReadOnlyList<TextSpan> Text,
    IReadOnlyList<IngredientView> Ingredients);
