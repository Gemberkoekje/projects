using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

public static partial class DecisionEngine
{
    /// <summary>How long a trader's own goods sit in storage before it posts them for sale.</summary>
    private const long SellOffAfterHours = 5 * 24;

    /// <summary>How long they sit, unsold, before it writes them off: leftovers too small or too spoiled for anyone.</summary>
    private const long WriteOffAfterHours = 30 * 24;

    private enum OptionKind
    {
        Haul,
        Fill,
    }

    /// <summary>One thing a free ship could do, scored: take a haul, or fill a delivered Sale from a source, on an offer, at a throttle.</summary>
    private readonly record struct TraderOption(OptionKind Kind, Contract Contract, Offer Offer, FillSource? Source, decimal Utility, Throttle Throttle);

    /// <summary>
    /// Trader heartbeat (design §4.1): each free ship takes the best of the hauls and delivered Sales it can see; goods
    /// left aboard go into storage; a trader with every ship busy may still fill a Sale by having the goods hauled;
    /// goods that sit too long in storage are posted for sale.
    /// </summary>
    public static void RunTrader(SimulationContext context, ActorState trader)
    {
        List<Contract> known = Known<Contract>(context, trader).ToList();
        foreach (Ship ship in trader.Ships.Select(context.Ship).Where(s => s.IsFree).ToList())
        {
            // What it believes open may have been taken since the news reported it: it tries its best, and its next best once.
            for (int attempt = 0; attempt < 2 && BestOption(context, trader, ship, known) is { } option; attempt++)
            {
                if (Commit(context, trader, ship, option) != Signing.NotOpen)
                {
                    break;
                }
            }

            if (ship.IsFree)
            {
                Stow(context, trader, ship);
            }
        }

        ArrangeTransport(context, trader);
        if (!trader.Ships.Select(context.Ship).Any(s => s.IsFree))
        {
            Broker(context, trader);
        }

        SellOff(context, trader);
        ManageFleet(context, trader);
    }

    /// <summary>
    /// Where <paramref name="seller"/> could get the goods for a delivered wanted Sale: its own goods aboard
    /// <paramref name="ship"/> or in storage, or a producer with them free, at the producer's ask.
    /// </summary>
    public static IEnumerable<FillSource> FillSources(SimulationContext context, ActorState seller, SaleContract wanted, Ship? ship) =>
        FillSources(context, seller, wanted, ship, Visibility.Of(context, seller));

    /// <summary>
    /// Where <paramref name="seller"/> could get the goods for a delivered wanted Sale: its own goods aboard
    /// <paramref name="ship"/> or in storage, or a producer it knows has them free, at the ask it knows (design §7, D32: live
    /// where it is present, else as last read; a producer that turned it down in the last day it leaves out).
    /// </summary>
    internal static IEnumerable<FillSource> FillSources(SimulationContext context, ActorState seller, SaleContract wanted, Ship? ship, Sight sight)
    {
        if (ship is not null && ship.LotOf(seller.Id, wanted.Good) is { } lot && lot.Quantity >= wanted.Quantity)
        {
            yield return FillSource.FromHold(ship);
        }

        foreach (StorageContract storage in context.StoragesOf(seller.Id))
        {
            if (storage.Available(wanted.Good) >= wanted.Quantity && context.FindPath(storage.Location, wanted.Place) is not null)
            {
                yield return FillSource.FromStorage(storage.Location);
            }
        }

        foreach (ActorState producer in context.ProducersOf(wanted.Good))
        {
            if (producer.Id != seller.Id && !producer.IsPlayer && Knowledge.KnownAsk(context, seller, producer, wanted.Good, wanted.Quantity, sight) is { } ask
                && context.FindPath(producer.Home, wanted.Place) is not null)
            {
                yield return FillSource.FromProducer(producer, Credits.Of(Math.Round(ask.Amount * wanted.Quantity, 2)));
            }
        }
    }

    private static TraderOption? BestOption(SimulationContext context, ActorState trader, Ship ship, List<Contract> known)
    {
        TraderOption? best = null;
        Sight sight = Visibility.Of(context, trader);
        foreach (HaulContract haul in known.OfType<HaulContract>())
        {
            if (Knowledge.BelievesOpen(context, trader, sight, haul) && ScoreHaul(context, trader, ship, haul) is { } option && (best is null || option.Utility > best.Value.Utility))
            {
                best = option;
            }
        }

        foreach (SaleContract wanted in known.OfType<SaleContract>())
        {
            if (!Knowledge.BelievesOpen(context, trader, sight, wanted) || wanted.Side != SaleSide.Wanted || wanted.Quantity > ship.CargoCapacity)
            {
                continue;
            }

            foreach (FillSource source in FillSources(context, trader, wanted, ship, sight))
            {
                if (ScoreFill(context, trader, ship, wanted, source) is { } option && (best is null || option.Utility > best.Value.Utility))
                {
                    best = option;
                }
            }
        }

        return best;
    }

    private static TraderOption? ScoreHaul(SimulationContext context, ActorState trader, Ship ship, HaulContract haul)
    {
        Offer posted = Offer.Of(haul);
        if (Price(context, trader, haul, posted, p => HaulTake(context, trader, ship, haul, posted with { Price = Credits.Of(p) })) is not { } deal
            || !Market.CanFund(haul, context.Actor(haul.Shipper), deal.Offer.Price))
        {
            return null;
        }

        return new TraderOption(OptionKind.Haul, haul, deal.Offer, null, deal.Utility, deal.Throttle);
    }

    private static TraderOption? ScoreFill(SimulationContext context, ActorState trader, Ship ship, SaleContract wanted, FillSource source)
    {
        Offer posted = Offer.Of(wanted);
        if (Price(context, trader, wanted, posted, p => FillTake(context, trader, ship, wanted, source, posted with { Price = Credits.Of(p) })) is not { } deal
            || !Market.CanFund(wanted, context.Actor(wanted.PostedBy), deal.Offer.Price))
        {
            return null;
        }

        return new TraderOption(OptionKind.Fill, wanted, deal.Offer, source, deal.Utility, deal.Throttle);
    }

    /// <summary>
    /// What a ship taking a haul on <paramref name="offer"/> is worth to the trader (design §4.3), and the least fee its costs
    /// allow, at the better throttle (design §16.7: pushing beats a late fee and risks the cargo); null if it can't do it on those
    /// terms at either: no room, no route, arriving past the grace, or goods the shipper would refuse. The terms are the offer's,
    /// rebuilt from its price as an agreed counter would be.
    /// </summary>
    private static Take? HaulTake(SimulationContext context, ActorState trader, Ship ship, HaulContract haul, Offer offer) =>
        Better(HaulTake(context, trader, ship, haul, offer, Throttle.Cruise), HaulTake(context, trader, ship, haul, offer, Throttle.Push), offer.Deadline);

    // An AI pushes to beat a late fee (design §8.4): only when at cruise it would be late, or past its grace, and pushing is worth more.
    // On time at cruise, it doesn't risk the cargo to be early.
    private static Take? Better(Take? cruise, Take? push, long deadline) =>
        push is not { } p ? cruise
        : cruise is not { } c ? p
        : c.Arrival > deadline && p.Utility > c.Utility ? p
        : c;

    private static Take? HaulTake(SimulationContext context, ActorState trader, Ship ship, HaulContract haul, Offer offer, Throttle throttle)
    {
        if (haul.Quantity > ship.FreeSpace || context.FindPath(haul.Origin, haul.Destination) is not { } laden
            || context.FindPath(ship.Location, haul.Origin) is not { } repo)
        {
            return null;
        }

        ShipStats cls = ship.Stats;
        long repoTicks = PriceModel.TravelTicks(repo.DistanceTicks, cls.Speed);
        long ladenTicks = Throttles.LadenTicks(context.Config, laden.DistanceTicks, cls, throttle);
        decimal running = cls.RunningCostPerTick.Amount * (repoTicks + ladenTicks);
        decimal pilotMin = PriceModel.PilotMinimumFee(cls, laden.DistanceTicks, laden.MaxDanger, trader.Personality).Amount
                           + (cls.RunningCostPerTick.Amount * repoTicks);
        ContractTerms terms = TermsOn(haul, offer);
        if (haul.UsesEscrow && !Solvency.CanBond(context, trader, Math.Round(terms.Bond.Amount, 2)))
        {
            return null; // it can't lock its whole bond (design §16.4), not even on the clearing house's credit
        }

        long arrival = context.CurrentTick + repoTicks + ladenTicks;
        if (arrival > offer.Deadline + terms.GraceHours)
        {
            return null; // it would be in default before it got there
        }

        // What the hold does to perishables on the laden leg, as the warranty judges it: against their quality when loaded
        // (fresh from stock, or as they will stand in storage when the ship comes).
        decimal quality = 1m;
        if (terms.Warranty is { } warranty)
        {
            decimal loaded = haul.PickupFrom.Kind == HoldingKind.Storage
                ? context.StorageOf(haul.Shipper, haul.Origin)?.Peek(haul.Good, context.CurrentTick)?.Decayed(repoTicks, 0m).Quality ?? 1m
                : 1m;
            decimal arrived = new Cargo(haul.Good, 1m, loaded).Decayed(ladenTicks, cls.Preservation).Quality;
            quality = loaded <= 0m ? 1m : arrived / loaded;
            if (warranty.Rejectable(quality))
            {
                return null; // the shipper would refuse the carriage
            }
        }

        decimal fee = offer.Price.Amount;
        long late = Math.Max(0, arrival - offer.Deadline);
        decimal lateFee = terms.LateFee?.PenaltyFor(late).Amount ?? 0m;
        decimal refund = fee * (terms.Warranty?.RefundFraction(quality) ?? 0m);
        decimal profit = fee - running - context.Config.DockingFee.Amount - lateFee - refund - TermsRisk(context, trader, haul.UsesEscrow, terms, fee, ship, laden, throttle);
        return new Take(Utility(trader, profit, laden.MaxDanger, repoTicks + ladenTicks), pilotMin, arrival, throttle);
    }

    /// <summary>
    /// What a ship filling a delivered wanted Sale from a source on <paramref name="offer"/> is worth to the trader, and the
    /// least price its costs allow: the goods, a carrier's margin on the trip, and what the terms will certainly take back; at the
    /// better throttle. Null if it can't on those terms at either: no room, no route, too dear for its purse, past the grace, or
    /// goods the buyer would reject.
    /// </summary>
    private static Take? FillTake(SimulationContext context, ActorState trader, Ship ship, SaleContract wanted, FillSource source, Offer offer) =>
        Better(
            FillTake(context, trader, ship, wanted, source, offer, Throttle.Cruise), FillTake(context, trader, ship, wanted, source, offer, Throttle.Push),
            offer.Deadline);

    private static Take? FillTake(SimulationContext context, ActorState trader, Ship ship, SaleContract wanted, FillSource source, Offer offer, Throttle throttle)
    {
        LocationId pickup = source.Kind == FillSourceKind.Aboard ? ship.Location : source.At;
        if (context.FindPath(pickup, wanted.Place) is not { } laden || context.FindPath(ship.Location, pickup) is not { } repo)
        {
            return null;
        }

        // Goods bought here need room in the hold; one's own goods aboard already have it.
        if (source.Kind != FillSourceKind.Aboard && wanted.Quantity > ship.FreeSpace)
        {
            return null;
        }

        ShipStats cls = ship.Stats;
        ContractTerms terms = TermsOn(wanted, offer);
        if (pickup == wanted.Place && throttle == Throttle.Push)
        {
            return null; // handed over where it is: nothing to push
        }

        long repoTicks = PriceModel.TravelTicks(repo.DistanceTicks, cls.Speed);
        long ladenTicks = pickup == wanted.Place ? 0 : Throttles.LadenTicks(context.Config, laden.DistanceTicks, cls, throttle);
        long arrival = context.CurrentTick + repoTicks + ladenTicks;
        if (arrival > offer.Deadline + terms.GraceHours)
        {
            return null; // it would be in default before the goods arrived
        }

        decimal purchase = source.PurchasePrice.Amount;
        decimal purchaseTax = purchase * Terms.TaxRate(context, context.Location(pickup).Reach);
        decimal bondNeeded = wanted.UsesEscrow ? Math.Round(terms.Bond.Amount, 2) : 0m;
        if (!Solvency.CanBond(context, trader, bondNeeded, purchase + purchaseTax))
        {
            return null; // the goods in cash, then its whole bond (design §16.4)
        }

        // The goods' quality now, decayed over the laden leg in this hold.
        decimal quality = new Cargo(wanted.Good, 1m, SourceQuality(context, trader, ship, wanted.Good, source)).Decayed(ladenTicks, cls.Preservation).Quality;
        if (terms.Warranty?.Rejectable(quality) == true)
        {
            return null; // the buyer would reject them
        }

        decimal price = offer.Price.Amount;
        decimal running = cls.RunningCostPerTick.Amount * (repoTicks + ladenTicks);
        decimal docking = ladenTicks > 0 ? context.Config.DockingFee.Amount : 0m;
        decimal transportMin = ladenTicks > 0
            ? PriceModel.PilotMinimumFee(cls, laden.DistanceTicks, laden.MaxDanger, trader.Personality).Amount + (cls.RunningCostPerTick.Amount * repoTicks)
            : cls.RunningCostPerTick.Amount * repoTicks;
        decimal refundShare = terms.Warranty?.RefundFraction(quality) ?? 0m;
        long late = Math.Max(0, arrival - offer.Deadline);
        decimal lateFee = terms.LateFee?.PenaltyFor(late).Amount ?? 0m;

        // The trader's floor covers the goods, a carrier's margin on the trip, and what the terms will certainly take back.
        decimal floor = ((purchase + purchaseTax + transportMin + docking + lateFee) / Math.Max(0.01m, 1m - refundShare)) + 1m;
        decimal risk = ladenTicks > 0 ? TermsRisk(context, trader, wanted.UsesEscrow, terms, price, ship, laden, throttle) : 0m;
        decimal profit = price - purchase - purchaseTax - running - docking - lateFee - (price * refundShare) - risk;
        return new Take(Utility(trader, profit, laden.MaxDanger, repoTicks + ladenTicks), floor, arrival, ladenTicks > 0 ? throttle : Throttle.None);
    }

    private static decimal SourceQuality(SimulationContext context, ActorState trader, Ship ship, GoodType good, FillSource source) => source.Kind switch
    {
        FillSourceKind.Aboard => ship.LotOf(trader.Id, good)?.At(context.CurrentTick, ship.Preservation).Cargo.Quality ?? 1m,
        FillSourceKind.Storage => context.StorageOf(trader.Id, source.At)?.Peek(good, context.CurrentTick)?.Quality ?? 1m,
        _ => 1m,
    };

    /// <summary>
    /// What a contract's terms stand to cost a carrier if an incident strikes on the laden trip (design §4.3's risk
    /// adjustment, and §3.4: stricter terms cost more): a total loss forfeits the price and the bond; a partial loss
    /// fails a strict contract outright and otherwise costs its share and any cargo liability; a delay can push it past
    /// a short grace. Weighted by the incident odds and by how much the trader minds risk.
    /// </summary>
    private static decimal TermsRisk(
        SimulationContext context, ActorState trader, bool usesEscrow, ContractTerms terms, decimal price, Ship ship, RoutePlan laden, Throttle throttle)
    {
        decimal odds = Throttles.IncidentOdds(context.Config, laden.MaxDanger, ship.Stats, throttle);
        decimal shift = context.Config.IncidentDangerShift;
        decimal bond = usesEscrow ? terms.Bond.Amount : 0m;
        decimal partial = terms.Partial == PartialDelivery.Failure
            ? price + bond
            : (price * 0.2m) + ((terms.Liability?.Cap.Amount ?? 0m) * 0.2m);
        decimal delay = terms.GraceHours < 18 ? bond * 0.5m : 0m;
        decimal expected = odds * ((IncidentResolver.Share(TransitIncidentKind.PartialLoss, laden.MaxDanger, shift) * partial)
                                   + (IncidentResolver.Share(TransitIncidentKind.TotalLoss, laden.MaxDanger, shift) * (price + bond))
                                   + (IncidentResolver.Share(TransitIncidentKind.MinorDelay, laden.MaxDanger, shift) * delay));
        return expected * (1.5m - trader.Personality.RiskTolerance);
    }

    // utility = expected credit change × risk adjustment × personality weight − opportunity cost (design §4.3).
    private static decimal Utility(ActorState trader, decimal profit, decimal danger, long hours)
    {
        decimal riskAdjustment = 1m - (danger * (1m - trader.Personality.RiskTolerance));
        decimal growthWeight = 0.7m + (0.6m * trader.Personality.GrowthAmbition);
        return (profit * riskAdjustment * growthWeight) - (hours * 0.08m);
    }

    // The commit threshold, set by liquidity preference: a cautious actor needs a clearly good deal (design §4.3).
    private static decimal Threshold(ActorState trader) => 4m * (0.5m + trader.Personality.LiquidityPreference);

    private static Signing Commit(SimulationContext context, ActorState trader, Ship ship, TraderOption option)
    {
        Offer offer = option.Offer;
        Signing signed = option.Kind == OptionKind.Haul
            ? Market.SignAgreed(option.Contract, offer, () => Market.TakeHaul(context, trader, (HaulContract)option.Contract, ship, offer.Price, option.Throttle))
            : Market.SignAgreed(option.Contract, offer, () => Market.FillWithShip(context, trader, (SaleContract)option.Contract, offer.Price, ship, option.Source!, option.Throttle));
        NoteHaggled(context, option.Contract, signed);
        return signed;
    }

    /// <summary>A free ship with the trader's own goods aboard and nothing better to do puts them into storage where it is.</summary>
    private static void Stow(SimulationContext context, ActorState trader, Ship ship)
    {
        foreach (Lot lot in ship.Hold.Where(l => l.Owner == trader.Id).ToList())
        {
            Cargo goods = context.Unload(ship, trader.Id, lot.Good, lot.Quantity);
            context.PutInStorage(trader, ship.Location, goods);
        }
    }

    /// <summary>
    /// A trader whose every ship is busy may still fill a delivered Sale: buy the goods into storage at the source (or
    /// use its own stored goods) and post a Haul for another carrier (design §4.1: "Hauls when overcommitted").
    /// </summary>
    private static void Broker(SimulationContext context, ActorState trader)
    {
        int brokered = context.LiveContractsOf(trader.Id).Count(c => c is SaleContract { Status: ContractStatus.Active } s && s.Seller == trader.Id);
        int allowed = trader.Role == ActorRole.ShippingCompany ? 2 : 1;
        if (brokered >= allowed)
        {
            return;
        }

        SaleContract? best = null;
        FillSource? bestSource = null;
        Offer? bestOffer = null;
        Credits bestFee = Credits.Zero;
        decimal bestBudget = 0m;
        decimal bestUtility = Threshold(trader);
        Sight sight = Visibility.Of(context, trader);
        foreach (SaleContract wanted in Known<SaleContract>(context, trader))
        {
            if (wanted.Side != SaleSide.Wanted)
            {
                continue;
            }

            foreach (FillSource source in FillSources(context, trader, wanted, ship: null, sight))
            {
                if (source.At == wanted.Place || context.FindPath(source.At, wanted.Place) is not { } laden
                    || context.StationOperatorAt(source.At) is not { } station)
                {
                    continue;
                }

                // A carrier has to find it and fly it: allow two days to come, then the trip at standard speed.
                if (context.CurrentTick + 48 + laden.DistanceTicks > wanted.DeadlineTick)
                {
                    continue;
                }

                Credits fee = HaulFeeEstimate(context, source.At, wanted.Place);
                decimal purchase = source.PurchasePrice.Amount;
                decimal purchaseTax = purchase * Terms.TaxRate(context, context.Location(source.At).Reach);
                decimal storage = station.Station!.StorageRatePerUnitDay.Amount * wanted.Quantity * 3m;
                decimal quality = new Cargo(wanted.Good, 1m, 1m).Decayed(48 + laden.DistanceTicks, 0m).Quality;
                if (wanted.Warranty?.Rejectable(quality) == true)
                {
                    continue; // a carrier's standard hold would deliver it spoiled
                }

                decimal refundShare = wanted.Warranty?.RefundFraction(quality) ?? 0m;
                decimal costs = purchase + purchaseTax + storage + (fee.Amount * 1.4m);

                // Its floor covers the goods, storage and a carrier's fee with room to raise it, before what the warranty takes back.
                decimal floor = (costs / Math.Max(0.01m, 1m - refundShare)) + 5m;
                Offer posted = Offer.Of(wanted);
                Take? TakeAt(decimal p) =>
                    new Take(Utility(trader, p - purchase - purchaseTax - storage - (p * refundShare) - (fee.Amount * 1.4m), laden.MaxDanger, laden.DistanceTicks / 2), floor, 0);
                if (Price(context, trader, wanted, posted, TakeAt) is not { } deal || !Market.CanFund(wanted, context.Actor(wanted.PostedBy), deal.Offer.Price))
                {
                    continue;
                }

                decimal price = deal.Offer.Price.Amount;
                decimal budget = price - purchase - purchaseTax - storage - (price * refundShare);
                decimal bond = wanted.UsesEscrow ? Math.Round(TermsOn(wanted, deal.Offer).Bond.Amount, 2) : 0m;
                bool affordable = Solvency.CanBond(context, trader, bond, purchase + purchaseTax + (fee.Amount * 3m) + 300m);
                if (affordable && HaulAffordable(fee, budget) && deal.Utility > bestUtility)
                {
                    best = wanted;
                    bestSource = source;
                    bestOffer = deal.Offer;
                    bestFee = fee;
                    bestBudget = budget;
                    bestUtility = deal.Utility;
                }
            }
        }

        if (best is null || bestSource is null || bestOffer is null)
        {
            return;
        }

        (Credits offer, Credits reservation) = HaulTerms(context, trader, best.Good, bestFee, bestBudget);
        HaulContract? haul = null;
        Signing signed = Market.SignAgreed(
            best, bestOffer, () => Market.FillViaHaul(context, trader, best, bestOffer.Price, bestSource, offer, reservation, out haul));
        NoteHaggled(context, best, signed);
        if (signed == Signing.Signed && haul is not null)
        {
            trader.Prices(new PriceKey(PriceKind.Haul, best.Good), UsualHaulMarkup).Posted(haul.Id);
        }
    }

    /// <summary>
    /// Goods an actor has had in storage for a while, with nothing planned for them, go up for sale where they are, and are written off
    /// if nobody takes them: a trader's leftovers, or a producer's or factory's goods set down far from home when a sale fell through
    /// (since S8: their rent keeps charging into debt, so nobody keeps them for ever).
    /// </summary>
    private static void SellOff(SimulationContext context, ActorState trader)
    {
        foreach (StorageContract storage in context.StoragesOf(trader.Id).ToList())
        {
            foreach (Lot lot in storage.Lots.ToList())
            {
                decimal free = storage.Available(lot.Good);
                bool posted = context.LiveContractsOf(trader.Id).Any(c => c is SaleContract { Side: SaleSide.ForSale } s && s.IsOpen && s.Good == lot.Good && s.Place == storage.Location);
                if (!posted && context.CurrentTick - lot.Since >= WriteOffAfterHours && free > 0m)
                {
                    // Nobody wanted them: written off, booked with the goods lost (design §20.2's ledger).
                    Cargo scrapped = context.TakeFromStorage(trader, storage.Location, lot.Good, free, reserved: false);
                    context.Lose(lot.Good, scrapped.Quantity);
                    continue;
                }

                if (free < 1m || posted || context.CurrentTick - lot.Since < SellOffAfterHours)
                {
                    continue;
                }

                decimal quality = storage.Peek(lot.Good, context.CurrentTick)?.Quality ?? 1m;
                decimal unit = Goods.Info(lot.Good).BaseValue * 0.7m * Math.Max(0.2m, quality);
                Credits price = Credits.Of(Math.Round(unit * free, 2));
                Market.PostSale(
                    context, trader, SaleSide.ForSale, lot.Good, free, storage.Location, price, Credits.Of(Math.Round(price.Amount * 0.7m, 2)),
                    context.CurrentTick + 96, Holding.StorageAt(storage.Location));
            }
        }
    }

    /// <summary>
    /// Station operator heartbeat (design §8.1, §5.1): collect the rent owed on every store at its location. What is above
    /// its float goes on station upkeep, a sink (<see cref="WorkingCapital"/>).
    /// </summary>
    public static void RunStation(SimulationContext context, ActorState station)
    {
        foreach (StorageContract storage in context.StoragesAt(station.Home))
        {
            context.SettleRent(storage);
        }
    }
}
