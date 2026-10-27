using ByGalacticAccord.Presentation.Session;

namespace ByGalacticAccord.Presentation.Bots;

/// <summary>
/// The board taker (design §12, §20.3): whenever its ship is free, it takes the board's best deal as posted, by take-home a
/// day. It never posts and never haggles: the baseline a reader of the market should beat.
/// </summary>
public sealed class BoardTaker : Bot
{
    public override string Name => "board taker";

    public override void Act(GameSession session)
    {
        if (!session.PlayerShip.IsFree)
        {
            return;
        }

        ClearHold(session);
        if (BestDeal(session) is { } best)
        {
            Take(session, best.Contract);
        }
    }
}
