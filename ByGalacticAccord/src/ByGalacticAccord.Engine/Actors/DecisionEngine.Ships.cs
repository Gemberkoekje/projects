using System;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

public static partial class DecisionEngine
{
    /// <summary>The window over which an owner judges how busy its ships are: thirty days.</summary>
    public const long FleetWindowHours = 30 * 24;

    /// <summary>No owner changes its fleet in the first fortnight: its ships haven't been busy or idle long enough to tell.</summary>
    private const long FleetSettleHours = 14 * 24;

    /// <summary>Busier than this, over the window, a company wants another ship (if its ambition and purse allow); a pilot, busier still.</summary>
    public const decimal GrowAbove = 0.80m;

    public const decimal PilotGrowAbove = 0.90m;

    /// <summary>Idler than this, a trader sells a ship that has sat free for <see cref="IdleDaysToSell"/> days.</summary>
    public const decimal ShrinkBelow = 0.40m;

    private const long IdleDaysToSell = 10;

    /// <summary>The most ships a shipping company runs, and an independent pilot.</summary>
    public const int CompanyFleetCap = 3;

    public const int PilotFleetCap = 2;

    /// <summary>How long a trader's Build for a new ship runs, and a ship it posts for sale.</summary>
    private const long BuildHours = 30 * 24;

    private const long ShipSaleHours = 60 * 24;

    /// <summary>How long a salvage yard keeps a ship it can't sell before it breaks it up.</summary>
    public const long ScrapAfterHours = 365 * 24;

    /// <summary>What a salvage yard keeps in hand beyond a ship's price before it buys one.</summary>
    private const decimal SalvageCushion = 10_000m;

    /// <summary>
    /// The share of its running cost a ship laid up for sale still costs. Not in the design (design §6.5 has every ship cost the same each day,
    /// earning or not): at full cost a listed ship bled its seller for months (plan S7).
    /// </summary>
    public const decimal LaidUpShare = 0.25m;

    /// <summary>
    /// How busy a fleet has been: the mean of its ships' shares of the last <see cref="FleetWindowHours"/> spent on jobs. A fleet
    /// of none counts as busy: it has nothing to fly.
    /// </summary>
    public static decimal FleetUtilization(SimulationContext context, ActorState owner)
    {
        if (owner.Ships.Count == 0)
        {
            return 1m;
        }

        decimal sum = 0m;
        foreach (ShipId id in owner.Ships)
        {
            sum += context.Ship(id).Utilization(context.CurrentTick, FleetWindowHours);
        }

        return sum / owner.Ships.Count;
    }

    /// <summary>
    /// Whether a trader wants another ship (design §4.2, §6.3: AI companies order ships when they want to grow): its ships have
    /// been busy (<see cref="GrowAbove"/>, the threshold eased by its growth ambition), its fleet is under its cap, and nothing
    /// is already on order or bought for it. A trader with no ship always wants one.
    /// </summary>
    public static bool WantsShip(SimulationContext context, ActorState trader)
    {
        if (!trader.MovesCargo || trader.IsPlayer || context.CurrentTick < FleetSettleHours || trader.Solvency != SolvencyStatus.None)
        {
            return false; // a trader warned for its debts doesn't grow
        }

        int cap = trader.Role == ActorRole.ShippingCompany ? CompanyFleetCap : PilotFleetCap;
        if (trader.Ships.Count >= cap || context.LiveContractsOf(trader.Id).Any(c => c is BuildContract b && b.Client == trader.Id))
        {
            return false;
        }

        decimal threshold = (trader.Role == ActorRole.ShippingCompany ? GrowAbove : PilotGrowAbove) + (0.15m * (0.5m - trader.Personality.GrowthAmbition));
        return trader.Ships.Count == 0 || FleetUtilization(context, trader) >= threshold;
    }

    /// <summary>
    /// Whether a trader's one ship doesn't pay (plan S7: "an actor can't change its fleet"): it costs half again what a Light Freighter
    /// does to run, it worked under a quarter of the last month on a ship it has had two months, and its owner's savings are worth
    /// less than the ship. It sells it while it can still pay to show it further, and with what it fetches buys a cheaper ship.
    /// </summary>
    private static bool Uneconomic(SimulationContext context, ActorState trader)
    {
        if (trader.Ships.Count != 1)
        {
            return false;
        }

        Ship ship = context.Ship(trader.Ships[0]);
        return context.CurrentTick - ship.OwnedSince >= 60 * GameTime.HoursPerDay
               && ship.Stats.RunningCostPerTick.Amount > Designs.Stats(Design.LightFreighter).RunningCostPerTick.Amount * 1.5m
               && ship.Utilization(context.CurrentTick, FleetWindowHours) < 0.25m
               && trader.Credits.Amount < ship.Stats.Value.Amount;
    }

    /// <summary>The design a trader wants: that of its busiest ship, which its trade has shown it needs; a Light Freighter if it has none.</summary>
    public static Design WantedDesign(SimulationContext context, ActorState trader)
    {
        Design? best = null;
        decimal busiest = -1m;
        foreach (ShipId id in trader.Ships)
        {
            Ship ship = context.Ship(id);
            decimal busy = ship.Utilization(context.CurrentTick, FleetWindowHours);
            if (ship.Design is { } design && busy > busiest)
            {
                best = design;
                busiest = busy;
            }
        }

        return best ?? Design.LightFreighter;
    }

    /// <summary>
    /// A trader's fleet on its heartbeat (design §6.3, §6.5): wanting another ship, and with the price and half its fleet's value
    /// in hand, it buys the best ship for sale it can see that is worth what it asks, else orders its design from the yards with
    /// a Build; idle, it sells its idlest ship once it has sat free for <see cref="IdleDaysToSell"/> days, keeping at least one
    /// working, and one up for sale at a time.
    /// </summary>
    private static void ManageFleet(SimulationContext context, ActorState trader)
    {
        if (context.CurrentTick < FleetSettleHours)
        {
            return;
        }

        if (WantsShip(context, trader))
        {
            Acquire(context, trader);
            return;
        }

        // One ship up for sale at a time; it stays up, a little cheaper each fortnight, until it sells.
        if (trader.Ships.Any(id => context.Ship(id).ListedFor is not null) && trader.Ships.Count > 0)
        {
            Ship listed = context.Ship(trader.Ships.First(id => context.Ship(id).ListedFor is not null));
            if (context.Contract(listed.ListedFor!.Value).IsOpen)
            {
                return;
            }
        }

        // Surplus: with more than one ship working and the fleet idle, the idlest ship that has sat free ten days goes (the dearest
        // to run, if two are as idle). A pilot whose one ship doesn't pay sells it, to buy a cheaper one with what it fetches. A
        // trader in debt (design §16.8) sells its idlest free ship however busy the fleet, to pay it off: in debt it can post no
        // bond, so it can take no work in Accord space.
        bool warned = trader.Credits.Amount < 0m;
        int working = trader.Ships.Count(id => context.Ship(id).ListedFor is null);
        if (working == 1 && Uneconomic(context, trader))
        {
            working = 2; // it may sell its only ship
        }
        else if (working <= 1 || (!warned && FleetUtilization(context, trader) >= ShrinkBelow))
        {
            return;
        }

        Ship? idlest = null;
        foreach (ShipId id in trader.Ships)
        {
            Ship ship = context.Ship(id);
            if (!ship.IsFree || ship.HoldQuantity > 0m || (!warned && context.CurrentTick - ship.FreeSince < IdleDaysToSell * GameTime.HoursPerDay))
            {
                continue;
            }

            decimal busy = ship.Utilization(context.CurrentTick, FleetWindowHours);
            decimal best = idlest?.Utilization(context.CurrentTick, FleetWindowHours) ?? decimal.MaxValue;
            if (busy < best || (busy == best && ship.Stats.RunningCostPerTick > idlest!.Stats.RunningCostPerTick))
            {
                idlest = ship;
            }
        }

        if (idlest is not null)
        {
            // Priced at 85% of its value, 5% less for each fortnight it has sat idle beyond the first ten days, down to 40%.
            decimal value = idlest.Stats.Value.Amount;
            decimal fortnights = Math.Floor((context.CurrentTick - idlest.FreeSince - (IdleDaysToSell * GameTime.HoursPerDay)) / (decimal)MarkdownHours);
            decimal share = Math.Max(0.4m, 0.85m - (0.05m * fortnights));
            Market.PostShipSale(
                context, trader, idlest, Credits.Of(Math.Round(value * share, 2)), Credits.Of(Math.Round(value * Math.Min(share, 0.6m), 2)),
                context.CurrentTick + ShipSaleHours);
        }
    }

    // Buy a ship for sale worth its price, or order one.
    private static void Acquire(SimulationContext context, ActorState trader)
    {
        Design design = WantedDesign(context, trader);
        decimal fleet = trader.Ships.Sum(id => context.Ship(id).Stats.Value.Amount);
        decimal spare = trader.Credits.Amount - (fleet * 0.5m);
        Sight sight = Visibility.Of(context, trader);

        ShipSaleContract? best = null;
        Offer? bestOffer = null;
        decimal bestGain = 0m;
        foreach (ShipSaleContract sale in Known<ShipSaleContract>(context, trader))
        {
            // Its own design at up to 95% of its value; another design only as a bargain, up to 85% (a shipless trader takes any).
            Ship ship = context.Ship(sale.Ship);
            if (!Knowledge.BelievesOpen(context, trader, sight, sale) || ship.Design is null)
            {
                continue;
            }

            decimal worth = ship.Stats.Value.Amount * (trader.Ships.Count == 0 || ship.Design == design ? 0.95m : 0.85m);
            Offer offer = Offer.Of(sale);
            if (offer.Price.Amount > worth)
            {
                if (Market.HagglesInGameTime(context.Actor(sale.Seller))
                    || ClosedForm(context, sale, PosterReservation(context, sale), trader, worth) is not { } agreed)
                {
                    continue;
                }

                offer = offer with { Price = Credits.Of(agreed) };
            }

            decimal gain = worth - offer.Price.Amount;
            if (gain > bestGain && spare >= offer.Price.Amount * (1m + sale.TaxRate))
            {
                best = sale;
                bestOffer = offer;
                bestGain = gain;
            }
        }

        if (best is not null && bestOffer is not null)
        {
            Signing signed = Market.SignAgreed(best, bestOffer, () => Market.BuyShip(context, trader, best, bestOffer.Price));
            NoteHaggled(context, best, signed);
            return;
        }

        // Nothing for sale suits: order one from the yards, priced at the design's value and worth a fifth more to it.
        decimal value = Designs.Stats(design).Value.Amount;
        decimal tax = Terms.TaxRate(context, context.Location(trader.Home).Reach);
        if (spare < value * (1m + tax))
        {
            return; // it saves toward it: its working capital counts the ship it wants (WorkingCapital)
        }

        Market.PostBuild(
            context, trader, Designs.Parts(design), trader.Home, Credits.Of(Math.Round(value, 2)), Credits.Of(Math.Round(value * 1.2m, 2)),
            context.CurrentTick + BuildHours);
    }

    /// <summary>
    /// What a trader keeps for a ship it wants, on top of its working capital (<see cref="WorkingCapital"/>): the price of a Build it
    /// has posted and not yet had signed, or the value of the design it wants.
    /// </summary>
    private static decimal GrowthFund(SimulationContext context, ActorState trader)
    {
        foreach (Contract contract in context.LiveContractsOf(trader.Id))
        {
            if (contract is BuildContract { IsOpen: true } build && build.Client == trader.Id)
            {
                return build.OfferedPrice.Amount * (1m + context.Config.TaxRate);
            }
        }

        return WantsShip(context, trader) ? Designs.Stats(WantedDesign(context, trader)).Value.Amount : 0m;
    }

    /// <summary>
    /// A salvage yard's heartbeat (design §15.5, Edge Salvage): ships it bought elsewhere fly home; ships at home go up for sale,
    /// second-hand with their histories, on the frontier with no escrow, a little cheaper each fortnight they sit, and are broken up
    /// once they have sat a year unsold; and while its lot has room it buys the cheapest ship for sale it can see at its price, as
    /// posted or in closed form.
    /// </summary>
    public static void RunSalvage(SimulationContext context, ActorState salvage)
    {
        if (salvage.Salvage is not { } profile)
        {
            return;
        }

        foreach (ShipId id in salvage.Ships.ToList())
        {
            Ship ship = context.Ship(id);
            if (!ship.IsFree)
            {
                continue;
            }

            if (ship.Location != salvage.Home)
            {
                Market.Ferry(context, ship, salvage.Home);
                continue;
            }

            // A ship that has sat unsold on its lot for a year is broken up: nobody wants it at any price it can ask.
            if (context.CurrentTick - ship.OwnedSince >= ScrapAfterHours)
            {
                context.ScrapShip(ship);
                context.Record(new ShipScrappedEntry(context.CurrentTick, salvage.Id, ship.Id));
                continue;
            }

            decimal value = ship.Stats.Value.Amount;
            decimal fortnights = Math.Floor((context.CurrentTick - ship.FreeSince) / (decimal)MarkdownHours);
            decimal share = Math.Max(profile.FloorShare, profile.AskShare - (LotMarkdown * fortnights));
            Market.PostShipSale(
                context, salvage, ship, Credits.Of(Math.Round(value * share, 2)), Credits.Of(Math.Round(value * profile.FloorShare, 2)),
                context.CurrentTick + LotPostHours);
        }

        if (salvage.Ships.Count >= profile.LotLimit)
        {
            return;
        }

        Sight sight = Visibility.Of(context, salvage);
        foreach (ShipSaleContract sale in Known<ShipSaleContract>(context, salvage))
        {
            if (!Knowledge.BelievesOpen(context, salvage, sight, sale) || Market.HagglesInGameTime(context.Actor(sale.Seller)))
            {
                continue;
            }

            decimal limit = context.Ship(sale.Ship).Stats.Value.Amount * profile.BuyShare;
            Offer offer = Offer.Of(sale);
            if (offer.Price.Amount > limit)
            {
                if (ClosedForm(context, sale, PosterReservation(context, sale), salvage, limit) is not { } agreed)
                {
                    continue;
                }

                offer = offer with { Price = Credits.Of(agreed) };
            }

            if (salvage.Credits.Amount < (offer.Price.Amount * (1m + sale.TaxRate)) + SalvageCushion)
            {
                continue; // it keeps enough in hand to run its lot meanwhile
            }

            Signing signed = Market.SignAgreed(sale, offer, () => Market.BuyShip(context, salvage, sale, offer.Price));
            NoteHaggled(context, sale, signed);
            if (signed == Signing.Signed)
            {
                AfterBuyingShip(context, salvage, context.Ship(sale.Ship));
                return;
            }
        }
    }

    // A salvage yard brings a ship it bought home; a trader flies a ship from where it bought it.
    private static void AfterBuyingShip(SimulationContext context, ActorState buyer, Ship ship)
    {
        if (buyer.Salvage is not null)
        {
            Market.Ferry(context, ship, buyer.Home);
        }
    }

    /// <summary>
    /// A buyer answering the player's ship for sale (design §6.3): a salvage yard with room on its lot, at its buying share of
    /// the ship's value; a trader that wants a ship of that design (or any, if it has none), at most 95% of its value; no more
    /// than either can pay.
    /// </summary>
    private static Quote? ShipBuyerQuote(SimulationContext context, ActorState buyer, ShipSaleContract sale, Offer offer)
    {
        Ship ship = context.Ship(sale.Ship);
        decimal limit;
        if (buyer.Salvage is { } profile)
        {
            if (buyer.Ships.Count >= profile.LotLimit)
            {
                return null;
            }

            limit = ship.Stats.Value.Amount * profile.BuyShare;
        }
        else
        {
            if (!WantsShip(context, buyer) || ship.Design is null)
            {
                return null;
            }

            limit = ship.Stats.Value.Amount * (buyer.Ships.Count == 0 || ship.Design == WantedDesign(context, buyer) ? 0.95m : 0.85m);
        }

        decimal ceiling = Math.Min(limit, Math.Max(0m, buyer.Credits.Amount) / (1m + sale.TaxRate));
        return ceiling <= 0m ? null : new Quote(Credits.Of(Math.Round(ceiling, 2)), Pays: true, offer, new Plan(PlanKind.BuyShip));
    }
}
