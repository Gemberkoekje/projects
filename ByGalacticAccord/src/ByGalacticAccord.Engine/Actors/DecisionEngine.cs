using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>
/// The actor decision algorithm (design §4.3): deliberately simple greedy utility with personality bias.
/// Emergent complexity is meant to come from many simple agents interacting, not clever individuals, so each
/// role has a small set of actions (plan risk R2):
/// <list type="bullet">
/// <item>consumers post delivered wanted Sales, or buy a cheap lot at its source and have it hauled home;</item>
/// <item>producers post for-sale lots, answer wanted Sales at their location, and sell delivered and post a Haul;</item>
/// <item>traders take Hauls, fill delivered Sales by buying and carrying, and post Hauls when overcommitted;</item>
/// <item>station operators collect rent.</item>
/// </list>
/// Every role also invokes its terms (design §3.4, §4.3): a payer whose counterparty is in default cancels or waits, a
/// buyer handed spoiled goods rejects or accepts them, and a seller that can no longer deliver walks away rather than
/// default. Every action goes through <see cref="Market"/>, the same commands the player uses.
/// </summary>
public static partial class DecisionEngine
{
    /// <summary>An actor's turn on its heartbeat, by what it does. The player's actor never comes here.</summary>
    public static void Run(SimulationContext context, ActorState actor)
    {
        if (actor.Station is not null)
        {
            RunStation(context, actor);
        }

        if (actor.Producer is not null)
        {
            RunProducer(context, actor);
        }

        if (actor.Demands.Count > 0)
        {
            RunConsumer(context, actor);
        }

        if (actor.MovesCargo)
        {
            RunTrader(context, actor);
        }

        if (actor.Role != ActorRole.Government)
        {
            PayOut(context, actor);
        }
    }

    /// <summary>
    /// The most an AI actor keeps in hand (S3's safety valve, like the inflation monitor, design §5.5): money it has
    /// no use for in the sector leaves it, as a consumer's spending on other things or a business's payout to its
    /// owners. Without it a saver soaks up the money supply and the monitor starves everyone else. Growth (S7),
    /// insolvency (S8) and demand starvation (§5.6) are the organic regulators that should replace it. The player is
    /// never touched.
    /// </summary>
    public static Credits WorkingCapital(SimulationContext context, ActorState actor)
    {
        SimulationConfig config = context.Config;
        if (actor.Station is { } station)
        {
            return station.Float;
        }

        if (actor.Producer is { } profile)
        {
            return Credits.Of(profile.UnitProductionCost.Amount * profile.StockCap * config.ProducerReserveWarehouses);
        }

        if (actor.Demands.Count > 0)
        {
            return Credits.Of(actor.IncomePerHeartbeat.Amount * config.ConsumerSavingsDays);
        }

        if (actor.MovesCargo)
        {
            decimal fleet = actor.Ships.Sum(id => context.Ship(id).Class.MarketValue.Amount);
            return Credits.Of(fleet * config.TraderReserveFleets);
        }

        return Credits.Of(decimal.MaxValue);
    }

    // Money above an actor's working capital leaves the sector.
    private static void PayOut(SimulationContext context, ActorState actor)
    {
        decimal surplus = actor.Credits.Amount - WorkingCapital(context, actor).Amount;
        if (surplus > 0m)
        {
            context.ApplySink(actor, Credits.Of(surplus));
        }
    }

    /// <summary>Running costs drain whether or not a ship is earning (design §6.5): the sink that gates fleet size.</summary>
    public static void ChargeRunningCosts(SimulationContext context, ActorState owner)
    {
        foreach (ShipId shipId in owner.Ships)
        {
            Ship ship = context.Ship(shipId);
            context.ApplySink(owner, Credits.Of(ship.Class.RunningCostPerTick.Amount * context.Config.HeartbeatInterval));
        }
    }

    /// <summary>
    /// AI producers at an AI's wanted Sale's place answer it the moment it is posted (design §4.3: an actor re-evaluates when
    /// a contract it can see appears; between AI actors a haggle resolves in closed form, at once). The player's posts are
    /// answered over game time instead (<see cref="NoticePost"/>).
    /// </summary>
    public static void AnswerAtOnce(SimulationContext context, SaleContract wanted)
    {
        foreach (ActorId id in context.Location(wanted.Place).Residents)
        {
            ActorState resident = context.Actor(id);
            if (!resident.IsPlayer && resident.Producer is { } profile && profile.Good == wanted.Good && TryAnswer(context, resident, wanted))
            {
                return;
            }
        }
    }

    /// <summary>
    /// What a typical carrier would ask to move goods between two places: a Light Freighter's minimum fee for the
    /// trip with an average personality, plus an empty leg to the pickup as long as the average trip there from the
    /// sector's locations. Shippers price hauls from it.
    /// </summary>
    public static Credits HaulFeeEstimate(SimulationContext context, LocationId from, LocationId to)
    {
        RoutePlan? plan = context.FindPath(from, to);
        if (plan is null || from == to)
        {
            return Credits.Zero;
        }

        ShipClassInfo standard = ShipClasses.Info(ShipType.LightFreighter);
        decimal minimum = PriceModel.PilotMinimumFee(standard, plan.DistanceTicks, plan.MaxDanger, Personality.Balanced).Amount;
        decimal emptyLeg = standard.RunningCostPerTick.Amount * AverageHoursTo(context, from);
        return Credits.Of(Math.Round(((minimum + emptyLeg) * 1.25m) + 10m, 2));
    }

    // The average trip to a location from every location in the sector, at standard speed.
    private static decimal AverageHoursTo(SimulationContext context, LocationId to)
    {
        decimal sum = 0m;
        int count = 0;
        foreach (LocationId from in context.Locations.Keys)
        {
            if (context.FindPath(from, to) is { } plan)
            {
                sum += plan.DistanceTicks;
                count++;
            }
        }

        return count == 0 ? 0m : sum / count;
    }

    /// <summary>How long an AI payer waits before looking again at a contract in default it chose to wait on.</summary>
    private const long DefaultReviewHours = 24;

    /// <summary>What a shipper first offers for a haul, as a multiple of the usual fee (<see cref="HaulFeeEstimate"/>).</summary>
    public const decimal UsualHaulMarkup = 1.6m;

    /// <summary>What a shipper offers for a haul it has no experience of: the usual fee with the usual markup. The player's offer.</summary>
    public static Credits UsualHaulOffer(SimulationContext context, LocationId from, LocationId to) =>
        Credits.Of(Math.Round(HaulFeeEstimate(context, from, to).Amount * UsualHaulMarkup, 2));

    /// <summary>
    /// What a shipper offers for a haul (and the most it would go to), given the usual fee and what moving the goods is
    /// worth to it. It starts at the usual markup on the usual fee and learns from its own hauls: taken within a day, it
    /// offers a little less next time; expired, more.
    /// </summary>
    private static (Credits Offer, Credits Reservation) HaulTerms(SimulationContext context, ActorState shipper, GoodType good, Credits estimate, decimal budget)
    {
        PriceMemory memory = shipper.Prices(new PriceKey(PriceKind.Haul, good), UsualHaulMarkup);
        decimal factor = memory.Next(context, onQuickTake: 0.95m, onExpiry: 1.25m, min: 1.1m, max: 3m);
        decimal offer = Math.Min(budget * 0.9m, estimate.Amount * factor);
        decimal reservation = Math.Min(budget * 0.95m, estimate.Amount * 3m);
        return (Credits.Of(Math.Round(offer, 2)), Credits.Of(Math.Round(Math.Max(offer, reservation), 2)));
    }

    /// <summary>Post a haul priced by <see cref="HaulTerms"/>, and remember it for next time.</summary>
    private static HaulContract PostPricedHaul(
        SimulationContext context, ActorState shipper, GoodType good, decimal quantity, Holding from, LocationId destination,
        Credits offer, Credits reservation, long deadline, ContractId? serves)
    {
        HaulContract haul = Market.PostHaul(context, shipper, good, quantity, from, destination, offer, reservation, deadline, serves);
        shipper.Prices(new PriceKey(PriceKind.Haul, good), UsualHaulMarkup).Posted(haul.Id);
        return haul;
    }


    /// <summary>The most a shipper can usefully spend moving goods: the haul path is only worth it above the estimate.</summary>
    private static bool HaulAffordable(Credits estimate, decimal budget) => estimate.Amount > 0m && budget >= estimate.Amount * 1.1m;

    /// <summary>
    /// Sellers whose signed delivered Sale has goods set aside away from the place and nothing moving them (its haul
    /// expired unsigned) arrange transport again: their own free ship if one can carry the goods, or a new haul, while a carrier near the goods
    /// could still make it before the sale's grace runs out. Once none could, the seller walks away
    /// (<see cref="WouldWalkAway"/>) rather than wait to be cancelled for default.
    /// </summary>
    private static void ArrangeTransport(SimulationContext context, ActorState seller)
    {
        RaiseHaulOffers(context, seller);
        foreach (Contract contract in context.LiveContractsOf(seller.Id))
        {
            if (contract is not SaleContract { Status: ContractStatus.Active } sale || sale.Seller != seller.Id
                || sale.Reserved is not { } from || from.Location == sale.Place || Market.UnderWay(context, sale))
            {
                continue;
            }

            if (context.LiveContractsOf(seller.Id).Any(c => c is HaulContract h && h.Serves == sale.Id))
            {
                continue; // a haul for it is up: RaiseHaulOffers reprices it
            }

            // Nothing is up for it. Too late even for a carrier near the pickup to deliver inside the grace: walk away now,
            // or leave it to the buyer.
            RoutePlan? plan = context.FindPath(from.Location, sale.Place);
            if (plan is null || context.CurrentTick + plan.DistanceTicks + 24 > sale.GraceEndsTick)
            {
                if (WouldWalkAway(seller, sale))
                {
                    Market.CancelAtWill(context, sale, seller);
                }

                continue;
            }

            // Its own free ship, if one can carry the goods; else a haul.
            Ship? ship = from.Kind == HoldingKind.Storage
                ? seller.Ships.Select(context.Ship).FirstOrDefault(s => s.IsFree && s.CargoCapacity >= sale.Quantity)
                : null;
            if (ship is not null && context.FindPath(ship.Location, from.Location) is not null)
            {
                Market.Dispatch(context, ship, new ShipJob(seller.Id, sale.Good, sale.Quantity, from.Location, PickupSource.Storage, sale.Place, Delivery: sale.Id));
                continue;
            }

            Credits estimate = HaulFeeEstimate(context, from.Location, sale.Place);
            decimal budget = Math.Max(estimate.Amount * 2m, sale.Price.Amount * 0.25m);
            (Credits offer, Credits reservation) = HaulTerms(context, seller, sale.Good, estimate, budget);
            if (Market.CanFundHaul(context, seller, from.Location, sale.Place, offer))
            {
                PostPricedHaul(context, seller, sale.Good, sale.Quantity, from, sale.Place, offer, reservation, HaulDeadline(context, sale, plan), sale.Id);
            }
        }
    }

    /// <summary>
    /// When a haul for a signed Sale must deliver: the sale's deadline, or later if that is past reach, but always inside
    /// the sale's grace, where a late delivery still costs only the late fee.
    /// </summary>
    public static long HaulDeadline(SimulationContext context, SaleContract sale, RoutePlan plan) =>
        Math.Max(sale.DeadlineTick, Math.Min(sale.GraceEndsTick, context.CurrentTick + (plan.DistanceTicks * 2) + 24));

    /// <summary>
    /// Whether a seller that can no longer deliver walks away, paying the break fee, rather than wait to be cancelled for
    /// default. In Accord space default forfeits its bond, so it walks when the break fee is less. Outside the Accord
    /// nothing is enforced: only an actor that minds its record pays (design §4.2).
    /// </summary>
    public static bool WouldWalkAway(ActorState seller, Contract contract) =>
        contract.UsesEscrow ? contract.Terms.BreakFee < contract.LockedBond : seller.Personality.TrustThreshold >= 0.5m;

    /// <summary>
    /// An AI payer whose contract is in default cancels for default, or waits and looks again tomorrow (design §4.3: invoke
    /// a term). Cancelling gets its money back and the performer's bond. Waiting gets what it signed for, late, and the
    /// late fee as it runs to its cap. With nothing on its way there is nothing to wait for; a haul's goods are the
    /// shipper's and arrive whatever it decides, so a shipper always cancels.
    /// </summary>
    public static void ReviewDefault(SimulationContext context, ActorState payer, Contract contract)
    {
        if (WouldCancel(context, payer, contract))
        {
            Market.CancelForDefault(context, contract, payer);
            return;
        }

        context.Schedule(new Events.DefaultReview(context.CurrentTick + DefaultReviewHours, contract.Id));
    }

    /// <summary>The utility comparison behind <see cref="ReviewDefault"/>.</summary>
    public static bool WouldCancel(SimulationContext context, ActorState payer, Contract contract)
    {
        if (contract is not SaleContract sale || Market.ExpectedArrival(context, contract) is not { } arrival)
        {
            return true;
        }

        decimal cancel = sale.UsesEscrow ? sale.LockedBond.Amount : 0m;
        decimal worth = (sale.PostedBy == payer.Id ? sale.Reservation : sale.Price).Amount;
        decimal lateFee = sale.LatePenalty?.PenaltyFor(arrival - sale.DeadlineTick).Amount ?? 0m;
        decimal wait = worth - sale.Price.Amount + Math.Min(lateFee, cancel);
        return cancel > wait;
    }

    /// <summary>
    /// Whether a buyer rejects goods below the warranty's floor (design §3.4): accepting gets the goods, worth to it what
    /// they are worth at their quality, for the price less the refund; rejecting gets its money back and the seller's bond.
    /// </summary>
    public static bool WouldReject(SimulationContext context, ActorState buyer, SaleContract sale, decimal quantity, decimal quality)
    {
        decimal unitWorth = (sale.PostedBy == buyer.Id ? sale.Reservation : sale.Price).Amount / Math.Max(1m, sale.Quantity);
        SettlementTerms terms = Settlement.Due(context, sale, sale.Quantity, quantity, quality, 0);
        decimal accept = (unitWorth * quantity * quality) - terms.Payment.Amount;
        decimal reject = sale.UsesEscrow ? sale.LockedBond.Amount : 0m;
        return reject > accept;
    }

    /// <summary>What a producer asks per unit now: its cost, margin, warehouse and savings (design §5.2).</summary>
    public static Credits Ask(ActorState producer) =>
        PriceModel.ProducerAsk(producer.Producer!, producer.Stock(producer.Producer!.Good), producer.Credits);

    /// <summary>
    /// A haul a seller posted to deliver a Sale that nobody has taken in a day is reposted a quarter dearer, while a
    /// carrier still has time to come and make it inside the sale's grace: the shipper raises its offer rather than let
    /// the Sale fail.
    /// </summary>
    private static void RaiseHaulOffers(SimulationContext context, ActorState seller)
    {
        foreach (Contract contract in context.LiveContractsOf(seller.Id))
        {
            if (contract is not HaulContract { Status: ContractStatus.Open, Serves: { } served } haul || haul.PostedBy != seller.Id
                || context.CurrentTick - haul.PostedTick < 24 || context.Contract(served) is not SaleContract { Status: ContractStatus.Active } sale)
            {
                continue;
            }

            RoutePlan? plan = context.FindPath(haul.Origin, haul.Destination);
            Credits offer = RaisedHaulOffer(haul);
            if (plan is null || context.CurrentTick + (plan.DistanceTicks * 2) > sale.GraceEndsTick
                || offer > haul.Reservation || !Market.CanFundHaul(context, seller, haul.Origin, haul.Destination, offer))
            {
                continue;
            }

            Market.Withdraw(context, haul);
            PostPricedHaul(context, seller, haul.Good, haul.Quantity, haul.PickupFrom, haul.Destination, offer, haul.Reservation, HaulDeadline(context, sale, plan), sale.Id);
        }
    }

    /// <summary>What a shipper reposts an untaken haul at: a quarter dearer.</summary>
    public static Credits RaisedHaulOffer(HaulContract haul) => Credits.Of(Math.Round(haul.OfferedPrice.Amount * 1.25m, 2));

    /// <summary>
    /// Others' contracts of a kind that an actor believes open, in id order (design §7.1, D31, D32): open ones it sees, and
    /// ones the news reported open that it hasn't found closed. It finds out whether those still are by trying.
    /// </summary>
    private static IEnumerable<T> Known<T>(SimulationContext context, ActorState actor)
        where T : Contract =>
        Knowledge.OpenContracts(context, actor, Visibility.Of(context, actor)).OfType<T>().Where(c => c.PostedBy != actor.Id);

    /// <summary>Hours at standard speed between two places, or null if no route joins them.</summary>
    private static long? Hours(SimulationContext context, LocationId from, LocationId to) =>
        context.FindPath(from, to)?.DistanceTicks;
}
