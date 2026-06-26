using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Events;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>
/// The actor decision algorithm (§4.5.3): deliberately simple greedy utility with personality bias.
/// Emergent complexity is meant to come from many simple agents interacting, not clever individuals —
/// a transparent rule a developer can reason about and a player can learn to predict (§1.2).
/// </summary>
public static class DecisionEngine
{
    /// <summary>Producer heartbeat: convert production cost (a sink) into stock, up to the warehouse cap.</summary>
    public static void RunProducer(SimulationContext context, ActorState producer)
    {
        if (producer.Producer is not { } profile)
        {
            return;
        }

        decimal stock = producer.Stock(profile.Good);
        decimal room = profile.StockCap - stock;
        if (room <= 0m)
        {
            return; // idle: warehouse full, a leading indicator of softening prices (§4.2.2)
        }

        decimal output = Math.Min(room, profile.OutputPerHeartbeat);
        Credits cost = Credits.Of(profile.UnitProductionCost.Amount * output);
        Credits paid = context.ApplySink(producer, cost);
        if (paid.Amount <= 0m && producer.Credits.Amount <= 0m)
        {
            return; // can't afford to produce
        }

        producer.AddStock(profile.Good, output);
    }

    /// <summary>Consumer heartbeat: take income (the faucet), consume stock, and post demand contracts where short.</summary>
    public static void RunConsumer(SimulationContext context, ActorState consumer)
    {
        context.ApplyFaucet(consumer, consumer.IncomePerHeartbeat * context.FaucetMultiplier);

        foreach (ConsumerDemand demand in consumer.Demands)
        {
            consumer.RemoveStock(demand.Good, demand.ConsumptionPerHeartbeat);
        }

        foreach (ConsumerDemand demand in consumer.Demands)
        {
            decimal stock = consumer.Stock(demand.Good);
            if (stock >= demand.TargetStock * 0.6m)
            {
                continue; // buffer is healthy; no need to post yet
            }

            // Avoid stacking duplicate open demand contracts for the same good.
            bool alreadyPosted = context.OpenContracts.Any(c =>
                c.PostedBy == consumer.Id && c.Good == demand.Good && c.Status == ContractStatus.Open);
            if (!alreadyPosted)
            {
                TryPostDemand(context, consumer, demand);
            }
        }
    }

    private static void TryPostDemand(SimulationContext context, ActorState consumer, ConsumerDemand demand)
    {
        // Source from the cheapest producer of this good reachable anywhere in the graph (multi-hop).
        ActorState? producer = null;
        RoutePlan? laden = null;
        decimal bestAsk = decimal.MaxValue;
        foreach (ActorState candidate in context.ActorsOrdered)
        {
            if (candidate.Producer is not { } prof || prof.Good != demand.Good || candidate.Stock(demand.Good) <= 0m)
            {
                continue;
            }

            RoutePlan? plan = context.FindPath(candidate.Home, consumer.Home);
            if (plan is null)
            {
                continue;
            }

            decimal askAmount = PriceModel.ProducerAsk(prof, candidate.Stock(demand.Good)).Amount;
            if (askAmount < bestAsk)
            {
                bestAsk = askAmount;
                producer = candidate;
                laden = plan;
            }
        }

        if (producer is null || laden is null)
        {
            return;
        }

        decimal deficit = demand.TargetStock - consumer.Stock(demand.Good);
        decimal quantity = Math.Floor(Math.Min(deficit, producer.Stock(demand.Good)));
        if (quantity < 1m)
        {
            return;
        }

        Credits ask = PriceModel.ProducerAsk(producer.Producer!, producer.Stock(demand.Good));
        Credits goodsCost = Credits.Of(ask.Amount * quantity);
        Credits perUnitWillingness = PriceModel.ConsumerWillingness(demand.Good, consumer.Stock(demand.Good), demand.TargetStock);
        decimal feeBudget = (perUnitWillingness.Amount * quantity) - goodsCost.Amount;
        if (feeBudget <= 1m)
        {
            return; // no profitable spread; the consumer can't tempt a hauler right now
        }

        var maxFee = Credits.Of(feeBudget);
        var openingOffer = Credits.Of(feeBudget * 0.45m);

        AccordReach jurisdiction = context.Location(consumer.Home).Reach;
        string region = context.Location(consumer.Home).Region;
        decimal taxRate = context.Config.TaxRate;

        // Affordability: escrow jurisdictions require the consumer to lock goods + worst-case fee + tax up front.
        if (jurisdiction is AccordReach.DeepAccord or AccordReach.AccordEdge)
        {
            decimal outlay = goodsCost.Amount + maxFee.Amount + (maxFee.Amount * taxRate);
            if (consumer.Credits.Amount < outlay)
            {
                return;
            }
        }

        long travel = laden.DistanceTicks;
        long deadline = context.CurrentTick + (travel * 2) + 80;

        var clauses = new List<ContractClause>
        {
            new PenaltyOnLateDelivery(PenaltyPerTickLate: Math.Max(0.5m, maxFee.Amount * 0.01m), MaxPenalty: Credits.Of(maxFee.Amount * 0.3m)),
        };
        if (Goods.Info(demand.Good).Perishable)
        {
            clauses.Add(new QualityWarranty(MinQuality: 0.7m, MaxRefundFraction: 0.5m));
        }

        var contract = new Contract(
            context.NextContractId(), ContractType.Haul, consumer.Id, demand.Good, quantity,
            producer.Home, consumer.Home, openingOffer, maxFee, goodsCost, taxRate, deadline,
            jurisdiction, region, clauses);

        context.Add(contract);
        context.Schedule(new ContractExpiryCheck(deadline, contract.Id));
        context.Record("Posted", $"{contract.Id}: {consumer.Name} wants {quantity:0} {demand.Good} from {producer.Name} (≤{maxFee} fee).");
    }

    // ---- haulers (shipping companies + independent pilots) ----

    private readonly record struct Candidate(Contract Contract, Ship Ship, long RepoTicks, RoutePlan Laden, Credits Fee, decimal Utility);

    /// <summary>Hauler heartbeat: pay running costs, then commit each free ship to the best deal it can read.</summary>
    public static void RunHauler(SimulationContext context, ActorState hauler)
    {
        // Running cost drains whether or not a ship is earning (§3.6) — the sink that gates fleet size.
        foreach (ShipId shipId in hauler.Ships)
        {
            Ship ship = context.Ship(shipId);
            Credits running = Credits.Of(ship.Class.RunningCostPerTick.Amount * context.Config.HeartbeatInterval);
            context.ApplySink(hauler, running);
        }

        foreach (ShipId shipId in hauler.Ships.ToList())
        {
            Ship ship = context.Ship(shipId);
            if (ship.Status != ShipStatus.Docked)
            {
                continue;
            }

            Candidate? best = BestCandidate(context, hauler, ship);
            if (best is { } pick)
            {
                Commit(context, hauler, pick);
            }
        }
    }

    private static Candidate? BestCandidate(SimulationContext context, ActorState hauler, Ship ship)
    {
        Candidate? best = null;
        foreach (Contract contract in Visibility.OpenContractsVisibleTo(context, hauler))
        {
            if (contract.Type != ContractType.Haul || contract.Status != ContractStatus.Open)
            {
                continue;
            }

            if (contract.Quantity > ship.CargoCapacity)
            {
                continue; // wrong tool for the job (niche differentiation, §3.6)
            }

            ActorState? producer = context.ProducerAt(contract.OriginLocationId, contract.Good);
            if (producer is null || producer.Stock(contract.Good) <= 0m)
            {
                continue;
            }

            RoutePlan? laden = context.FindPath(contract.OriginLocationId, contract.DestinationLocationId);
            if (laden is null)
            {
                continue;
            }

            long repoTicks = 0;
            if (ship.Location != contract.OriginLocationId)
            {
                RoutePlan? repo = context.FindPath(ship.Location, contract.OriginLocationId);
                if (repo is null)
                {
                    continue; // can't reach the pickup at all
                }

                repoTicks = PriceModel.TravelTicks(repo.DistanceTicks, ship.Class.SpeedMultiplier);
            }

            Candidate? scored = Score(context, hauler, ship, contract, laden, repoTicks);
            if (scored is { } c && (best is null || c.Utility > best.Value.Utility))
            {
                best = c;
            }
        }

        return best;
    }

    private static Candidate? Score(SimulationContext context, ActorState hauler, Ship ship, Contract contract, RoutePlan laden, long repoTicks)
    {
        long ladenTicks = PriceModel.TravelTicks(laden.DistanceTicks, ship.Class.SpeedMultiplier);
        decimal runningWholeTrip = ship.Class.RunningCostPerTick.Amount * (ladenTicks + repoTicks);

        Credits pilotMin = Credits.Of(PriceModel.PilotMinimumFee(ship.Class, laden.DistanceTicks, laden.MaxDanger, hauler.Personality).Amount
                                      + (ship.Class.RunningCostPerTick.Amount * repoTicks));

        var buyer = new NegotiationParty(contract.BuyerReservationFee, context.Actor(contract.PostedBy).Personality.ConcessionRate, IsBuyer: true);
        var seller = new NegotiationParty(pilotMin, hauler.Personality.ConcessionRate, IsBuyer: false);
        NegotiationOutcome deal = Negotiation.ResolveAi(buyer, seller);
        if (!deal.Agreed)
        {
            return null;
        }

        decimal grossProfit = deal.Fee.Amount - runningWholeTrip;
        decimal riskAdjustment = 1m - (laden.MaxDanger * (1m - hauler.Personality.RiskTolerance));
        decimal growthWeight = 0.7m + (0.6m * hauler.Personality.GrowthAmbition);
        decimal opportunityCost = (ladenTicks + repoTicks) * 0.08m;
        decimal utility = (grossProfit * riskAdjustment * growthWeight) - opportunityCost;

        // Commit threshold gated by liquidity preference (§4.5.3): a cautious actor needs a clearly good deal.
        decimal threshold = 4m * (0.5m + hauler.Personality.LiquidityPreference);
        if (utility < threshold)
        {
            return null;
        }

        // Frontier jobs require fronting the goods cost (no escrow). Escrow jobs only need an
        // affordable bond — a near-broke pilot locks what it can and still gets to work (recovery).
        if (!contract.UsesEscrow && hauler.Credits.Amount < contract.GoodsCost.Amount)
        {
            return null;
        }

        return new Candidate(contract, ship, repoTicks, laden, deal.Fee, utility);
    }

    private static void Commit(SimulationContext context, ActorState hauler, Candidate pick)
    {
        Contract contract = pick.Contract;
        ActorState consumer = context.Actor(contract.PostedBy);

        if (contract.UsesEscrow)
        {
            Credits tax = Credits.Of(pick.Fee.Amount * contract.TaxRate);
            Credits outlay = contract.GoodsCost + pick.Fee + tax;
            if (consumer.Credits.Amount < outlay.Amount)
            {
                return; // consumer can no longer fund it; leave the contract open
            }
        }

        contract.BeginNegotiation();
        contract.Activate(hauler.Id, pick.Ship.Id, pick.Fee);

        if (contract.UsesEscrow)
        {
            Credits tax = Credits.Of(pick.Fee.Amount * contract.TaxRate);
            context.LockToEscrow(contract, consumer, contract.GoodsCost + pick.Fee + tax);
            Credits bond = Credits.Of(Math.Min(contract.PerformanceBond.Amount, hauler.Credits.Amount));
            context.LockToEscrow(contract, hauler, bond);
            contract.SetLockedBond(bond);
        }

        pick.Ship.Reserve(context.CurrentTick, contract.Id);
        context.Schedule(new ShipDeparted(context.CurrentTick + pick.RepoTicks, contract.Id));
        context.Record("Accepted", $"{contract.Id}: {hauler.Name} took the {contract.Good} haul for {pick.Fee}.");
    }
}
