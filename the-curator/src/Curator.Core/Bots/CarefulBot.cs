using Curator.Core.Commands;
using Curator.Core.Game;
using Curator.Core.Projections;

namespace Curator.Core.Bots;

/// <summary>
/// Asks what the book is for, casts Read Thoughts when mana allows, identifies the requested
/// book up to twice, and if it saw a page of danger 2 or more offers the best same-topic book
/// with no danger seen; otherwise lends (BUILD_BRIEF §8.4). It reads page danger the way a
/// player reads a page: only for pages it has identified.
/// </summary>
/// <remarks>
/// Two readings of the brief (docs/QUESTIONS.md): "when mana allows" means after keeping enough
/// mana for two Identifies in every visit still to come today; "the best" alternative is the one
/// known to be safest (most pages read without danger), then the cheapest, since "the dangerous
/// grimoire is often the lucrative one" (GDD A13).
/// </remarks>
public sealed class CarefulBot : IStrategyBot
{
    private const int DangerSeen = 2;
    private const int IdentifiesPerBook = 2;
    private const string PurposeQuestion = "purpose";

    /// <inheritdoc />
    public string Name => "careful";

    /// <inheritdoc />
    public void PlayVisit(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Views.Visit().Questions.Any(q => q.Id == PurposeQuestion && !q.Asked))
        {
            BotMoves.Must(session, new AskQuestion(PurposeQuestion));
        }

        if (!session.State.InVisit)
        {
            return;
        }

        var visit = session.Views.Visit();
        var slotsLeft = session.Content.Day(session.State.Day).Slots.Count - session.State.Visit.SlotIndex;
        if (visit.CanReadThoughts && session.State.Mana >= visit.ReadThoughtsCost + (IdentifiesPerBook * visit.IdentifyCost * slotsLeft))
        {
            BotMoves.Must(session, new ReadThoughts());
        }

        if (session.Views.Visit().CanLendRequested)
        {
            var requested = visit.RequestedBookId;
            for (var cast = 0; cast < IdentifiesPerBook && MaxDangerSeen(session, requested) < DangerSeen; cast++)
            {
                var book = session.Views.Book(requested);
                if (!book.CanIdentify || book.UnreadCount == 0 || session.State.Visit.Patience <= 0)
                {
                    break;
                }

                BotMoves.Must(session, new Identify(requested));
                if (!session.State.InVisit)
                {
                    return;
                }
            }

            if (MaxDangerSeen(session, requested) < DangerSeen)
            {
                BotMoves.Must(session, new LendRequested());
                return;
            }
        }

        if (BotMoves.OfferUntilTaken(session, SafestFirst(session, BotMoves.OfferCandidates(session, sameTopicOnly: true))))
        {
            return;
        }

        BotMoves.Must(session, session.Views.Visit().CanDecline ? new Decline() : new LendRequested());
    }

    private static IEnumerable<string> SafestFirst(GameSession session, IEnumerable<CatalogueCard> cards) =>
        cards.Where(c => MaxDangerSeen(session, c.BookId) < DangerSeen)
            .OrderByDescending(c => c.PagesRead)
            .ThenBy(c => c.Fee)
            .ThenBy(c => c.BookId, StringComparer.Ordinal)
            .Select(c => c.BookId)
            .ToList();

    private static int MaxDangerSeen(GameSession session, string bookId)
    {
        var identified = session.State.Book(bookId).Identified;
        return identified.Count == 0 ? 0 : identified.Max(id => session.Content.Page(id).Page.Danger);
    }
}
