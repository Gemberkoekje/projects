using Curator.Core.Commands;
using Curator.Core.Game;
using Curator.Core.Projections;

namespace Curator.Core.Bots;

/// <summary>Moves the bots share: commands that must succeed, and the books a bot can offer.</summary>
public static class BotMoves
{
    /// <summary>Sends a command that must be accepted.</summary>
    /// <param name="session">The session.</param>
    /// <param name="command">The command.</param>
    /// <exception cref="InvalidOperationException">The game refused it: a bot bug.</exception>
    public static void Must(GameSession session, Command command)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = session.Handle(command);
        if (!result.Accepted)
        {
            throw new InvalidOperationException($"Bot command {command} was refused: {result.Rejection}.");
        }
    }

    /// <summary>Books on the shelf that could be offered: not the one asked for, same topic first, then by fee.</summary>
    /// <param name="session">The session.</param>
    /// <param name="sameTopicOnly">Whether to leave out other topics.</param>
    /// <returns>The candidates.</returns>
    public static IReadOnlyList<CatalogueCard> OfferCandidates(GameSession session, bool sameTopicOnly)
    {
        ArgumentNullException.ThrowIfNull(session);
        var visit = session.Views.Visit();
        return session.Views.Catalogue().Cards
            .Where(c => c.Status == BookStatus.OnShelf && c.BookId != visit.RequestedBookId)
            .Where(c => !sameTopicOnly || c.SpineTag == visit.Topic)
            .OrderByDescending(c => c.SpineTag == visit.Topic)
            .ThenByDescending(c => c.Fee)
            .ThenBy(c => c.BookId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Offers books in order until one is taken or the patron leaves.</summary>
    /// <param name="session">The session.</param>
    /// <param name="candidates">The books, best first.</param>
    /// <returns>True when the visit is over.</returns>
    public static bool OfferUntilTaken(GameSession session, IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(candidates);
        foreach (var bookId in candidates)
        {
            if (!session.State.InVisit)
            {
                return true;
            }

            if (session.State.Visit.OffersRefused.Contains(bookId))
            {
                continue;
            }

            Must(session, new OfferBook(bookId));
        }

        return !session.State.InVisit;
    }
}
