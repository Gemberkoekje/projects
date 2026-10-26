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

    /// <summary>One thing a free ship could do, scored: take a haul, or fill a delivered Sale from a source.</summary>
    private readonly record struct TraderOption(OptionKind Kind, Contract Contract, Credits Price, FillSource? Source, decimal Utility);

    /// <summary>
    /// Trader heartbeat (design §4.1): each free ship takes the best of the hauls and delivered Sales it can see; goods
    /// left aboard go into storage; a trader with every ship busy may still fill a Sale by having the goods hauled;
    /// goods that sit too long in storage are posted for sale.
    /// </summary>
    public static void RunTrader(SimulationContext context, ActorState trader)
    {
        List<Contract> visible = Visible<Contract>(context, trader).ToList();
        foreach (Ship ship in trader.Ships.Select(context.Ship).Where(s => s.IsFree).ToList())
        {
            if (BestOption(context, trader, ship, visible) is { } option)
            {
                Commit(context, trader, ship, option);
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
    }

    /// <summary>
    /// Where <paramref name="seller"/> could get the goods for a delivered wanted Sale: its own goods aboard
    /// <paramref name="ship"/> or in storage, or a producer with them free, at the producer's ask.
    /// </summary>
    public static IEnumerable<FillSource> FillSources(SimulationContext context, ActorState seller, SaleContract wanted, Ship? ship)
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
            if (producer.Id != seller.Id && !producer.IsPlayer && producer.Available(wanted.Good) >= wanted.Quantity
                && context.FindPath(producer.Home, wanted.Place) is not null)
            {
                Credits ask = Ask(producer);
                yield return FillSource.FromProducer(producer, Credits.Of(ask.Amount * wanted.Quantity));
            }
        }
    }

    private static TraderOption? BestOption(SimulationContext context, ActorState trader, Ship ship, List<Contract> visible)
    {
        TraderOption? best = null;
        foreach (HaulContract haul in visible.OfType<HaulContract>())
        {
            if (haul.IsOpen && ScoreHaul(context, trader, ship, haul) is { } option && (best is null || option.Utility > best.Value.Utility))
            {
                best = option;
            }
        }

        foreach (SaleContract wanted in visible.OfType<SaleContract>())
        {
            if (!wanted.IsOpen || wanted.Side != SaleSide.Wanted || wanted.Quantity > ship.CargoCapacity)
            {
                continue;
            }

            foreach (FillSource source in FillSources(context, trader, wanted, ship))
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
        if (haul.Quantity > ship.FreeSpace || context.FindPath(haul.Origin, haul.Destination) is not { } laden
            || context.FindPath(ship.Location, haul.Origin) is not { } repo)
        {
            return null;
        }

        ShipClassInfo cls = ship.Class;
        long repoTicks = PriceModel.TravelTicks(repo.DistanceTicks, cls.SpeedMultiplier);
        long ladenTicks = Math.Max(1, PriceModel.TravelTicks(laden.DistanceTicks, cls.SpeedMultiplier));
        decimal running = cls.RunningCostPerTick.Amount * (repoTicks + ladenTicks);
        Credits pilotMin = Credits.Of(PriceModel.PilotMinimumFee(cls, laden.DistanceTicks, laden.MaxDanger, trader.Personality).Amount
                                      + (cls.RunningCostPerTick.Amount * repoTicks));

        // Taken as posted (S5 brings haggling), if the fee meets the carrier's floor.
        Credits fee = haul.OfferedPrice;
        if (fee < pilotMin || !Market.CanFund(haul, context.Actor(haul.Shipper), fee))
        {
            return null;
        }

        long late = Math.Max(0, context.CurrentTick + repoTicks + ladenTicks - haul.DeadlineTick);
        decimal lateFee = Math.Min(haul.LatePenalty?.PenaltyFor(late).Amount ?? 0m, fee.Amount * Terms.HaulLateCap);
        decimal profit = fee.Amount - running - context.Config.DockingFee.Amount - lateFee;
        decimal utility = Utility(trader, profit, laden.MaxDanger, repoTicks + ladenTicks);
        return utility < Threshold(trader) ? null : new TraderOption(OptionKind.Haul, haul, fee, null, utility);
    }

    private static TraderOption? ScoreFill(SimulationContext context, ActorState trader, Ship ship, SaleContract wanted, FillSource source)
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

        ShipClassInfo cls = ship.Class;
        long repoTicks = PriceModel.TravelTicks(repo.DistanceTicks, cls.SpeedMultiplier);
        long ladenTicks = pickup == wanted.Place ? 0 : Math.Max(1, PriceModel.TravelTicks(laden.DistanceTicks, cls.SpeedMultiplier));
        long arrival = context.CurrentTick + repoTicks + ladenTicks;
        if (arrival > wanted.DefaultTick)
        {
            return null; // it would fail before the goods arrived
        }

        decimal purchase = source.PurchasePrice.Amount;
        decimal purchaseTax = purchase * Terms.TaxRate(context, context.Location(pickup).Reach);
        if (trader.Credits.Amount < purchase + purchaseTax)
        {
            return null;
        }

        // The goods' quality now, decayed over the laden leg in this hold.
        decimal quality = new Cargo(wanted.Good, 1m, SourceQuality(context, trader, ship, wanted.Good, source)).Decayed(ladenTicks, cls.Preservation).Quality;
        decimal running = cls.RunningCostPerTick.Amount * (repoTicks + ladenTicks);
        decimal docking = ladenTicks > 0 ? context.Config.DockingFee.Amount : 0m;
        decimal transportMin = ladenTicks > 0
            ? PriceModel.PilotMinimumFee(cls, laden.DistanceTicks, laden.MaxDanger, trader.Personality).Amount + (cls.RunningCostPerTick.Amount * repoTicks)
            : cls.RunningCostPerTick.Amount * repoTicks;
        decimal refundShare = wanted.Warranty?.RefundFraction(quality) ?? 0m;
        long late = Math.Max(0, arrival - wanted.DeadlineTick);
        decimal lateFee = wanted.LatePenalty?.PenaltyFor(late).Amount ?? 0m;

        // The trader's floor covers the goods, a carrier's margin on the trip, and what the terms will certainly take back.
        decimal floor = ((purchase + purchaseTax + transportMin + docking + lateFee) / Math.Max(0.01m, 1m - refundShare)) + 1m;
        Credits price = wanted.OfferedPrice;
        if (price.Amount < floor || !Market.CanFund(wanted, context.Actor(wanted.PostedBy), price))
        {
            return null; // taken as posted (S5 brings haggling), if the price meets the trader's floor
        }

        decimal profit = price.Amount - purchase - purchaseTax - running - docking - lateFee - (price.Amount * refundShare);
        decimal utility = Utility(trader, profit, laden.MaxDanger, repoTicks + ladenTicks);
        return utility < Threshold(trader) ? null : new TraderOption(OptionKind.Fill, wanted, price, source, utility);
    }

    private static decimal SourceQuality(SimulationContext context, ActorState trader, Ship ship, GoodType good, FillSource source) => source.Kind switch
    {
        FillSourceKind.Aboard => ship.LotOf(trader.Id, good)?.At(context.CurrentTick, ship.Preservation).Cargo.Quality ?? 1m,
        FillSourceKind.Storage => context.StorageOf(trader.Id, source.At)?.Peek(good, context.CurrentTick)?.Quality ?? 1m,
        _ => 1m,
    };

    // utility = expected credit change × risk adjustment × personality weight − opportunity cost (design §4.3).
    private static decimal Utility(ActorState trader, decimal profit, decimal danger, long hours)
    {
        decimal riskAdjustment = 1m - (danger * (1m - trader.Personality.RiskTolerance));
        decimal growthWeight = 0.7m + (0.6m * trader.Personality.GrowthAmbition);
        return (profit * riskAdjustment * growthWeight) - (hours * 0.08m);
    }

    // The commit threshold, set by liquidity preference: a cautious actor needs a clearly good deal (design §4.3).
    private static decimal Threshold(ActorState trader) => 4m * (0.5m + trader.Personality.LiquidityPreference);

    private static void Commit(SimulationContext context, ActorState trader, Ship ship, TraderOption option)
    {
        if (option.Kind == OptionKind.Haul)
        {
            Market.TakeHaul(context, trader, (HaulContract)option.Contract, ship, option.Price);
        }
        else
        {
            Market.FillWithShip(context, trader, (SaleContract)option.Contract, option.Price, ship, option.Source!);
        }
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
        Credits bestPrice = Credits.Zero;
        Credits bestFee = Credits.Zero;
        decimal bestBudget = 0m;
        decimal bestUtility = Threshold(trader);
        foreach (SaleContract wanted in Visible<SaleContract>(context, trader))
        {
            if (wanted.Side != SaleSide.Wanted)
            {
                continue;
            }

            foreach (FillSource source in FillSources(context, trader, wanted, ship: null))
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
                decimal refundShare = wanted.Warranty?.RefundFraction(quality) ?? 0m;
                decimal costs = purchase + purchaseTax + storage + (fee.Amount * 1.4m);
                decimal floor = (costs / Math.Max(0.01m, 1m - refundShare)) + 5m;
                Credits price = wanted.OfferedPrice;
                if (price.Amount < floor || !Market.CanFund(wanted, context.Actor(wanted.PostedBy), price))
                {
                    continue;
                }

                decimal budget = price.Amount - purchase - purchaseTax - storage - (price.Amount * refundShare);
                decimal utility = Utility(trader, budget - (fee.Amount * 1.4m), laden.MaxDanger, laden.DistanceTicks / 2);
                bool affordable = trader.Credits.Amount >= purchase + purchaseTax + (fee.Amount * 3m) + 300m;
                if (affordable && HaulAffordable(fee, budget) && utility > bestUtility)
                {
                    best = wanted;
                    bestSource = source;
                    bestPrice = price;
                    bestFee = fee;
                    bestBudget = budget;
                    bestUtility = utility;
                }
            }
        }

        if (best is null || bestSource is null)
        {
            return;
        }

        (Credits offer, Credits reservation) = HaulTerms(context, trader, best.Good, bestFee, bestBudget);
        if (Market.FillViaHaul(context, trader, best, bestPrice, bestSource, offer, reservation, out HaulContract? haul) == Signing.Signed && haul is not null)
        {
            trader.Prices(new PriceKey(PriceKind.Haul, best.Good), 1.6m).Posted(haul.Id);
        }
    }

    /// <summary>Goods a trader has had in storage for a while, with nothing planned for them, go up for sale where they are.</summary>
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

                decimal unit = Goods.Info(lot.Good).BaseValue * 0.7m * Math.Max(0.2m, storage.Peek(lot.Good, context.CurrentTick)?.Quality ?? 1m);
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
