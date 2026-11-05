using System;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// Insolvency (design §5.4, §16.3). When <see cref="Solvency.Review"/> finds an actor insolvent, <see cref="Begin"/> ends what it
/// was doing: its haggles end, its open posts are withdrawn, and every contract it signed is cancelled (an insolvent performer for
/// default, its bond forfeited; an insolvent payer walking away for its break fee). From then on the Accord clearing house runs it
/// on its heartbeat (<see cref="Run"/>): each ship, once it is free, has its owner's goods set down and goes up in a fire sale; its
/// goods go up as fire-sale lots; every fire sale is shown in every system at once, free, and marked down week by week. What sells
/// pays down the debt. When nothing is left, or after <see cref="LongestHours"/> (what is unsold is then broken up or written off),
/// the actor dissolves: the clearing house writes off what it still owes, and anything left over goes to its owners. The same for
/// every actor; for the player, insolvency ends the game.
/// </summary>
public static class Liquidation
{
    /// <summary>The longest a liquidation runs; then what hasn't sold is broken up or written off.</summary>
    public const long LongestHours = 60 * 24;

    /// <summary>A fire-sale ship's first ask, as a share of its value; it loses <see cref="MarkdownShare"/> a week, down to <see cref="ShipFloorShare"/>.</summary>
    public const decimal ShipAskShare = 0.6m;

    /// <summary>The least a fire-sale ship is asked for, as a share of its value.</summary>
    public const decimal ShipFloorShare = 0.3m;

    /// <summary>A fire-sale lot's first ask, as a share of the goods' base value (at their quality); marked down like a ship.</summary>
    public const decimal GoodsAskShare = 0.6m;

    /// <summary>The least a fire-sale lot is asked for, as a share of the goods' base value.</summary>
    public const decimal GoodsFloorShare = 0.2m;

    /// <summary>How much of the first ask a fire sale loses each week.</summary>
    public const decimal MarkdownShare = 0.1m;

    /// <summary>How long a fire-sale post runs before it is posted again, lower.</summary>
    public const long PostHours = 4 * 24;

    private const long Week = 7 * 24;

    /// <summary>
    /// An actor becomes insolvent (design §5.4): recorded (the player is told: its game is over), and everything it was doing ends.
    /// </summary>
    internal static void Begin(SimulationContext context, ActorState actor, Credits debt, Credits assets, InsolvencyReason reason)
    {
        long now = context.CurrentTick;
        actor.Solvency = SolvencyStatus.Liquidating;
        actor.InsolventSince = now;
        actor.WarnedSince = null;
        actor.DebtAtInsolvency = debt;
        context.NoteInsolvent(actor);
        context.NoteFailure(FailureKind.Insolvency);
        JournalEntry insolvent = context.Record(new InsolventEntry(now, actor.Id, debt, assets, reason) { Cause = actor.SolvencyEntry });
        actor.SolvencyEntry = insolvent.Seq;
        if (actor.IsPlayer)
        {
            context.RaisePlayerAlert(insolvent);
        }

        // Its haggles end: it can perform nothing it agrees to.
        foreach (Negotiation thread in context.Negotiations.Values.Where(n => n.IsOpen && n.Involves(actor.Id)).OrderBy(n => n.Id.Value).ToList())
        {
            thread.Close(NegotiationEnd.CannotPerform, actor.Id, now);
            context.Record(new NegotiationEndedEntry(now, thread.Contract, thread.Id, actor.Id, thread.Other(actor.Id), NegotiationEnd.CannotPerform, null)
            {
                Cause = insolvent.Seq,
            });
        }

        // Its open posts come down, then every contract it signed is cancelled on its insolvency: as the performer, for default (the
        // payer takes its bond); as the payer, walking away (the performer gets its break fee). The insolvent party is the canceller.
        foreach (Contract contract in context.LiveContractsOf(actor.Id).ToList())
        {
            if (contract.IsOpen && contract.PostedBy == actor.Id)
            {
                Market.Withdraw(context, contract, insolvent.Seq);
            }
        }

        foreach (Contract contract in context.LiveContractsOf(actor.Id).ToList())
        {
            if (contract.Status == ContractStatus.Active && contract is not StorageContract)
            {
                Settlement.Cancel(context, contract, actor, CancelReason.Insolvency, insolvent.Seq);
            }
        }
    }

    /// <summary>The clearing house's turn with an insolvent actor, on its heartbeat: sell what it can, and dissolve it when nothing is left.</summary>
    internal static void Run(SimulationContext context, ActorState actor)
    {
        if (actor.Solvency != SolvencyStatus.Liquidating)
        {
            return;
        }

        long now = context.CurrentTick;
        long since = actor.InsolventSince ?? now;
        bool overdue = now - since >= LongestHours;
        decimal markdown = MarkdownShare * Math.Floor((now - since) / (decimal)Week);
        long cause = actor.SolvencyEntry ?? -1;

        SellShips(context, actor, overdue, markdown, cause);
        SellGoods(context, actor, overdue, markdown, cause);
        if (Done(context, actor))
        {
            Dissolve(context, actor, cause);
        }
    }

    // Each ship, once free: its owner's goods aboard set down in storage where it is, then up for sale (or, overdue, broken up).
    private static void SellShips(SimulationContext context, ActorState actor, bool overdue, decimal markdown, long cause)
    {
        foreach (Ship ship in actor.Ships.Select(context.Ship).ToList())
        {
            if (ship.ListedFor is { } listed)
            {
                if (overdue)
                {
                    Market.Withdraw(context, context.Contract(listed), cause);
                }
                else
                {
                    continue;
                }
            }

            if (!ship.IsFree)
            {
                continue; // still flying: it finishes its trip first
            }

            foreach (Lot lot in ship.Hold.Where(l => l.Owner == actor.Id).ToList())
            {
                Cargo goods = context.Unload(ship, actor.Id, lot.Good, lot.Quantity);
                context.PutInStorage(actor, ship.Location, goods);
            }

            if (ship.HoldQuantity > 0m)
            {
                continue; // someone else's goods aboard: set down on arrival, never here
            }

            if (overdue)
            {
                context.ScrapShip(ship);
                context.Record(new ShipScrappedEntry(context.CurrentTick, actor.Id, ship.Id) { Cause = cause });
                continue;
            }

            decimal value = ship.Stats.Value.Amount;
            decimal share = Math.Max(ShipFloorShare, ShipAskShare - markdown);
            Credits price = Credits.Of(Math.Round(value * share, 2));
            ShipSaleContract sale = Market.PostShipSale(
                context, actor, ship, price, Credits.Of(Math.Round(value * ShipFloorShare, 2)), context.CurrentTick + PostHours, cause);
            ShowEverywhere(context, sale);
        }
    }

    // Its goods, at home and in storage: up as fire-sale lots (or, overdue, written off); a scrap of a unit written off at once.
    private static void SellGoods(SimulationContext context, ActorState actor, bool overdue, decimal markdown, long cause)
    {
        foreach (GoodType good in Goods.All)
        {
            decimal free = actor.Available(good);
            if (free <= 0m)
            {
                continue;
            }

            if (overdue || free < 1m)
            {
                Cargo gone = context.TakeFromStock(actor, good, free, reserved: false);
                context.Lose(good, gone.Quantity);
                continue;
            }

            PostLot(context, actor, good, free, actor.Home, Holding.StockAt(actor.Home), 1m, markdown, cause);
        }

        foreach (StorageContract storage in context.StoragesOf(actor.Id).ToList())
        {
            foreach (Lot lot in storage.Lots.ToList())
            {
                decimal free = storage.Available(lot.Good);
                if (free <= 0m)
                {
                    continue;
                }

                if (overdue || free < 1m)
                {
                    Cargo gone = context.TakeFromStorage(actor, storage.Location, lot.Good, free, reserved: false);
                    context.Lose(lot.Good, gone.Quantity);
                    continue;
                }

                decimal quality = storage.Peek(lot.Good, context.CurrentTick)?.Quality ?? 1m;
                PostLot(context, actor, lot.Good, free, storage.Location, Holding.StorageAt(storage.Location), quality, markdown, cause);
            }
        }

        if (overdue)
        {
            foreach (Contract open in context.LiveContractsOf(actor.Id).Where(c => c.IsOpen && c.FireSale).ToList())
            {
                Market.Withdraw(context, open, cause);
            }
        }
    }

    private static void PostLot(
        SimulationContext context, ActorState actor, GoodType good, decimal quantity, LocationId place, Holding holding, decimal quality, decimal markdown, long cause)
    {
        decimal unit = Goods.Info(good).BaseValue * Math.Max(0.2m, quality);
        decimal share = Math.Max(GoodsFloorShare, GoodsAskShare - markdown);
        Credits price = Credits.Of(Math.Max(0.01m, Math.Round(unit * share * quantity, 2)));
        SaleContract lot = Market.PostSale(
            context, actor, SaleSide.ForSale, good, quantity, place, price, Credits.Of(Math.Round(unit * GoodsFloorShare * quantity, 2)),
            context.CurrentTick + PostHours, holding, cause: cause);
        ShowEverywhere(context, lot);
    }

    // The Accord publishes a liquidation's sales: shown in every system at once, free (design §5.4).
    private static void ShowEverywhere(SimulationContext context, Contract contract)
    {
        contract.FireSale = true;
        foreach (LocationId location in context.Locations.Keys.OrderBy(l => l.Value))
        {
            contract.ShowAt(location, context.CurrentTick);
        }
    }

    // Nothing left to sell or settle: no ships, no goods anywhere, no contracts.
    private static bool Done(SimulationContext context, ActorState actor) =>
        actor.Ships.Count == 0
        && Goods.All.All(g => actor.Stock(g) <= 0m)
        && !context.StoragesOf(actor.Id).Any()
        && !context.LiveContractsOf(actor.Id).Any()
        && !context.Ships.Values.Any(s => s.Hold.Any(l => l.Owner == actor.Id));

    private static void Dissolve(SimulationContext context, ActorState actor, long cause)
    {
        Credits raised = actor.Credits + actor.DebtAtInsolvency;
        Credits writtenOff = context.WriteOffDebt(actor);
        Credits leftOver = actor.Credits.Amount > 0m ? context.ApplySink(actor, actor.Credits, SinkKind.PayOut) : Credits.Zero;
        actor.Solvency = SolvencyStatus.Dissolved;
        context.NoteDissolved(actor);
        context.Record(new ActorDissolvedEntry(context.CurrentTick, actor.Id, raised, writtenOff, leftOver) { Cause = cause });
    }
}
