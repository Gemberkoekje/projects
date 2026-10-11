using Curator.Core.Commands;
using Curator.Core.Game;

namespace Curator.Core.Bots;

/// <summary>Lends every request and never investigates.</summary>
public sealed class LendAllBot : IStrategyBot
{
    /// <inheritdoc />
    public string Name => "lend-all";

    /// <inheritdoc />
    public void PlayVisit(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Views.Visit().CanLendRequested)
        {
            BotMoves.Must(session, new LendRequested());
            return;
        }

        if (BotMoves.OfferUntilTaken(session, BotMoves.OfferCandidates(session, sameTopicOnly: false).Select(c => c.BookId)))
        {
            return;
        }

        BotMoves.Must(session, new Decline());
    }
}
