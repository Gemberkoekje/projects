using Curator.Core.Commands;
using Curator.Core.Game;

namespace Curator.Core.Bots;

/// <summary>Declines everything that isn't forced.</summary>
public sealed class DeclineAllBot : IStrategyBot
{
    /// <inheritdoc />
    public string Name => "decline-all";

    /// <inheritdoc />
    public void PlayVisit(GameSession session, ICommandSender sender)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sender);
        var visit = session.Views.Visit();
        if (visit.CanDecline)
        {
            BotMoves.Must(sender, new Decline());
            return;
        }

        if (visit.CanLendRequested)
        {
            BotMoves.Must(sender, new LendRequested());
            return;
        }

        BotMoves.OfferUntilTaken(session, sender, BotMoves.OfferCandidates(session, sameTopicOnly: false).Select(c => c.BookId));
    }
}
