using System;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Events;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>How an attempt to sign a contract ended. The interface words each one (design §11.5).</summary>
public enum Signing
{
    Signed,
    NotOpen,
    PayerCannotFund,
    SupplierDeclined,
}

/// <summary>Where a seller gets the goods to fill a Sale.</summary>
public enum FillSourceKind
{
    /// <summary>Bought from a producer at its location.</summary>
    Producer,

    /// <summary>The seller's own stock at home (a producer selling its output delivered).</summary>
    Stock,

    /// <summary>The seller's storage at a location.</summary>
    Storage,

    /// <summary>The hold of the seller's ship, where it is docked.</summary>
    Aboard,
}

/// <summary>A source of goods for filling a Sale; a producer source carries the price it asks for them.</summary>
public sealed record FillSource(FillSourceKind Kind, LocationId At, ActorId? Producer = null, Credits PurchasePrice = default)
{
    public static FillSource FromProducer(ActorState producer, Credits price) => new(FillSourceKind.Producer, producer.Home, producer.Id, price);

    public static FillSource FromStock(ActorState seller) => new(FillSourceKind.Stock, seller.Home);

    public static FillSource FromStorage(LocationId at) => new(FillSourceKind.Storage, at);

    public static FillSource FromHold(Ship ship) => new(FillSourceKind.Aboard, ship.Location);
}

/// <summary>
/// The market's commands: post, withdraw, sign, and the compound deals built from them (fill a delivered Sale,
/// buy a lot, take a haul). Every one is the same path for an AI decision and a player command (plan §2,
/// "symmetric by construction"): the decision engine and <see cref="PlayerActions"/> differ only in who
/// chooses. Signing locks escrow and bonds in Accord space (design §16.4); goods move only at handovers,
/// pickups and arrivals, through <see cref="Settlement"/>.
/// </summary>
public static class Market
{
    // ---- posting ----

    /// <summary>
    /// Post a Sale. For a wanted Sale <paramref name="holding"/> is where the buyer will take the goods at the place;
    /// for one for sale it is where the seller's goods are, and they are set aside. AI producers at the place answer
    /// a wanted Sale at once (design §4.3: an actor re-evaluates when a contract it can see appears).
    /// </summary>
    public static SaleContract PostSale(
        SimulationContext context, ActorState poster, SaleSide side, GoodType good, decimal quantity, LocationId place,
        Credits price, Credits reservation, long deadline, Holding holding)
    {
        Location location = context.Location(place);
        var sale = new SaleContract(
            context.NextContractId(), poster.Id, context.CurrentTick, side, good, quantity, place, price, reservation,
            Terms.TaxRate(context, location.Reach), deadline, location.Reach, location.Region, Terms.ForSale(good, price));
        if (side == SaleSide.Wanted)
        {
            sale.SetReceipt(holding);
        }
        else
        {
            Reserve(context, poster, holding, good, quantity);
            sale.SetReserved(holding);
        }

        context.Add(sale);
        context.Schedule(new ContractExpiryCheck(deadline, sale.Id));
        context.Record(new ContractPostedEntry(context.CurrentTick, sale.Id, poster.Id));
        if (side == SaleSide.Wanted)
        {
            DecisionEngine.AnswerAtOnce(context, sale);
        }

        return sale;
    }

    /// <summary>
    /// Post a Haul of the shipper's goods from where they wait to <paramref name="destination"/>. A haul that serves a
    /// Sale moves goods the Sale already set aside; any other haul sets its goods aside here.
    /// </summary>
    public static HaulContract PostHaul(
        SimulationContext context, ActorState shipper, GoodType good, decimal quantity, Holding pickupFrom, LocationId destination,
        Credits fee, Credits reservation, long deadline, ContractId? serves)
    {
        Location to = context.Location(destination);
        var haul = new HaulContract(
            context.NextContractId(), shipper.Id, context.CurrentTick, good, quantity, pickupFrom.Location, destination, pickupFrom, serves,
            fee, reservation, Terms.TaxRate(context, to.Reach), deadline, to.Reach, to.Region, Terms.ForHaul(good, fee));
        if (serves is null)
        {
            Reserve(context, shipper, pickupFrom, good, quantity);
        }

        context.Add(haul);
        context.Schedule(new ContractExpiryCheck(deadline, haul.Id));
        context.Record(new ContractPostedEntry(context.CurrentTick, haul.Id, shipper.Id));
        return haul;
    }

    /// <summary>The poster takes an open contract down; anything it set aside for it is free again.</summary>
    public static void Withdraw(SimulationContext context, Contract contract)
    {
        if (!contract.IsOpen)
        {
            return;
        }

        ReleaseOpen(context, contract);
        contract.MarkWithdrawn();
        context.Record(new ContractWithdrawnEntry(context.CurrentTick, contract.Id));
    }

    /// <summary>Nobody took an open contract before its deadline.</summary>
    internal static void Expire(SimulationContext context, Contract contract)
    {
        if (!contract.IsOpen)
        {
            return;
        }

        ReleaseOpen(context, contract);
        contract.MarkExpired();
        context.Record(new ContractExpiredEntry(context.CurrentTick, contract.Id));
    }

    // ---- signing ----

    /// <summary>Whether the payer could lock <paramref name="price"/> and its tax in escrow. Outside the Accord nothing is locked.</summary>
    public static bool CanFund(Contract contract, ActorState payer, Credits price) =>
        !contract.UsesEscrow || payer.Credits.Amount >= price.Amount * (1m + contract.TaxRate);

    /// <summary>Whether a shipper could fund a haul to <paramref name="destination"/> at <paramref name="fee"/>: its escrow in the Accord, or paying at handover outside it.</summary>
    public static bool CanFundHaul(SimulationContext context, ActorState shipper, LocationId origin, LocationId destination, Credits fee) =>
        context.FindPath(origin, destination) is not null
        && shipper.Credits.Amount >= fee.Amount * (1m + Terms.TaxRate(context, context.Location(destination).Reach));

    /// <summary>
    /// The seller signs a wanted Sale. <paramref name="reserveFrom"/> is where its goods are set aside (its stock or
    /// storage), or null when they will come aboard a ship. Set aside at the place, with the buyer taking them into
    /// its stock or storage, they are handed over at once.
    /// </summary>
    public static Signing Supply(SimulationContext context, ActorState seller, SaleContract wanted, Credits price, Holding? reserveFrom)
    {
        if (wanted.Side != SaleSide.Wanted)
        {
            return Signing.NotOpen;
        }

        Signing signed = Sign(context, wanted, seller, price);
        if (signed != Signing.Signed)
        {
            return signed;
        }

        if (reserveFrom is { } from)
        {
            Reserve(context, seller, from, wanted.Good, wanted.Quantity);
            wanted.SetReserved(from);
        }

        if (wanted.Reserved is { } r && r.Location == wanted.Place && wanted.Receipt is { Kind: HoldingKind.Stock or HoldingKind.Storage })
        {
            Settlement.HandOverReserved(context, wanted);
        }
        else
        {
            context.Schedule(new ContractDefaultCheck(wanted.DefaultTick, wanted.Id));
        }

        return Signing.Signed;
    }

    /// <summary>
    /// The buyer takes a for-sale lot: into its stock or storage at the place at once, or into one of its ships,
    /// which goes to collect it.
    /// </summary>
    public static Signing BuyLot(SimulationContext context, ActorState buyer, SaleContract lot, Credits price, Holding receipt)
    {
        if (lot.Side != SaleSide.ForSale)
        {
            return Signing.NotOpen;
        }

        Signing signed = Sign(context, lot, buyer, price);
        if (signed != Signing.Signed)
        {
            return signed;
        }

        lot.SetReceipt(receipt);
        if (receipt.Kind == HoldingKind.Ship)
        {
            Ship ship = context.Ship(receipt.Ship!.Value);
            Dispatch(context, ship, new ShipJob(buyer.Id, lot.Good, lot.Quantity, lot.Place, PickupSource.Purchase, Destination: null, Purchase: lot.Id));
            context.Schedule(new ContractDefaultCheck(lot.DefaultTick, lot.Id));
        }
        else
        {
            Settlement.HandOverReserved(context, lot);
        }

        return Signing.Signed;
    }

    /// <summary>The carrier takes a haul: it signs, and its ship goes to the pickup.</summary>
    public static Signing TakeHaul(SimulationContext context, ActorState carrier, HaulContract haul, Ship ship, Credits fee)
    {
        Signing signed = Sign(context, haul, carrier, fee);
        if (signed != Signing.Signed)
        {
            return signed;
        }

        haul.Assign(ship.Id);
        Dispatch(context, ship, new ShipJob(haul.Shipper, haul.Good, haul.Quantity, haul.Origin, PickupSource.Haul, haul.Destination, Haul: haul.Id));
        return Signing.Signed;
    }

    /// <summary>
    /// Buy goods at a producer's location by posting a wanted Sale there at <paramref name="price"/>, which an AI
    /// producer there answers at once. Null, with the post withdrawn, if none did (or, outside the Accord, if the
    /// buyer then couldn't pay).
    /// </summary>
    public static SaleContract? BuyAtSource(
        SimulationContext context, ActorState buyer, GoodType good, decimal quantity, LocationId source, Credits price, Holding receipt, long deadline)
    {
        SaleContract post = PostSale(context, buyer, SaleSide.Wanted, good, quantity, source, price, price, deadline, receipt);
        if (post.Status is ContractStatus.Active or ContractStatus.Fulfilled)
        {
            return post;
        }

        Withdraw(context, post);
        return null;
    }

    /// <summary>
    /// Fill a delivered wanted Sale with one's own ship: buy the goods at a producer (or take them from one's
    /// storage or hold), carry them to the place, and hand them over there. Goods already at the place are handed
    /// over at once.
    /// </summary>
    public static Signing FillWithShip(SimulationContext context, ActorState seller, SaleContract wanted, Credits price, Ship ship, FillSource source)
    {
        if (!wanted.IsOpen || wanted.Side != SaleSide.Wanted)
        {
            return Signing.NotOpen;
        }

        if (!CanFund(wanted, context.Actor(wanted.PostedBy), price))
        {
            return Signing.PayerCannotFund;
        }

        LocationId pickup = source.Kind == FillSourceKind.Aboard ? ship.Location : source.At;
        if (pickup == wanted.Place && source.Kind == FillSourceKind.Storage)
        {
            return Supply(context, seller, wanted, price, Holding.StorageAt(pickup));
        }

        if (pickup == wanted.Place && source.Kind == FillSourceKind.Aboard)
        {
            Signing signed = Sign(context, wanted, seller, price);
            if (signed == Signing.Signed)
            {
                Cargo goods = context.Unload(ship, seller.Id, wanted.Good, wanted.Quantity);
                if (!Settlement.HandOverCargo(context, wanted, goods))
                {
                    context.Load(ship, seller.Id, goods); // a default: the seller keeps its goods
                }
            }

            return signed;
        }

        SaleContract? purchase = null;
        if (source.Kind == FillSourceKind.Producer)
        {
            long collect = context.CurrentTick + TravelTo(context, ship, pickup) + 24;
            purchase = BuyAtSource(context, seller, wanted.Good, wanted.Quantity, pickup, source.PurchasePrice, Holding.Aboard(ship.Id, pickup), collect);
            if (purchase is null)
            {
                return Signing.SupplierDeclined;
            }
        }

        Signing supplied = Supply(context, seller, wanted, price, source.Kind == FillSourceKind.Storage ? Holding.StorageAt(pickup) : null);
        if (supplied != Signing.Signed)
        {
            // Funding was checked above, so this shouldn't happen; if it does, the ship still collects what was bought.
            if (purchase is not null)
            {
                Dispatch(context, ship, new ShipJob(seller.Id, wanted.Good, wanted.Quantity, pickup, PickupSource.Purchase, null, Purchase: purchase.Id));
            }

            return supplied;
        }

        PickupSource how = source.Kind switch
        {
            FillSourceKind.Producer => PickupSource.Purchase,
            FillSourceKind.Storage => PickupSource.Storage,
            _ => PickupSource.Aboard,
        };
        Dispatch(context, ship, new ShipJob(seller.Id, wanted.Good, wanted.Quantity, pickup, how, wanted.Place, Purchase: purchase?.Id, Delivery: wanted.Id));
        return Signing.Signed;
    }

    /// <summary>
    /// Fill a delivered wanted Sale by having the goods hauled: buy them into storage at a producer's location (or use
    /// one's stock or storage there) and post a Haul to the place, which hands them over when it arrives. Goods
    /// already at the place are handed over at once.
    /// </summary>
    public static Signing FillViaHaul(
        SimulationContext context, ActorState seller, SaleContract wanted, Credits price, FillSource source, Credits fee, Credits feeReservation) =>
        FillViaHaul(context, seller, wanted, price, source, fee, feeReservation, out _);

    /// <inheritdoc cref="FillViaHaul(SimulationContext, ActorState, SaleContract, Credits, FillSource, Credits, Credits)"/>
    public static Signing FillViaHaul(
        SimulationContext context, ActorState seller, SaleContract wanted, Credits price, FillSource source, Credits fee, Credits feeReservation,
        out HaulContract? haul)
    {
        haul = null;
        if (!wanted.IsOpen || wanted.Side != SaleSide.Wanted)
        {
            return Signing.NotOpen;
        }

        if (!CanFund(wanted, context.Actor(wanted.PostedBy), price))
        {
            return Signing.PayerCannotFund;
        }

        Holding from;
        switch (source.Kind)
        {
            case FillSourceKind.Producer:
                if (BuyAtSource(context, seller, wanted.Good, wanted.Quantity, source.At, source.PurchasePrice, Holding.StorageAt(source.At), context.CurrentTick + 24)
                    is not { Status: ContractStatus.Fulfilled })
                {
                    return Signing.SupplierDeclined;
                }

                from = Holding.StorageAt(source.At);
                break;
            case FillSourceKind.Stock:
                from = Holding.StockAt(seller.Home);
                break;
            case FillSourceKind.Storage:
                from = Holding.StorageAt(source.At);
                break;
            default:
                throw new ArgumentException("Goods aboard a ship are carried by that ship, not hauled.", nameof(source));
        }

        Signing signed = Supply(context, seller, wanted, price, from);
        if (signed == Signing.Signed && wanted.Status == ContractStatus.Active && from.Location != wanted.Place)
        {
            haul = PostHaul(context, seller, wanted.Good, wanted.Quantity, from, wanted.Place, fee, feeReservation, wanted.DeadlineTick, wanted.Id);
        }

        return signed;
    }

    /// <summary>Whether a signed contract is being performed: a ship's job serves it, or a signed haul delivers for it.</summary>
    public static bool UnderWay(SimulationContext context, Contract contract)
    {
        foreach (Ship ship in context.Ships.Values)
        {
            if (ship.Job is { } job && job.Serves(contract.Id))
            {
                return true;
            }
        }

        if (contract is SaleContract { Seller: { } seller } sale)
        {
            return context.LiveContractsOf(seller).Any(c => c is HaulContract { Status: ContractStatus.Active } haul && haul.Serves == sale.Id);
        }

        return false;
    }

    /// <summary>Where and when a ship is next free: now where it sits, or where and when its job ends.</summary>
    public static (LocationId At, long Tick) NextFree(SimulationContext context, Ship ship)
    {
        long now = context.CurrentTick;
        if (ship.Job is not { } job)
        {
            return (ship.Location, now);
        }

        LocationId end = job.Destination ?? job.Pickup;
        if (ship.Course is { } laden && ship.Location == job.Pickup)
        {
            return (end, laden.ArrivalTick); // loaded and on its way
        }

        // Not loaded yet: any empty leg, then the laden one.
        long pickup = ship.Course?.ArrivalTick ?? now;
        long ladenTicks = 0;
        if (job.Destination is { } destination && context.FindPath(job.Pickup, destination) is { } plan)
        {
            ladenTicks = Math.Max(1, PriceModel.TravelTicks(plan.DistanceTicks, ship.Class.SpeedMultiplier));
        }

        return (end, pickup + ladenTicks);
    }

    /// <summary>The hours a ship needs to reach <paramref name="to"/> from where it is.</summary>
    public static long TravelTo(SimulationContext context, Ship ship, LocationId to)
    {
        if (ship.Location == to)
        {
            return 0;
        }

        RoutePlan plan = context.FindPath(ship.Location, to)
                         ?? throw new InvalidOperationException($"No route from {ship.Location} to {to}.");
        return PriceModel.TravelTicks(plan.DistanceTicks, ship.Class.SpeedMultiplier);
    }

    /// <summary>Commit a ship to a job: any empty leg to the pickup starts now, and it loads when it gets there.</summary>
    internal static void Dispatch(SimulationContext context, Ship ship, ShipJob job)
    {
        ShipCourse? reposition = null;
        long ticks = 0;
        if (ship.Location != job.Pickup)
        {
            RoutePlan plan = context.FindPath(ship.Location, job.Pickup)
                             ?? throw new InvalidOperationException("A dispatched ship has no route to its pickup.");
            ticks = PriceModel.TravelTicks(plan.DistanceTicks, ship.Class.SpeedMultiplier);
            reposition = ShipCourse.Along(plan, context.CurrentTick, ticks);
        }

        ship.Reserve(context.CurrentTick, job, reposition);
        context.Schedule(new ShipDeparted(context.CurrentTick + ticks, ship.Id, job));
    }

    // Sign, then lock the payer's price and tax and the performer's bond in Accord escrow (design §16.4).
    private static Signing Sign(SimulationContext context, Contract contract, ActorState taker, Credits price)
    {
        if (!contract.IsOpen || contract.PostedBy == taker.Id)
        {
            return Signing.NotOpen;
        }

        ActorState payer = contract switch
        {
            SaleContract { Side: SaleSide.ForSale } => taker,
            _ => context.Actor(contract.PostedBy),
        };
        if (!CanFund(contract, payer, price))
        {
            return Signing.PayerCannotFund;
        }

        context.Sign(contract, taker, price);
        if (contract.UsesEscrow)
        {
            ActorState performer = context.Actor(contract.Performer!.Value);
            context.LockToEscrow(contract, payer, price + contract.Tax);
            Credits bond = Credits.Of(Math.Min(contract.PerformanceBond.Amount, Math.Max(0m, performer.Credits.Amount)));
            context.LockToEscrow(contract, performer, bond);
            contract.SetLockedBond(bond);
        }

        context.Record(new ContractSignedEntry(context.CurrentTick, contract.Id, taker.Id, price));
        return Signing.Signed;
    }

    private static void Reserve(SimulationContext context, ActorState owner, Holding holding, GoodType good, decimal quantity)
    {
        if (holding.Kind == HoldingKind.Stock)
        {
            context.ReserveStock(owner, good, quantity);
        }
        else if (holding.Kind == HoldingKind.Storage)
        {
            StorageContract storage = context.StorageOf(owner.Id, holding.Location)
                                      ?? throw new InvalidOperationException($"{owner.Name} has no storage at {holding.Location}.");
            context.ReserveStorage(storage, good, quantity);
        }
    }

    // An open post's set-aside goods go back to being free: a lot's, or a haul's that serves no Sale.
    private static void ReleaseOpen(SimulationContext context, Contract contract)
    {
        ActorState poster = context.Actor(contract.PostedBy);
        switch (contract)
        {
            case SaleContract { Side: SaleSide.ForSale } lot:
                Settlement.Unreserve(context, poster, lot.Reserved, lot.Good, lot.Quantity);
                lot.SetReserved(null);
                break;
            case HaulContract { Serves: null } haul:
                Settlement.Unreserve(context, poster, haul.PickupFrom, haul.Good, haul.Quantity);
                break;
        }
    }
}
