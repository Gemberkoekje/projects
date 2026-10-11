using Curator.Core.Content;
using Curator.Core.Game;

namespace Curator.Core.Projections;

/// <summary>The open book on the desk.</summary>
/// <param name="BookId">The book.</param>
/// <param name="Title">Its title.</param>
/// <param name="SpineTag">Its topic.</param>
/// <param name="Rarity">Its rarity.</param>
/// <param name="Fee">Its fee.</param>
/// <param name="Summary">A previous owner's note, or empty.</param>
/// <param name="CoverSlot">The art slot for its cover.</param>
/// <param name="Pages">Identified pages, in page order.</param>
/// <param name="UnreadCount">Pages not yet read.</param>
/// <param name="ResistedPageNumbers">Pages that won't come into focus.</param>
/// <param name="RemovedPageNumbers">Pages torn out.</param>
/// <param name="Status">Where it is.</param>
/// <param name="CanIdentify">Whether Identify can be cast on it now.</param>
/// <param name="IdentifyCost">Identify's mana cost.</param>
/// <param name="CostsPatience">Whether Identify now costs the waiting patron patience.</param>
/// <param name="IsRequested">Whether the patron at the counter asked for it.</param>
/// <param name="CanLend">Whether it can be stamped and lent now.</param>
/// <param name="CanOffer">Whether it can be offered instead now.</param>
public sealed record BookView(
    string BookId,
    string Title,
    string SpineTag,
    Rarity Rarity,
    int Fee,
    string Summary,
    string CoverSlot,
    IReadOnlyList<PageView> Pages,
    int UnreadCount,
    IReadOnlyList<int> ResistedPageNumbers,
    IReadOnlyList<int> RemovedPageNumbers,
    BookStatus Status,
    bool CanIdentify,
    int IdentifyCost,
    bool CostsPatience,
    bool IsRequested,
    bool CanLend,
    bool CanOffer);
