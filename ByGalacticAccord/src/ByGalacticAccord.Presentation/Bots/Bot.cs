using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Views;

namespace ByGalacticAccord.Presentation.Bots;

/// <summary>
/// A scripted player (design §20.3): a way of playing, played through the session as the client plays it, reading what the
/// player can read (the board and its estimates, its own posts and haggles) and acting by the player's commands. The soak
/// harness runs one as the player of a world (<see cref="BotRun"/>) to measure what that way of playing earns.
/// </summary>
public abstract class Bot
{
    public abstract string Name { get; }

    /// <summary>Contracts it signed, as taker or poster.</summary>
    public int Deals { get; protected set; }

    /// <summary>Contracts it posted.</summary>
    public int Posts { get; protected set; }

    /// <summary>Counter-offers it made.</summary>
    public int Counters { get; protected set; }

    /// <summary>Look at the world and act, as a player does between glances at the clock.</summary>
    public abstract void Act(GameSession session);

    /// <summary>
    /// A decision the terms handed the player, answered by the rule an AI actor in its place uses (design §4.3): a bot plays
    /// the market, not these calls.
    /// </summary>
    public static DecisionChoice Answer(GameSession session, PlayerDecision decision)
    {
        SimulationContext context = session.Context;
        Contract contract = context.Contract(decision.Contract);
        return decision.Kind == DecisionKind.CancelForDefault
            ? DecisionEngine.WouldCancel(context, session.Player, contract) ? DecisionChoice.Cancel : DecisionChoice.Wait
            : DecisionEngine.WouldReject(context, session.Player, (SaleContract)contract, decision.Quantity, decision.Quality) ? DecisionChoice.Reject : DecisionChoice.Accept;
    }

    /// <summary>
    /// The best deal its free ship could take as posted: of the hauls and wanted Sales on its board, the one with the most
    /// take-home a day by the board's own estimate; none if nothing pays. A deal it would deliver past the grace it leaves (it
    /// would be in default, and the payer could cancel and keep its bond), and goods the buyer could reject. <paramref name="spare"/>
    /// is time it wants in hand.
    /// </summary>
    protected static (Contract Contract, DealEstimate Estimate)? BestDeal(GameSession session, long spare = 0)
    {
        SimulationContext context = session.Context;
        (Contract, DealEstimate)? best = null;
        decimal bestRate = 0m;
        foreach (Contract contract in Visibility.OpenContractsVisibleTo(context, session.Player))
        {
            if (contract.PostedBy == session.Player.Id || contract is not (HaulContract or SaleContract { Side: SaleSide.Wanted })
                || PlayerActions.Check(context, session.Player, contract) != DealBlock.None
                || DealEstimate.For(context, session.Player, session.PlayerShip, contract) is not { } estimate || estimate.TakeHome.Amount <= 0m
                || estimate.ArrivalTick + spare > contract.GraceEndsTick || contract.Warranty?.Rejectable(estimate.ArrivalQuality) == true)
            {
                continue;
            }

            decimal rate = estimate.PerDay(session.Now).Amount;
            if (rate > bestRate)
            {
                best = (contract, estimate);
                bestRate = rate;
            }
        }

        return best;
    }

    /// <summary>Take a contract as posted, counting it if it signed.</summary>
    protected bool Take(GameSession session, Contract contract)
    {
        bool signed = session.Accept(contract.Id).Signed;
        if (signed)
        {
            Deals++;
        }

        return signed;
    }

    /// <summary>Goods of its own left in the free ship's hold go into storage where it is docked, to clear the hold.</summary>
    protected static void ClearHold(GameSession session)
    {
        Ship ship = session.PlayerShip;
        if (!ship.IsFree)
        {
            return;
        }

        foreach (Lot lot in ship.Hold.Where(l => l.Owner == session.Player.Id).ToList())
        {
            session.Store(lot.Good);
        }
    }
}
