using System;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

public static partial class DecisionEngine
{
    /// <summary>How full a warehouse must be before a producer sells delivered, carrying the haul itself.</summary>
    private const decimal SellDeliveredFullness = 0.45m;

    /// <summary>How full a warehouse must be before a producer posts a lot for sale.</summary>
    private const decimal PostLotFullness = 0.75m;

    /// <summary>How long a lot stays posted.</summary>
    private const long LotHours = 7 * 24;

    /// <summary>The most delivered sales a producer has outstanding at once.</summary>
    private const int MaxRemoteSales = 2;

    /// <summary>
    /// Producer heartbeat (design §5.3, §4.1): make goods up to the warehouse cap, paying production cost (a sink);
    /// answer wanted Sales at its own location; sell delivered elsewhere and post a Haul for it; post a lot for sale
    /// when the warehouse fills.
    /// </summary>
    public static void RunProducer(SimulationContext context, ActorState producer)
    {
        if (producer.Producer is not { } profile)
        {
            return;
        }

        Produce(context, producer, profile);

        foreach (SaleContract wanted in context.OpenContracts.OfType<SaleContract>().Where(s => s.Side == SaleSide.Wanted && s.Place == producer.Home))
        {
            TryAnswer(context, producer, wanted);
        }

        SellDelivered(context, producer, profile);
        ArrangeTransport(context, producer);
        PostLot(context, producer, profile);
    }

    private static void Produce(SimulationContext context, ActorState producer, ProducerProfile profile)
    {
        decimal room = profile.StockCap - producer.Stock(profile.Good);
        if (room <= 0m)
        {
            return; // idle: warehouse full, a leading indicator of softening prices (design §5.3)
        }

        decimal output = Math.Min(room, profile.OutputPerHeartbeat);
        Credits paid = context.ApplySink(producer, Credits.Of(profile.UnitProductionCost.Amount * output));
        if (paid.Amount <= 0m && producer.Credits.Amount <= 0m)
        {
            return; // can't afford to produce
        }

        context.Produce(producer, profile.Good, output);
    }

    /// <summary>A producer answers a wanted Sale at its location as posted, if it has the goods free and the price meets its ask.</summary>
    private static bool TryAnswer(SimulationContext context, ActorState producer, SaleContract wanted)
    {
        if (!wanted.IsOpen || wanted.PostedBy == producer.Id || producer.Producer is not { } profile || profile.Good != wanted.Good
            || wanted.Place != producer.Home || producer.Available(wanted.Good) < wanted.Quantity)
        {
            return false;
        }

        Credits ask = Ask(producer);
        if (wanted.OfferedPrice.Amount < ask.Amount * wanted.Quantity * 0.999m)
        {
            return false;
        }

        return Market.Supply(context, producer, wanted, wanted.OfferedPrice, Holding.StockAt(producer.Home)) == Signing.Signed;
    }

    /// <summary>
    /// A producer with stock to spare takes a delivered wanted Sale for its good elsewhere, and posts a Haul to deliver
    /// it, when the price leaves room for a carrier's fee on top of its ask (design §4.1: "Hauls when it sold delivered").
    /// </summary>
    private static void SellDelivered(SimulationContext context, ActorState producer, ProducerProfile profile)
    {
        decimal stock = producer.Stock(profile.Good);
        if (profile.Fullness(stock) < SellDeliveredFullness)
        {
            return;
        }

        int outstanding = context.LiveContractsOf(producer.Id).Count(c => c is SaleContract s && s.Seller == producer.Id && s.Place != producer.Home);
        if (outstanding >= MaxRemoteSales)
        {
            return;
        }

        Credits ask = Ask(producer);
        SaleContract? best = null;
        Credits bestFee = Credits.Zero;
        decimal bestMargin = 0m;
        foreach (SaleContract wanted in Visible<SaleContract>(context, producer))
        {
            // A carrier has to come for the goods before it can fly them: allow it the trip twice over and two days.
            if (wanted.Side != SaleSide.Wanted || wanted.Good != profile.Good || wanted.Place == producer.Home
                || producer.Available(profile.Good) < wanted.Quantity || Hours(context, producer.Home, wanted.Place) is not { } hours
                || context.CurrentTick + (hours * 2) + 48 > wanted.DeadlineTick)
            {
                continue;
            }

            // Its goods keep fresh in its warehouse; a carrier's standard hold must get them there before the buyer could reject them.
            if (wanted.Warranty is { } warranty && warranty.Rejectable(new Cargo(wanted.Good, 1m, 1m).Decayed(hours, 0m).Quality))
            {
                continue;
            }

            // As posted (S5 brings haggling): the price must cover its ask and a carrier's fee with room to spare.
            Credits fee = HaulFeeEstimate(context, producer.Home, wanted.Place);
            decimal margin = wanted.OfferedPrice.Amount - (ask.Amount * wanted.Quantity) - (fee.Amount * 1.3m);
            if (margin > bestMargin && Market.CanFund(wanted, context.Actor(wanted.PostedBy), wanted.OfferedPrice))
            {
                best = wanted;
                bestFee = fee;
                bestMargin = margin;
            }
        }

        if (best is null)
        {
            return;
        }

        decimal budget = best.OfferedPrice.Amount - (ask.Amount * best.Quantity);
        if (!HaulAffordable(bestFee, budget))
        {
            return;
        }

        (Credits offer, Credits reservation) = HaulTerms(context, producer, profile.Good, bestFee, budget);
        if (!Market.CanFundHaul(context, producer, producer.Home, best.Place, offer))
        {
            return; // it must be able to pay the carrier
        }

        if (Market.FillViaHaul(context, producer, best, best.OfferedPrice, FillSource.FromStock(producer), offer, reservation, out HaulContract? haul) == Signing.Signed
            && haul is not null)
        {
            producer.Prices(new PriceKey(PriceKind.Haul, profile.Good), UsualHaulMarkup).Posted(haul.Id);
        }
    }

    /// <summary>
    /// When the warehouse fills, a producer posts a lot for sale at its location, a little under its ask: the "posting
    /// food cheap" a market reader looks for (design §13). One lot at a time.
    /// </summary>
    private static void PostLot(SimulationContext context, ActorState producer, ProducerProfile profile)
    {
        if (context.LiveContractsOf(producer.Id).Any(c => c is SaleContract { Side: SaleSide.ForSale } s && s.IsOpen))
        {
            return;
        }

        decimal stock = producer.Stock(profile.Good);
        decimal available = producer.Available(profile.Good);
        if (profile.Fullness(stock) < PostLotFullness)
        {
            return;
        }

        decimal lot = Math.Floor(Math.Min(profile.StockCap * 0.2m, available - (profile.StockCap * 0.3m)) / 5m) * 5m;
        if (lot < 5m)
        {
            return;
        }

        // Priced under the ask, and learnt: a lot taken within a day makes the next a little dearer, one left unsold cheaper.
        PriceMemory memory = producer.Prices(new PriceKey(PriceKind.Lot, profile.Good), 0.92m);
        decimal factor = memory.Next(context, onQuickTake: 1.03m, onExpiry: 0.95m, min: 0.8m, max: 1m);
        Credits ask = Ask(producer);
        decimal unit = Math.Max(ask.Amount * factor, profile.UnitProductionCost.Amount); // never below cost
        Credits price = Credits.Of(Math.Round(unit * lot, 2));
        SaleContract posted = Market.PostSale(
            context, producer, SaleSide.ForSale, profile.Good, lot, producer.Home, price, Credits.Of(Math.Round(price.Amount * 0.9m, 2)),
            context.CurrentTick + LotHours, Holding.StockAt(producer.Home));
        memory.Posted(posted.Id);
    }
}
