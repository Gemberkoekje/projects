using Curator.Core.Game;

namespace Curator.Core.Projections;

/// <summary>An index card in the catalogue drawer.</summary>
/// <param name="BookId">The book.</param>
/// <param name="Title">Its title.</param>
/// <param name="SpineTag">Its topic.</param>
/// <param name="Fee">Its fee.</param>
/// <param name="Summary">A previous owner's note, or empty.</param>
/// <param name="PagesRead">Pages identified.</param>
/// <param name="PagesTotal">Pages in the book now.</param>
/// <param name="Status">On the shelf, out or gone.</param>
/// <param name="BackToday">Returned this morning.</param>
/// <param name="BorrowerName">Who has it, when out.</param>
/// <param name="SinceDate">Since when, when out.</param>
public sealed record CatalogueCard(
    string BookId,
    string Title,
    string SpineTag,
    int Fee,
    string Summary,
    int PagesRead,
    int PagesTotal,
    BookStatus Status,
    bool BackToday,
    string BorrowerName,
    string SinceDate);
