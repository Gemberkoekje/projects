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
/// The market reader (design §12, §13's second beat, §20.3), a first cut: it reads the market for a spread and posts its own
/// wanted Sale to buy into it. When a delivered wanted Sale on its board pays well over what the goods cost at a producer's
/// door (by the lots on its board, or else their base value), it posts a wanted Sale at that door, haggles the answers down
/// to its budget, and once the goods are in its storage there, fills the Sale with its ship. Otherwise it takes the board's
/// best deal, but counters a post that has waited a while before it takes it as posted.
/// </summary>
public sealed class MarketReader : Bot
{
    /// <summary>What it keeps for itself on a plan, as a share of the Sale it fills.</summary>
    private const decimal Margin = 0.15m;

    /// <summary>How long a post has waited before it haggles over it rather than take it as posted.</summary>
    private const long HaggleAfterHours = 36;

    /// <summary>How much more than posted it first asks for carrying a haul or filling a Sale.</summary>
    private const decimal Ask = 1.12m;

    /// <summary>How long it keeps goods it bought for a Sale someone else filled, looking for another, before it sells them where they are.</summary>
    private const long KeepHours = 10 * 24;

    // Its own wanted Sale at a producer's door, the delivered Sale it means to fill with the goods, and the most it pays for them.
    private Plan? _plan;

    // Goods it bought and couldn't place yet: what, where, and since when; and the least it takes for them as a lot.
    private Leftover? _leftover;

    private sealed record Plan(ContractId Post, ContractId Target, GoodType Good, LocationId Source, decimal Budget);

    private sealed record Leftover(GoodType Good, LocationId At, long Since, decimal Floor);

    public override string Name => "market reader";

    public override void Act(GameSession session)
    {
        AnswerHaggles(session);
        if (!session.PlayerShip.IsFree)
        {
            return;
        }

        ClearHold(session);
        if (CarryOut(session) || Waiting(session) || (_leftover is null && PlanPurchase(session)))
        {
            return;
        }

        HaggleOrTake(session);
    }

    // Answers that wait for it: on its own post, the cheapest within budget is accepted, others countered half way to its
    // budget or declined; on others' posts, a poster's counter is never under what it posted, so it is taken while the ship is free.
    private void AnswerHaggles(GameSession session)
    {
        SimulationContext context = session.Context;
        ActorId player = session.Player.Id;
        foreach (Negotiation thread in session.PlayerNegotiations.Where(n => n.IsOpen && n.AwaitingFrom == player).OrderBy(n => n.Latest.Offer.Price.Amount).ToList())
        {
            if (!thread.IsOpen)
            {
                continue; // an answer it accepted just now closed the rest
            }

            Offer theirs = thread.Latest.Offer;
            if (thread.Poster == player && session.Context.Contract(thread.Contract) is SaleContract { Side: SaleSide.ForSale })
            {
                // Its leftovers, up for sale: any offer over its floor will do.
                if (theirs.Price.Amount >= (_leftover?.Floor ?? 0m) && session.AcceptOffer(thread.Id) == Signing.Signed)
                {
                    Deals++;
                    _leftover = null;
                }
                else
                {
                    session.Decline(thread.Id);
                }
            }
            else if (thread.Poster == player)
            {
                decimal budget = _plan is { } plan && plan.Post == thread.Contract ? plan.Budget : 0m;
                if (theirs.Price.Amount <= budget && theirs.Deadline <= context.Contract(thread.Contract).DeadlineTick + 24)
                {
                    if (session.AcceptOffer(thread.Id) == Signing.Signed)
                    {
                        Deals++;
                    }
                }
                else if (thread.Rounds < 4 && budget > 0m)
                {
                    decimal mine = thread.LatestBy(player)?.Offer.Price.Amount ?? 0m;
                    Offer counter = theirs with { Price = Credits.Of(Math.Round(Math.Min(budget, (mine + theirs.Price.Amount) / 2m), 2)) };
                    if (session.Counter(thread.Contract, counter, thread.Id) == DealBlock.None)
                    {
                        Counters++;
                    }
                }
                else
                {
                    session.Decline(thread.Id);
                }
            }
            else if (session.PlayerShip.IsFree && session.AcceptOffer(thread.Id) == Signing.Signed)
            {
                Deals++;
            }
            else
            {
                session.Decline(thread.Id);
            }
        }
    }

    // Goods it bought for a plan are in its storage at the source: fill the Sale they were for, or another they fit. Goods it
    // couldn't place it keeps for a while, filling the first Sale they fit; then it puts them up where they are.
    private bool CarryOut(GameSession session)
    {
        SimulationContext context = session.Context;
        if (_plan is { } plan && context.Contract(plan.Post).Status == ContractStatus.Fulfilled)
        {
            if (Fill(session, plan.Good, plan.Source, context.Contract(plan.Target)))
            {
                _plan = null;
                return true;
            }

            _plan = null;
            if ((context.StorageOf(session.Player.Id, plan.Source)?.Available(plan.Good) ?? 0m) > 0m)
            {
                _leftover = new Leftover(plan.Good, plan.Source, session.Now, 0m);
            }

            return false;
        }

        if (_leftover is not { } left)
        {
            return false;
        }

        decimal stored = context.StorageOf(session.Player.Id, left.At)?.Available(left.Good) ?? 0m;
        if (stored <= 0m || Fill(session, left.Good, left.At, null))
        {
            _leftover = null;
            return stored > 0m;
        }

        if (session.Now - left.Since >= KeepHours && left.Floor == 0m)
        {
            // Nothing it can fill with them: put them up where they are at their base value, and take any offer over half that.
            decimal value = Goods.Info(left.Good).BaseValue * stored;
            if (session.Post(new PostDraft(PostKind.Lot, left.Good, stored, left.At, null, new Offer(Credits.Of(Math.Round(value, 2)), session.Now + 96, TermPackage.Standard))).Posted)
            {
                Posts++;
                _leftover = left with { Floor = value * 0.5m };
            }
        }

        return false;
    }

    // Fill a wanted Sale with its stored goods: the one they were for, else any on its board they fit, if the board says it pays.
    private bool Fill(GameSession session, GoodType good, LocationId at, Contract? target)
    {
        SimulationContext context = session.Context;
        IEnumerable<Contract> targets = (target is null ? Enumerable.Empty<Contract>() : new[] { target })
            .Concat(Visibility.OpenContractsVisibleTo(context, session.Player).Where(c => c is SaleContract { Side: SaleSide.Wanted } s && s.Good == good && s.PostedBy != session.Player.Id));
        foreach (Contract candidate in targets)
        {
            if (candidate.IsOpen && PlayerActions.CheckFill(context, session.Player, (SaleContract)candidate) is { Ok: true, Source: { Kind: FillSourceKind.Storage } source } && source.At == at
                && DealEstimate.For(context, session.Player, session.PlayerShip, candidate) is { } estimate && estimate.TakeHome.Amount > 0m
                && estimate.ArrivalTick <= candidate.GraceEndsTick && candidate.Warranty?.Rejectable(estimate.ArrivalQuality) != true
                && Take(session, candidate))
            {
                return true;
            }
        }

        return false;
    }

    // Its post is still up, or signed and the goods not yet in its storage: the ship waits for them.
    private bool Waiting(GameSession session)
    {
        if (_plan is not { } plan)
        {
            return false;
        }

        Contract post = session.Context.Contract(plan.Post);
        if (post.IsOpen || post.Status == ContractStatus.Active)
        {
            return true;
        }

        _plan = null; // nobody sold, or the sale failed
        return false;
    }

    // A delivered wanted Sale on its board that pays well over the goods at a producer's door and the trip: post for the goods there.
    private bool PlanPurchase(GameSession session)
    {
        SimulationContext context = session.Context;
        Ship ship = session.PlayerShip;
        Plan? best = null;
        decimal bestMargin = 0m;
        decimal offer = 0m;
        foreach (Contract contract in Visibility.OpenContractsVisibleTo(context, session.Player))
        {
            if (contract is not SaleContract { Side: SaleSide.Wanted } wanted || wanted.PostedBy == session.Player.Id || wanted.Quantity > ship.FreeSpace
                || wanted.DeadlineTick < session.Now + 72)
            {
                continue;
            }

            decimal unit = DoorPrice(session, wanted.Good);
            foreach (ActorState producer in context.ProducersOf(wanted.Good))
            {
                if (context.StationOperatorAt(producer.Home) is null || context.FindPath(ship.Location, producer.Home) is not { } repo
                    || context.FindPath(producer.Home, wanted.Place) is not { } laden || producer.Home == wanted.Place)
                {
                    continue;
                }

                long hours = PriceModel.TravelTicks(repo.DistanceTicks + laden.DistanceTicks, ship.Class.SpeedMultiplier) + 24;
                if (session.Now + hours > wanted.DeadlineTick)
                {
                    continue;
                }

                decimal tax = Terms.TaxRate(context, context.Location(producer.Home).Reach);
                decimal trip = (ship.Class.RunningCostPerTick.Amount * hours) + context.Config.DockingFee.Amount;
                decimal budget = ((wanted.OfferedPrice.Amount * (1m - Margin)) - trip) / (1m + tax);
                decimal margin = budget - (unit * wanted.Quantity);
                if (margin > bestMargin && margin > unit * wanted.Quantity * 0.1m && session.Player.Credits.Amount > budget * (1m + tax) * 1.5m)
                {
                    best = new Plan(new ContractId(0), wanted.Id, wanted.Good, producer.Home, Math.Round(budget, 2));
                    bestMargin = margin;
                    offer = unit * wanted.Quantity * 0.9m;
                }
            }
        }

        if (best is null)
        {
            return false;
        }

        SaleContract target = (SaleContract)context.Contract(best.Target);
        PostOutcome outcome = session.Post(new PostDraft(
            PostKind.Wanted, best.Good, target.Quantity, best.Source, null, new Offer(Credits.Of(Math.Round(offer, 2)), session.Now + 24, TermPackage.Standard)));
        if (outcome.Post is not { } post)
        {
            return false;
        }

        Posts++;
        _plan = best with { Post = post.Id };
        return true;
    }

    // What it reckons goods cost at a producer's door: the cheapest lot a producer of them has on its board, else their base value.
    private static decimal DoorPrice(GameSession session, GoodType good)
    {
        SimulationContext context = session.Context;
        decimal unit = Goods.Info(good).BaseValue;
        foreach (Contract contract in Visibility.OpenContractsVisibleTo(context, session.Player))
        {
            if (contract is SaleContract { Side: SaleSide.ForSale } lot && lot.Good == good && lot.Quantity > 0m && context.Actor(lot.PostedBy).Producer?.Good == good)
            {
                unit = Math.Min(unit, lot.OfferedPrice.Amount / lot.Quantity);
            }
        }

        return unit;
    }

    // The board's best deal: countered first if its post has waited a while and there is time for a reply, else taken as posted.
    private void HaggleOrTake(GameSession session)
    {
        if (BestDeal(session) is not { } best)
        {
            return;
        }

        Contract contract = best.Contract;
        SimulationContext context = session.Context;
        bool haggled = session.PlayerNegotiations.Any(n => n.Contract == contract.Id);
        bool time = best.Estimate.ArrivalTick + Haggling.SlowestReply <= contract.DeadlineTick;
        if (!haggled && time && session.Now - contract.PostedTick >= HaggleAfterHours
            && session.Counter(contract.Id, Offer.Of(contract) with { Price = Credits.Of(Math.Round(contract.OfferedPrice.Amount * Ask, 2)) }) == DealBlock.None)
        {
            Counters++;
            return; // the ship waits for the reply, which comes within half a day
        }

        if (context.NegotiationWith(contract.Id, session.Player.Id) is null)
        {
            Take(session, contract);
        }
    }
}
