using Curator.Core.Commands;
using Curator.Core.Game;

namespace Curator.Core.Bots;

/// <summary>Declines everything that isn't forced.</summary>
public sealed class DeclineAllBot : IStrategyBot
{
    /// <inheritdoc />
    public string Name => "decline-all";

    /// <inheritdoc />
    public void PlayVisit(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var visit = session.Views.Visit();
        if (visit.CanDecline)
        {
            BotMoves.Must(session, new Decline());
            return;
        }

        if (visit.CanLendRequested)
        {
            BotMoves.Must(session, new LendRequested());
            return;
        }

        BotMoves.OfferUntilTaken(session, BotMoves.OfferCandidates(session, sameTopicOnly: false).Select(c => c.BookId));
    }
}
