using System;
using System.Collections.Generic;
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
    /// take-home a day by the board's own estimate; none if nothing pays.
    /// </summary>
    protected static (Contract Contract, DealEstimate Estimate)? BestDeal(GameSession session)
    {
        (Contract, DealEstimate)? best = null;
        decimal bestRate = 0m;
        foreach ((Contract contract, DealEstimate estimate) in Takeable(session))
        {
            decimal rate = estimate.PerDay(session.Now).Amount;
            if (rate > bestRate)
            {
                best = (contract, estimate);
                bestRate = rate;
            }
        }

        return best;
    }

    /// <summary>How much a reader trusts that a deal it sees now from where a first one ends will still be there when it arrives.</summary>
    public const decimal NextLegWeight = 0.5m;

    /// <summary>
    /// The deal a reader of the market judges best (design §11.2: the uncertain inputs beside the take-home are for the player to
    /// weigh; D17: judging whether a contract is worth signing is the skill). It takes off each take-home what could go wrong,
    /// priced (<see cref="Risk"/>), and ranks each deal by what it and the best deal from where it ends would make a day together,
    /// the second counted at <see cref="NextLegWeight"/> (it may be gone by then). None if nothing pays after its risk.
    /// </summary>
    protected static (Contract Contract, DealEstimate Estimate)? ReadDeal(GameSession session)
    {
        SimulationContext context = session.Context;
        Ship ship = session.PlayerShip;
        decimal running = ship.Class.RunningCostPerTick.Amount;
        var deals = Takeable(session).Select(d => (d.Contract, d.Estimate, Net: d.Estimate.TakeHome.Amount - Risk(d.Contract, d.Estimate))).Where(d => d.Net > 0m).ToList();
        (Contract, DealEstimate)? best = null;
        decimal bestScore = 0m;
        foreach ((Contract contract, DealEstimate estimate, decimal net) in deals)
        {
            decimal hours = Math.Max(1, estimate.TiedUpTicks(session.Now));
            LocationId end = contract is HaulContract haul ? haul.Destination : ((SaleContract)contract).Place;
            decimal score = net / hours;
            foreach ((Contract next, DealEstimate later, decimal laterNet) in deals)
            {
                if (ReferenceEquals(next, contract) || next.DeadlineTick < estimate.ArrivalTick)
                {
                    continue;
                }

                // The next deal as it would be from where this one ends: its empty leg from there instead of from here.
                long fromEnd = end == later.Pickup ? 0
                    : context.FindPath(end, later.Pickup) is { } leg ? PriceModel.TravelTicks(leg.DistanceTicks, ship.Class.SpeedMultiplier) : long.MaxValue / 4;
                decimal secondNet = laterNet + (running * (later.RepoTicks - fromEnd));
                if (secondNet <= 0m)
                {
                    continue;
                }

                decimal both = (net + (secondNet * NextLegWeight)) / (hours + (Math.Max(1, later.LadenTicks + fromEnd) * NextLegWeight));
                score = Math.Max(score, both);
            }

            if (score > bestScore)
            {
                best = (contract, estimate);
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// What could go wrong on a deal, priced (first-pass weights): an incident on the laden leg puts a fifth of what is at stake at
    /// risk (the price, the goods bought for it, the bond); outside the Accord a payer may not pay, a tenth of the price.
    /// </summary>
    public static decimal Risk(Contract contract, DealEstimate estimate) =>
        (estimate.IncidentOdds * 0.2m * (estimate.Income.Amount + estimate.Goods.Amount + estimate.Bond.Amount))
        + (contract.UsesEscrow ? 0m : 0.1m * estimate.Income.Amount);

    /// <summary>
    /// The deals its free ship could take as posted, with their estimates: the hauls and wanted Sales on its board that pay
    /// something. One it would deliver past the grace it leaves (it would be in default, and the payer could cancel and keep its
    /// bond), and goods the buyer could reject.
    /// </summary>
    protected static IEnumerable<(Contract Contract, DealEstimate Estimate)> Takeable(GameSession session)
    {
        SimulationContext context = session.Context;
        foreach (Contract contract in session.KnownOpen())
        {
            if (contract.PostedBy == session.Player.Id || contract is not (HaulContract or SaleContract { Side: SaleSide.Wanted })
                || PlayerActions.Check(context, session.Player, contract) != DealBlock.None
                || DealEstimate.For(context, session.Player, session.PlayerShip, contract) is not { } estimate || estimate.TakeHome.Amount <= 0m
                || estimate.ArrivalTick > contract.GraceEndsTick || contract.Warranty?.Rejectable(estimate.ArrivalQuality) == true)
            {
                continue;
            }

            yield return (contract, estimate);
        }
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
