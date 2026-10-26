using System;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>What the player would do to take a contract: carry a haul, fill a delivered Sale (with its ship, or through a carrier), or buy a lot.</summary>
public enum DealKind
{
    Haul,
    Fill,
    Buy,
    Broker,
}

/// <summary>
/// What a contract is worth to the player's ship, computed with the engine's own rules and formulas (design §11.2,
/// §11.5): what it pays, minus the costs the game knows for certain if nothing goes wrong (goods bought at the source
/// and their tax, running costs, docking, a warranty refund the trip's length makes certain, a late fee the arrival
/// time makes certain). The incident odds stay separate, as the uncertain input. It never collapses into one
/// "expected value". For a lot the player buys there is no income: the take-home is the cost, negative. Filled through
/// a carrier, the player's ship isn't used: the costs are the goods and the haul fee, each with its tax.
/// </summary>
public sealed record DealEstimate(
    DealKind Kind,
    Ship Ship,
    LocationId StartAt,
    long StartTick,
    LocationId Pickup,
    RoutePlan? Repositioning,
    long RepoTicks,
    RoutePlan? Laden,
    long LadenTicks,
    Credits Income,
    Credits Goods,
    Credits GoodsTax,
    Credits Running,
    Credits Docking,
    decimal ArrivalQuality,
    Credits QualityRefund,
    long TicksLate,
    Credits LateFee,
    Credits Bond,
    decimal IncidentOdds,
    FillSource? Source,
    Credits HaulFee = default,
    Credits HaulTax = default)
{
    public Credits TakeHome => Income - Goods - GoodsTax - Running - Docking - QualityRefund - LateFee - HaulFee - HaulTax;

    /// <summary>What it costs to buy: the goods, their tax and the trip to collect them.</summary>
    public Credits Cost => Goods + GoodsTax + Running;

    public long PickupTick => StartTick + RepoTicks;

    public long ArrivalTick => PickupTick + LadenTicks;

    /// <summary>How long it ties the ship up, from now: any wait until it is free, the empty leg and the laden one.</summary>
    public long TiedUpTicks(long now) => ArrivalTick - now;

    /// <summary>Take-home per game day of the ship's time.</summary>
    public Credits PerDay(long now)
    {
        long ticks = Math.Max(1, TiedUpTicks(now));
        return Credits.Of(TakeHome.Amount * GameTime.HoursPerDay / ticks);
    }

    public DangerBand Band => Laden?.Band ?? Repositioning?.Band ?? DangerBand.Green;

    /// <summary>The estimate for taking <paramref name="contract"/> with the player's ship, from where and when the ship is next free.</summary>
    public static DealEstimate? For(SimulationContext context, ActorState player, Ship ship, Contract contract) => contract switch
    {
        HaulContract haul => ForHaul(context, player, ship, haul),
        SaleContract { Side: SaleSide.Wanted } wanted => PlayerActions.CheckFill(context, player, wanted).Source is { } source
            ? ForFill(context, player, ship, wanted, source)
            : null,
        SaleContract { Side: SaleSide.ForSale } lot => ForLot(context, player, ship, lot),
        _ => null,
    };

    /// <summary>Carry a haul: the fee, less the trip's running costs and docking, and what the terms will certainly take back.</summary>
    public static DealEstimate? ForHaul(SimulationContext context, ActorState player, Ship ship, HaulContract haul)
    {
        (LocationId startAt, long startTick) = Market.NextFree(context, ship);
        if (context.FindPath(haul.Origin, haul.Destination) is not { } laden || Leg(context, ship, startAt, haul.Origin) is not { } repo)
        {
            return null;
        }

        long ladenTicks = Math.Max(1, PriceModel.TravelTicks(laden.DistanceTicks, ship.Class.SpeedMultiplier));
        Credits fee = haul.OfferedPrice;

        // The warranty judges what the hold does to the goods: their quality on arrival as a share of what it was when loaded.
        decimal loaded = haul.PickupFrom.Kind == HoldingKind.Storage
            ? context.StorageOf(haul.Shipper, haul.Origin)?.Peek(haul.Good, context.CurrentTick)?.Decayed(startTick + repo.Ticks - context.CurrentTick, 0m).Quality ?? 1m
            : 1m;
        decimal arrived = new Cargo(haul.Good, 1m, loaded).Decayed(ladenTicks, ship.Preservation).Quality;
        decimal quality = loaded <= 0m ? 1m : arrived / loaded;
        Credits refund = Credits.Of(fee.Amount * (haul.Warranty?.RefundFraction(quality) ?? 0m));
        Credits bond = haul.UsesEscrow ? Credits.Of(Math.Min(haul.PerformanceBond.Amount, Math.Max(0m, player.Credits.Amount))) : Credits.Zero;
        long arrival = startTick + repo.Ticks + ladenTicks;
        (long late, Credits lateFee) = Late(haul, arrival, fee - refund, bond, player.Credits);

        return new DealEstimate(
            DealKind.Haul, ship, startAt, startTick, haul.Origin, repo.Plan, repo.Ticks, laden, ladenTicks,
            fee, Credits.Zero, Credits.Zero, RunningFor(ship, repo.Ticks + ladenTicks), context.Config.DockingFee,
            quality, refund, late, lateFee, bond, Odds(context, ship, laden), null);
    }

    /// <summary>
    /// Fill a delivered Sale from a source: the price, less the goods bought there and their tax, the running costs of
    /// fetching and carrying them, docking, and what the terms will certainly take back. Goods already at the place are
    /// handed over at once.
    /// </summary>
    public static DealEstimate? ForFill(SimulationContext context, ActorState player, Ship ship, SaleContract wanted, FillSource source)
    {
        (LocationId startAt, long startTick) = Market.NextFree(context, ship);
        LocationId pickup = source.Kind == FillSourceKind.Aboard ? ship.Location : source.At;
        if (context.FindPath(pickup, wanted.Place) is not { } laden || Leg(context, ship, startAt, pickup) is not { } repo)
        {
            return null;
        }

        // At the place already, from storage or the hold: handed over on signing. Otherwise a trip, however short.
        bool onTheSpot = pickup == wanted.Place && source.Kind is FillSourceKind.Storage or FillSourceKind.Aboard;
        long ladenTicks = onTheSpot ? 0 : Math.Max(1, PriceModel.TravelTicks(laden.DistanceTicks, ship.Class.SpeedMultiplier));
        Credits goods = source.PurchasePrice;
        Credits goodsTax = Credits.Of(goods.Amount * Terms.TaxRate(context, context.Location(pickup).Reach));

        // Goods decay in storage until they are picked up, then in the hold; stock and fresh purchases start fresh.
        decimal quality = source.Kind switch
        {
            FillSourceKind.Aboard => ship.LotOf(player.Id, wanted.Good)?.At(context.CurrentTick, ship.Preservation).Cargo.Quality ?? 1m,
            FillSourceKind.Storage => context.StorageOf(player.Id, pickup)?.Peek(wanted.Good, context.CurrentTick)?.Decayed(startTick + repo.Ticks - context.CurrentTick, 0m).Quality ?? 1m,
            _ => 1m,
        };
        quality = new Cargo(wanted.Good, 1m, quality).Decayed(ladenTicks + (source.Kind == FillSourceKind.Aboard ? repo.Ticks : 0), ship.Preservation).Quality;

        Credits price = wanted.OfferedPrice;
        Credits refund = Credits.Of(price.Amount * (wanted.Warranty?.RefundFraction(quality) ?? 0m));
        Credits bond = wanted.UsesEscrow
            ? Credits.Of(Math.Min(wanted.PerformanceBond.Amount, Math.Max(0m, player.Credits.Amount - goods.Amount - goodsTax.Amount)))
            : Credits.Zero;
        long arrival = startTick + repo.Ticks + ladenTicks;
        (long late, Credits lateFee) = Late(wanted, arrival, price - refund, bond, player.Credits - goods - goodsTax);

        return new DealEstimate(
            DealKind.Fill, ship, startAt, startTick, pickup, repo.Plan, repo.Ticks, onTheSpot ? null : laden, ladenTicks,
            price, goods, goodsTax, RunningFor(ship, repo.Ticks + ladenTicks), onTheSpot ? Credits.Zero : context.Config.DockingFee,
            quality, refund, late, lateFee, bond, onTheSpot ? 0m : Odds(context, ship, laden), source);
    }

    /// <summary>Buy a lot and collect it: the price, its tax, and the running cost of getting there. Nothing to deliver.</summary>
    public static DealEstimate? ForLot(SimulationContext context, ActorState player, Ship ship, SaleContract lot)
    {
        (LocationId startAt, long startTick) = Market.NextFree(context, ship);
        if (Leg(context, ship, startAt, lot.Place) is not { } repo)
        {
            return null;
        }

        Credits tax = Credits.Of(lot.OfferedPrice.Amount * lot.TaxRate);
        return new DealEstimate(
            DealKind.Buy, ship, startAt, startTick, lot.Place, repo.Plan, repo.Ticks, null, 0,
            Credits.Zero, lot.OfferedPrice, tax, RunningFor(ship, repo.Ticks), Credits.Zero,
            1m, Credits.Zero, 0, Credits.Zero, Credits.Zero, 0m, null);
    }

    /// <summary>
    /// Fill a delivered Sale through a carrier, as the check found it: the price, less the goods bought into storage at the
    /// source and their tax, and the haul fee and its tax. When a carrier takes the haul, how late it is and what the
    /// goods are worth on arrival are not known yet: they stay with the uncertain inputs.
    /// </summary>
    public static DealEstimate? ForBroker(SimulationContext context, ActorState player, Ship ship, SaleContract wanted)
    {
        PlayerActions.BrokerFeasibility check = PlayerActions.CheckBroker(context, player, wanted);
        if (check.Source is not { } source || context.FindPath(source.At, wanted.Place) is not { } laden)
        {
            return null;
        }

        Credits goodsTax = Credits.Of(source.PurchasePrice.Amount * Terms.TaxRate(context, context.Location(source.At).Reach));
        Credits haulTax = Credits.Of(check.Fee.Amount * wanted.TaxRate);
        Credits bond = wanted.UsesEscrow ? Credits.Of(Math.Min(wanted.PerformanceBond.Amount, Math.Max(0m, player.Credits.Amount - source.PurchasePrice.Amount - goodsTax.Amount))) : Credits.Zero;
        return new DealEstimate(
            DealKind.Broker, ship, source.At, context.CurrentTick, source.At, null, 0, laden, laden.DistanceTicks,
            wanted.OfferedPrice, source.PurchasePrice, goodsTax, Credits.Zero, Credits.Zero,
            1m, Credits.Zero, 0, Credits.Zero, bond, Math.Clamp(context.Config.IncidentProbabilityScale * laden.MaxDanger, 0m, 1m), source,
            check.Fee, haulTax);
    }

    // The empty leg from where the ship will be free to a pickup, or none if it is there already.
    private static (RoutePlan? Plan, long Ticks)? Leg(SimulationContext context, Ship ship, LocationId from, LocationId to)
    {
        if (from == to)
        {
            return (null, 0);
        }

        return context.FindPath(from, to) is { } plan ? (plan, PriceModel.TravelTicks(plan.DistanceTicks, ship.Class.SpeedMultiplier)) : null;
    }

    private static Credits RunningFor(Ship ship, long hours) => Credits.Of(ship.Class.RunningCostPerTick.Amount * hours);

    // The engine's incident chance: one check on the laden trip, at its worst leg (TransitIncidentCheck).
    private static decimal Odds(SimulationContext context, Ship ship, RoutePlan laden) =>
        Math.Clamp(context.Config.IncidentProbabilityScale * laden.MaxDanger * ship.Class.IncidentChanceModifier, 0m, 1m);

    // A late fee the arrival makes certain, limited as Settlement limits it: by what the jurisdiction can collect.
    private static (long Late, Credits Fee) Late(Contract contract, long arrival, Credits due, Credits bond, Credits credits)
    {
        long late = Math.Max(0, arrival - contract.DeadlineTick);
        decimal fee = contract.LatePenalty?.PenaltyFor(late).Amount ?? 0m;
        return (late, Credits.Of(Math.Min(fee, Settlement.Collectable(contract, due, bond, credits - bond).Amount)));
    }
}
