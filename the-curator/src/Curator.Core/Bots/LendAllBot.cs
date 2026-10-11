using Curator.Core.Commands;
using Curator.Core.Game;

namespace Curator.Core.Bots;

/// <summary>Lends every request and never investigates.</summary>
public sealed class LendAllBot : IStrategyBot
{
    /// <inheritdoc />
    public string Name => "lend-all";

    /// <inheritdoc />
    public void PlayVisit(GameSession session, ICommandSender sender)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sender);
        if (session.Views.Visit().CanLendRequested)
        {
            BotMoves.Must(sender, new LendRequested());
            return;
        }

        if (BotMoves.OfferUntilTaken(session, sender, BotMoves.OfferCandidates(session, sameTopicOnly: false).Select(c => c.BookId)))
        {
            return;
        }

        BotMoves.Must(sender, new Decline());
    }
}
