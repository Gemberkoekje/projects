using System;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

public static partial class DecisionEngine
{
    /// <summary>A consumer restocks when its stock, with what is already on its way, falls below this share of its target.</summary>
    private const decimal RestockBelow = 0.6m;

    /// <summary>The most a consumer asks for in one post: what most holds carry. A bigger need is posted in parts.</summary>
    private const decimal MaxPostQuantity = 100m;

    /// <summary>
    /// Consumer heartbeat (design §4.1, §5.1): take income (the faucet), use up stock, bring home goods it owns
    /// elsewhere, and restock what is running short: by buying a cheap lot at its source and having it hauled, or
    /// by posting a delivered wanted Sale.
    /// </summary>
    public static void RunConsumer(SimulationContext context, ActorState consumer)
    {
        context.ApplyFaucet(consumer, consumer.IncomePerHeartbeat * context.FaucetMultiplier);

        foreach (ConsumerDemand demand in consumer.Demands)
        {
            context.Consume(consumer, demand.Good, demand.ConsumptionPerHeartbeat);
        }

        BringHome(context, consumer);

        foreach (ConsumerDemand demand in consumer.Demands)
        {
            decimal stock = consumer.Stock(demand.Good);
            decimal inbound = Inbound(context, consumer, demand.Good);
            if (stock + inbound >= demand.TargetStock * RestockBelow)
            {
                continue; // the buffer is healthy, or enough is on its way
            }

            decimal deficit = Math.Floor(Math.Min(MaxPostQuantity, demand.TargetStock - stock - inbound));
            if (deficit < 1m || TryBuyLot(context, consumer, demand, deficit))
            {
                continue;
            }

            PostWanted(context, consumer, demand, deficit);
        }
    }

    /// <summary>What a unit of a good is worth to a consumer now: how short it is, and what it can afford.</summary>
    public static Credits Willingness(ActorState consumer, GoodType good, decimal target) =>
        PriceModel.ConsumerWillingness(good, consumer.Stock(good), target, consumer.Credits, consumer.IncomePerHeartbeat);

    /// <summary>
    /// What a consumer already has coming of a good: wanted Sales it posted or signed and not yet received, and goods it
    /// owns away from home (in storage, or aboard a carrier's ship).
    /// </summary>
    public static decimal Inbound(SimulationContext context, ActorState consumer, GoodType good)
    {
        decimal inbound = 0m;
        foreach (Contract contract in context.LiveContractsOf(consumer.Id))
        {
            if (contract is SaleContract sale && sale.Good == good && sale.Buyer == consumer.Id && sale.Place == consumer.Home)
            {
                inbound += sale.Quantity;
            }
            else if (contract is HaulContract { Status: ContractStatus.Active, AssignedShip: { } shipId } haul && haul.Shipper == consumer.Id && haul.Good == good)
            {
                inbound += context.Ship(shipId).LotOf(consumer.Id, good)?.Quantity ?? 0m;
            }
        }

        foreach (StorageContract storage in context.StoragesOf(consumer.Id))
        {
            if (storage.Location != consumer.Home)
            {
                inbound += storage.Held(good);
            }
        }

        return inbound;
    }

    /// <summary>Goods a consumer owns in storage elsewhere go home: a Haul for each that nothing is moving yet.</summary>
    private static void BringHome(SimulationContext context, ActorState consumer)
    {
        foreach (StorageContract storage in context.StoragesOf(consumer.Id).ToList())
        {
            if (storage.Location == consumer.Home)
            {
                continue;
            }

            foreach (GoodType good in Goods.All)
            {
                decimal free = storage.Available(good);
                if (free < 1m)
                {
                    continue; // nothing there, or already set aside for a haul
                }

                Credits estimate = HaulFeeEstimate(context, storage.Location, consumer.Home);
                if (estimate.Amount <= 0m)
                {
                    continue;
                }

                ConsumerDemand? demand = consumer.Demands.FirstOrDefault(d => d.Good == good);
                decimal value = Willingness(consumer, good, demand?.TargetStock ?? free).Amount * free;
                (Credits offer, Credits reservation) = HaulTerms(context, consumer, good, estimate, Math.Max(estimate.Amount * 2m, value * 0.3m));
                if (!Market.CanFundHaul(context, consumer, storage.Location, consumer.Home, offer))
                {
                    continue;
                }

                long hours = Hours(context, storage.Location, consumer.Home) ?? 0;
                PostPricedHaul(
                    context, consumer, good, free, Holding.StorageAt(storage.Location), consumer.Home, offer, reservation,
                    context.CurrentTick + (hours * 2) + 72, serves: null);
            }
        }
    }

    /// <summary>
    /// A consumer buys a lot posted for sale elsewhere when, with a carrier's fee and a little storage on top, it still
    /// costs well under what the goods are worth to it; it then hauls them home (the design's "move my 60 Food from
    /// Verdant to Rust Belt").
    /// </summary>
    private static bool TryBuyLot(SimulationContext context, ActorState consumer, ConsumerDemand demand, decimal deficit)
    {
        SaleContract? best = null;
        decimal bestCost = decimal.MaxValue;
        foreach (SaleContract lot in Visible<SaleContract>(context, consumer))
        {
            if (lot.Side != SaleSide.ForSale || lot.Good != demand.Good || lot.Quantity > deficit * 1.2m || lot.Quantity < deficit * 0.3m)
            {
                continue;
            }

            decimal cost = lot.Price.Amount * (1m + lot.TaxRate);
            if (lot.Place != consumer.Home)
            {
                Credits estimate = HaulFeeEstimate(context, lot.Place, consumer.Home);
                ActorState? station = context.StationOperatorAt(lot.Place);
                if (estimate.Amount <= 0m || station is null)
                {
                    continue;
                }

                cost += (estimate.Amount * 1.3m) + (station.Station!.StorageRatePerUnitDay.Amount * lot.Quantity * 2m);
            }

            decimal value = Willingness(consumer, demand.Good, demand.TargetStock).Amount * lot.Quantity;
            decimal perUnit = cost / lot.Quantity;
            if (cost <= value * 0.85m && perUnit < bestCost && consumer.Credits.Amount >= cost)
            {
                best = lot;
                bestCost = perUnit;
            }
        }

        if (best is null)
        {
            return false;
        }

        Holding receipt = best.Place == consumer.Home ? Holding.StockAt(consumer.Home) : Holding.StorageAt(best.Place);
        if (Market.BuyLot(context, consumer, best, best.Price, receipt) != Signing.Signed)
        {
            return false;
        }

        BringHome(context, consumer);
        return true;
    }

    /// <summary>
    /// Post a delivered wanted Sale for what is short (design §4.1). The price sits between what the cheapest
    /// producer asks and what the goods are worth to the consumer, which is its hidden reservation. With a
    /// producer at home it is a local sale, answered at once.
    /// </summary>
    private static void PostWanted(SimulationContext context, ActorState consumer, ConsumerDemand demand, decimal quantity)
    {
        ActorState? source = null;
        decimal bestAsk = decimal.MaxValue;
        long hours = 0;
        foreach (ActorState producer in context.ProducersOf(demand.Good))
        {
            if (producer.Available(demand.Good) < 1m || Hours(context, producer.Home, consumer.Home) is not { } h)
            {
                continue;
            }

            decimal ask = Ask(producer).Amount;
            if (producer.Home == consumer.Home && producer.Available(demand.Good) >= quantity)
            {
                ask *= 0.5m; // a producer at home needs no carrier: always the one to buy from
            }

            if (ask < bestAsk)
            {
                bestAsk = ask;
                source = producer;
                hours = h;
            }
        }

        if (source is null)
        {
            return;
        }

        bool local = source.Home == consumer.Home;
        decimal goods = Ask(source).Amount * quantity;
        decimal worth = Willingness(consumer, demand.Good, demand.TargetStock).Amount * quantity;
        decimal budget = worth - goods;
        if (budget <= (local ? 0m : 1m))
        {
            return; // no spread: the consumer can't tempt anyone to supply it right now
        }

        // Its offer: the goods plus a share of the spread, learnt from its own posts (taken within a day, it offers less
        // of the spread next time; expired, more). A local sale needs no carrier and is offered just over the ask.
        PriceMemory memory = consumer.Prices(new PriceKey(PriceKind.Wanted, demand.Good), 0.45m);
        decimal share = local ? 0m : memory.Next(context, onQuickTake: 0.85m, onExpiry: 1.3m, min: 0.1m, max: 1m);
        decimal price = Math.Round(local ? Math.Min(worth, goods * 1.05m) : goods + (budget * share), 2);
        if (consumer.Credits.Amount < price * (1m + Terms.TaxRate(context, context.Location(consumer.Home).Reach)))
        {
            return; // it must be able to pay: into escrow in Accord space, at handover outside it
        }

        // Terms no carrier could meet from the cheapest source only deter sellers: it relaxes them to what a standard hold meets.
        TermPackage package = Terms.Meetable(Terms.PackageFor(consumer), demand.Good, new Cargo(demand.Good, 1m, 1m).Decayed(hours, 0m).Quality);

        long deadline = local ? context.CurrentTick + 48 : context.CurrentTick + (hours * 2) + 80;
        SaleContract posted = Market.PostSale(
            context, consumer, SaleSide.Wanted, demand.Good, quantity, consumer.Home,
            Credits.Of(price), Credits.Of(Math.Round(worth, 2)), deadline, Holding.StockAt(consumer.Home), package);
        if (!local)
        {
            memory.Posted(posted.Id);
        }
    }
}
