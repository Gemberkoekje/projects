namespace Curator.Core.Projections;

/// <summary>The card-catalogue drawer.</summary>
/// <param name="Cards">One card per book, by title.</param>
public sealed record CatalogueView(
    IReadOnlyList<CatalogueCard> Cards);
