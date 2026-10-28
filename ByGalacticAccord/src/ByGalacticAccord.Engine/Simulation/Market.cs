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

    /// <summary>An offer was accepted, but the side that would perform it could no longer do what it offered.</summary>
    CannotPerform,
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
/// The market's commands: post, withdraw, sign, cancel, and the compound deals built from them (fill a delivered
/// Sale, buy a lot, take a haul). Every one is the same path for an AI decision and a player command (plan §2,
/// "symmetric by construction"): the decision engine and <see cref="PlayerActions"/> differ only in who
/// chooses. Signing locks escrow and bonds in Accord space (design §16.4) and, for a contract someone waits on,
/// schedules the end of its grace; goods move only at handovers, pickups and arrivals, through <see cref="Settlement"/>.
/// </summary>
public static partial class Market
{
    // ---- posting ----

    /// <summary>
    /// Post a Sale. For a wanted Sale <paramref name="holding"/> is where the buyer will take the goods at the place;
    /// for one for sale it is where the seller's goods are, and they are set aside. The terms are the poster's package
    /// (its habit, unless <paramref name="package"/> says otherwise), with its late fee scaled by
    /// <paramref name="lateFeeScale"/>; a lot waiting in storage is warranted as seen. AI producers at the place answer
    /// an AI's wanted Sale at once (design §4.3: an actor re-evaluates when a contract it can see appears); the player's
    /// posts reach the actors that can see them over game time, and their answers follow (<see cref="DecisionEngine.NoticePost"/>).
    /// </summary>
    public static SaleContract PostSale(
        SimulationContext context, ActorState poster, SaleSide side, GoodType good, decimal quantity, LocationId place,
        Credits price, Credits reservation, long deadline, Holding holding, TermPackage? package = null, decimal lateFeeScale = 1m) =>
        PostSale(context, poster, side, good, quantity, place, price, reservation, deadline, holding, package, lateFeeScale, atTheDoor: false);

    // A wanted Sale posted at the door (atTheDoor) is an order at a producer's asking price there, answered at once whoever posts it.
    private static SaleContract PostSale(
        SimulationContext context, ActorState poster, SaleSide side, GoodType good, decimal quantity, LocationId place,
        Credits price, Credits reservation, long deadline, Holding holding, TermPackage? package, decimal lateFeeScale, bool atTheDoor)
    {
        Location location = context.Location(place);
        decimal? stored = side == SaleSide.ForSale && holding.Kind == HoldingKind.Storage
            ? context.StorageOf(poster.Id, holding.Location)?.Peek(good, context.CurrentTick)?.Quality
            : null;
        ContractTerms terms = Terms.ForSale(package ?? Terms.PackageFor(poster), good, price, stored, deadline - context.CurrentTick, lateFeeScale);
        var sale = new SaleContract(
            context.NextContractId(), poster.Id, context.CurrentTick, side, good, quantity, place, price, reservation,
            Terms.TaxRate(context, location.Reach), deadline, location.Reach, location.Region, terms);
        sale.SetSeen(stored);
        sale.AtTheDoor = atTheDoor;
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
        if (side == SaleSide.Wanted && (atTheDoor || !HagglesInGameTime(poster)))
        {
            DecisionEngine.AnswerAtOnce(context, sale);
        }
        else if (HagglesInGameTime(poster))
        {
            DecisionEngine.NoticePost(context, sale);
        }

        return sale;
    }

    /// <summary>
    /// Whether an actor's offers go back and forth over game time (design §3.6). AI actors haggle among themselves in
    /// closed form, at once; the player, a human, reads replies as they come and answers them.
    /// </summary>
    public static bool HagglesInGameTime(ActorState actor) => actor.IsPlayer;

    /// <summary>
    /// Post a Haul of the shipper's goods from where they wait to <paramref name="destination"/>, on the shipper's package
    /// unless <paramref name="package"/> says otherwise. The goods are declared at the unit price of the Sale the haul
    /// serves, or else at their base value, for the carrier's liability. A haul that serves a Sale moves goods the Sale
    /// already set aside, and its posting follows from that Sale; any other haul sets its goods aside here.
    /// </summary>
    public static HaulContract PostHaul(
        SimulationContext context, ActorState shipper, GoodType good, decimal quantity, Holding pickupFrom, LocationId destination,
        Credits fee, Credits reservation, long deadline, ContractId? serves, TermPackage? package = null, long? cause = null, decimal lateFeeScale = 1m)
    {
        Location to = context.Location(destination);
        SaleContract? served = serves is { } id ? context.Contract(id) as SaleContract : null;
        Credits unitValue = served?.UnitPrice ?? Credits.Of(Goods.Info(good).BaseValue);
        var haul = new HaulContract(
            context.NextContractId(), shipper.Id, context.CurrentTick, good, quantity, pickupFrom.Location, destination, pickupFrom, serves,
            fee, reservation, Terms.TaxRate(context, to.Reach), deadline, to.Reach, to.Region,
            Terms.ForHaul(package ?? Terms.PackageFor(shipper), good, fee, quantity, unitValue, lateFeeScale));
        haul.Declare(unitValue);
        if (serves is null)
        {
            Reserve(context, shipper, pickupFrom, good, quantity);
        }

        context.Add(haul);
        context.Schedule(new ContractExpiryCheck(deadline, haul.Id));
        context.Record(new ContractPostedEntry(context.CurrentTick, haul.Id, shipper.Id) { Cause = cause ?? served?.LastEntry });
        if (HagglesInGameTime(shipper))
        {
            DecisionEngine.NoticePost(context, haul);
        }

        return haul;
    }

    /// <summary>The poster takes an open contract down; anything it set aside for it is free again.</summary>
    public static void Withdraw(SimulationContext context, Contract contract, long? cause = null)
    {
        if (!contract.IsOpen)
        {
            return;
        }

        ReleaseOpen(context, contract);
        contract.MarkWithdrawn();
        context.CloseNegotiationsOn(contract);
        context.Record(new ContractWithdrawnEntry(context.CurrentTick, contract.Id) { Cause = cause });
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
        context.CloseNegotiationsOn(contract);
        context.NoteFailure(FailureKind.Expired);
        context.Record(new ContractExpiredEntry(context.CurrentTick, contract.Id));
    }

    // ---- cancelling (design §3.4) ----

    /// <summary>
    /// The payer cancels a contract in default: its grace has passed and it is not performed. Its escrow comes back and
    /// the performer's bond is its. False if it may not.
    /// </summary>
    public static bool CancelForDefault(SimulationContext context, Contract contract, ActorState payer)
    {
        if (contract.Payer != payer.Id || !contract.InDefault(context.CurrentTick))
        {
            return false;
        }

        Settlement.Cancel(context, contract, payer, CancelReason.ForDefault);
        return true;
    }

    /// <summary>Either party walks away from a signed contract and pays the break fee. False if it may not.</summary>
    public static bool CancelAtWill(SimulationContext context, Contract contract, ActorState party)
    {
        if (contract.Status != ContractStatus.Active || contract is StorageContract || (contract.Payer != party.Id && contract.Performer != party.Id))
        {
            return false;
        }

        Settlement.Cancel(context, contract, party, CancelReason.AtWill);
        return true;
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
        else if (!wanted.Collected)
        {
            AwaitPerformance(context, wanted);
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
            // The goods wait, set aside, for the buyer's ship: nobody waits on the seller, so there is no grace to run out.
            Ship ship = context.Ship(receipt.Ship!.Value);
            Dispatch(context, ship, new ShipJob(buyer.Id, lot.Good, lot.Quantity, lot.Place, PickupSource.Purchase, Destination: null, Purchase: lot.Id));
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
        AwaitPerformance(context, haul);
        return Signing.Signed;
    }

    /// <summary>
    /// How far above the price it expects a buyer goes in an order at a producer's door: its knowledge of the ask may be a day
    /// old or more (design §7.2), and it pays the ask as it stands, up to this much more.
    /// </summary>
    public const decimal DoorTolerance = 0.10m;

    /// <summary>The most an order at the door pays for goods the buyer expects to cost <paramref name="expected"/>.</summary>
    public static Credits DoorLimit(Credits expected) => Credits.Of(Math.Round(expected.Amount * (1m + DoorTolerance), 2));

    /// <summary>
    /// Buy goods at a producer's location with an order at the door: a wanted Sale there that an AI producer answers at once, for
    /// the player as for anyone, at its ask as it stands, so long as that is within <see cref="DoorTolerance"/> of what the
    /// buyer expects to pay (<paramref name="expected"/>, from what it knows). Null, with the post withdrawn, if none did (or,
    /// outside the Accord, if the buyer then couldn't pay).
    /// </summary>
    public static SaleContract? BuyAtSource(
        SimulationContext context, ActorState buyer, GoodType good, decimal quantity, LocationId source, Credits expected, Holding receipt, long deadline)
    {
        Credits limit = DoorLimit(expected);
        SaleContract post = PostSale(context, buyer, SaleSide.Wanted, good, quantity, source, limit, limit, deadline, receipt, null, 1m, atTheDoor: true);
        bool bought = post.Status is ContractStatus.Active or ContractStatus.Fulfilled;
        context.NoteDoorOrder(refused: !bought, forStock: !bought && !context.ProducersOf(good).Any(p => p.Home == source && p.Available(good) >= quantity));
        if (bought)
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
            return NotOpen(context, seller, wanted);
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
                if (Settlement.HandOverCargo(context, wanted, goods) is HandOver.Defaulted or HandOver.Refused)
                {
                    context.Load(ship, seller.Id, goods); // the seller keeps its goods
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
                return Declined(context, seller, source);
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
            return NotOpen(context, seller, wanted);
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
                    return Declined(context, seller, source);
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

    /// <summary>
    /// Whether a signed contract is being performed: a ship's job serves it, or carries goods for a haul that delivers for it (a
    /// haul its shipper cancelled on the way still sets the goods down, and they go on to the Sale), or a signed haul delivers
    /// for it.
    /// </summary>
    public static bool UnderWay(SimulationContext context, Contract contract)
    {
        foreach (Ship ship in context.Ships.Values)
        {
            if (ship.Job is { } job && (job.Serves(contract.Id) || Delivers(context, job, contract)))
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

    /// <summary>When the goods for a signed contract are due to arrive, if anything is bringing them: a ship's job, or a haul's.</summary>
    public static long? ExpectedArrival(SimulationContext context, Contract contract)
    {
        foreach (Ship ship in context.Ships.Values)
        {
            if (ship.Job is { } job && (job.Serves(contract.Id) || Delivers(context, job, contract)))
            {
                return NextFree(context, ship).Tick;
            }
        }

        if (contract is SaleContract { Seller: { } seller } sale)
        {
            foreach (Contract c in context.LiveContractsOf(seller))
            {
                if (c is HaulContract { Status: ContractStatus.Active, AssignedShip: { } id } haul && haul.Serves == sale.Id)
                {
                    return NextFree(context, context.Ship(id)).Tick;
                }
            }
        }

        return null;
    }

    // A ship's job carries a haul's goods, loaded, toward the place of the Sale that haul serves.
    private static bool Delivers(SimulationContext context, ShipJob job, Contract contract) =>
        job.Haul is { } id && context.Contract(id) is HaulContract { PickedUp: true } haul && haul.Serves == contract.Id;

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

    // Someone now waits on the performer: when its grace runs out, the payer may cancel for default.
    private static void AwaitPerformance(SimulationContext context, Contract contract)
    {
        if (contract.GraceEndsTick != long.MaxValue)
        {
            context.Schedule(new GraceEnded(contract.GraceEndsTick, contract.Id));
        }
    }

    // A taker found a contract closed that it believed open (it knew of it from the news): it won't count on it again.
    private static Signing NotOpen(SimulationContext context, ActorState taker, Contract contract)
    {
        if (context.News.WasReportedOpen(contract.Id) && !Visibility.Of(context, taker).CanSee(contract))
        {
            context.NoteFoundClosedFromNews();
        }

        Knowledge.NoteClosed(context, taker, contract);
        return Signing.NotOpen;
    }

    // A producer turned down an order at its door: the buyer stops counting on it for a while.
    private static Signing Declined(SimulationContext context, ActorState buyer, FillSource source)
    {
        if (source.Producer is { } producer)
        {
            Knowledge.NoteRefused(context, buyer, producer);
        }

        return Signing.SupplierDeclined;
    }

    // Sign, then lock the payer's price and tax and the performer's bond in Accord escrow (design §16.4).
    private static Signing Sign(SimulationContext context, Contract contract, ActorState taker, Credits price)
    {
        if (contract.PostedBy == taker.Id)
        {
            return Signing.NotOpen;
        }

        if (!contract.IsOpen)
        {
            return NotOpen(context, taker, contract);
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

        if (context.News.WasReportedOpen(contract.Id) && !Visibility.Of(context, taker).CanSee(contract))
        {
            context.NoteSignedFromNews();
        }

        if (contract.Advertised && Advertising.SeenOnlyThroughAd(context, contract, Visibility.Of(context, taker)))
        {
            context.NoteSignedViaAd();
        }

        context.Sign(contract, taker, price);
        if (contract.UsesEscrow)
        {
            ActorState performer = context.Actor(contract.Performer!.Value);
            context.LockToEscrow(contract, payer, price + contract.Tax);
            Credits bond = Credits.Of(Math.Min(contract.PerformanceBond.Amount, Math.Floor(Math.Max(0m, performer.Credits.Amount) * 100m) / 100m));
            context.LockToEscrow(contract, performer, bond);
            contract.SetLockedBond(bond);
        }

        context.CloseNegotiationsOn(contract);
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
