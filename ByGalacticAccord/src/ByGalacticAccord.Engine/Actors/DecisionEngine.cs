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
/// Every action goes through <see cref="Market"/>, the same commands the player uses.
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
    /// AI producers at a wanted Sale's place answer it the moment it is posted (design §4.3: an actor re-evaluates
    /// when a contract it can see appears). S5 makes replies take game time.
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

    /// <summary>
    /// What a shipper offers for a haul (and the most it would go to), given the usual fee and what moving the goods is
    /// worth to it. It starts at 1.6 times the usual fee and learns from its own hauls: taken within a day, it offers a
    /// little less next time; expired, more.
    /// </summary>
    private static (Credits Offer, Credits Reservation) HaulTerms(SimulationContext context, ActorState shipper, GoodType good, Credits estimate, decimal budget)
    {
        PriceMemory memory = shipper.Prices(new PriceKey(PriceKind.Haul, good), 1.6m);
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
        shipper.Prices(new PriceKey(PriceKind.Haul, good), 1.6m).Posted(haul.Id);
        return haul;
    }


    /// <summary>The most a shipper can usefully spend moving goods: the haul path is only worth it above the estimate.</summary>
    private static bool HaulAffordable(Credits estimate, decimal budget) => estimate.Amount > 0m && budget >= estimate.Amount * 1.1m;

    /// <summary>
    /// Sellers whose signed delivered Sale has goods set aside away from the place and nothing moving them (its haul
    /// expired unsigned) arrange transport again: their own free ship, or a new haul, while there is still time.
    /// </summary>
    private static void ArrangeTransport(SimulationContext context, ActorState seller)
    {
        RaiseHaulOffers(context, seller);
        foreach (Contract contract in context.LiveContractsOf(seller.Id))
        {
            if (contract is not SaleContract { Status: ContractStatus.Active } sale || sale.Seller != seller.Id
                || sale.Reserved is not { } from || from.Location == sale.Place || Market.UnderWay(context, sale)
                || context.LiveContractsOf(seller.Id).Any(c => c is HaulContract h && h.Serves == sale.Id))
            {
                continue;
            }

            RoutePlan? plan = context.FindPath(from.Location, sale.Place);
            if (plan is null || context.CurrentTick + (plan.DistanceTicks * 2) + 24 > sale.DefaultTick)
            {
                continue; // too late for a carrier to come and make it: the sale will fail on its own
            }

            Ship? ship = from.Kind == HoldingKind.Storage ? seller.Ships.Select(context.Ship).FirstOrDefault(s => s.IsFree) : null;
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
                PostPricedHaul(context, seller, sale.Good, sale.Quantity, from, sale.Place, offer, reservation, sale.DeadlineTick, sale.Id);
            }
        }
    }

    /// <summary>What a producer asks per unit now: its cost, margin, warehouse and savings (design §5.2).</summary>
    public static Credits Ask(ActorState producer) =>
        PriceModel.ProducerAsk(producer.Producer!, producer.Stock(producer.Producer!.Good), producer.Credits);

    /// <summary>
    /// A haul a seller posted to deliver a Sale that nobody has taken in a day is reposted a quarter dearer, while a
    /// carrier still has time to come and make the deadline: the shipper raises its offer rather than let the Sale fail.
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
            Credits offer = Credits.Of(Math.Round(haul.OfferedPrice.Amount * 1.25m, 2));
            if (plan is null || context.CurrentTick + (plan.DistanceTicks * 2) > haul.DeadlineTick
                || offer > haul.Reservation || !Market.CanFundHaul(context, seller, haul.Origin, haul.Destination, offer))
            {
                continue;
            }

            Market.Withdraw(context, haul);
            PostPricedHaul(context, seller, haul.Good, haul.Quantity, haul.PickupFrom, haul.Destination, offer, haul.Reservation, haul.DeadlineTick, sale.Id);
        }
    }

    /// <summary>A visible open contract of a kind, in id order.</summary>
    private static IEnumerable<T> Visible<T>(SimulationContext context, ActorState actor)
        where T : Contract =>
        Visibility.OpenContractsVisibleTo(context, actor).OfType<T>().Where(c => c.Status == ContractStatus.Open && c.PostedBy != actor.Id);

    /// <summary>Hours at standard speed between two places, or null if no route joins them.</summary>
    private static long? Hours(SimulationContext context, LocationId from, LocationId to) =>
        context.FindPath(from, to)?.DistanceTicks;
}
