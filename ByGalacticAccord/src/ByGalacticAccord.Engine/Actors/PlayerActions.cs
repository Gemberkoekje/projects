using System;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>Why the player can't take a contract right now. The interface words each one (design §11.5).</summary>
public enum DealBlock
{
    None,
    NotOpen,
    NoFreeShip,
    TooLarge,
    NoSource,
    NoRoute,
    PickupUnreachable,
    CannotAfford,
    TooLate,
}

/// <summary>
/// The player's commands, through exactly the market commands an AI decision uses (plan §2, "symmetric by
/// construction"). The player is just an actor (§4.7.3); the only difference is that a human picks the contract
/// and takes it as posted, and answers the decisions the terms hand it (design §3.4) where an AI decides by
/// utility. Checks return codes, not sentences: the interface's templates turn them into text.
/// </summary>
public static class PlayerActions
{
    /// <summary>Whether the player can take a haul, and with which ship from where.</summary>
    public sealed record HaulFeasibility(DealBlock Block, Ship? Ship)
    {
        public bool Ok => Block == DealBlock.None;
    }

    /// <summary>Whether the player can fill a delivered Sale, with which ship, and from which source.</summary>
    public sealed record FillFeasibility(DealBlock Block, Ship? Ship, FillSource? Source)
    {
        public bool Ok => Block == DealBlock.None;
    }

    /// <summary>Whether the player can buy a lot for sale and collect it with a ship.</summary>
    public sealed record BuyFeasibility(DealBlock Block, Ship? Ship)
    {
        public bool Ok => Block == DealBlock.None;
    }

    /// <summary>
    /// Whether the player can fill a delivered Sale through a carrier, as a trader with no free ship does: buy the goods
    /// into storage at the source and post a Haul to the place at <see cref="Fee"/>, the usual offer for the trip.
    /// </summary>
    public sealed record BrokerFeasibility(DealBlock Block, FillSource? Source, Credits Fee)
    {
        public bool Ok => Block == DealBlock.None;
    }

    /// <summary>Whether the player can post a haul for a Sale it sold whose goods wait with nothing moving them, and at what fee.</summary>
    public sealed record HaulAgainFeasibility(DealBlock Block, Holding? From, Credits Fee)
    {
        public bool Ok => Block == DealBlock.None;
    }

    /// <summary>The player's ship that would do the job: a free one, else the first (busy, for estimates).</summary>
    public static Ship? ShipFor(SimulationContext context, ActorState player) =>
        player.Ships.Select(context.Ship).FirstOrDefault(s => s.IsFree)
        ?? player.Ships.Select(context.Ship).FirstOrDefault();

    /// <summary>Can the player take this haul right now?</summary>
    public static HaulFeasibility CheckHaul(SimulationContext context, ActorState player, HaulContract haul)
    {
        if (!haul.IsOpen || haul.PostedBy == player.Id)
        {
            return new HaulFeasibility(DealBlock.NotOpen, null);
        }

        Ship? ship = ShipFor(context, player);
        if (ship is null)
        {
            return new HaulFeasibility(DealBlock.NoFreeShip, null);
        }

        if (context.FindPath(haul.Origin, haul.Destination) is null)
        {
            return new HaulFeasibility(DealBlock.NoRoute, ship);
        }

        (LocationId from, _) = Market.NextFree(context, ship);
        if (context.FindPath(from, haul.Origin) is null)
        {
            return new HaulFeasibility(DealBlock.PickupUnreachable, ship);
        }

        if (haul.Quantity > ship.FreeSpace)
        {
            return new HaulFeasibility(DealBlock.TooLarge, ship);
        }

        return new HaulFeasibility(ship.IsFree ? DealBlock.None : DealBlock.NoFreeShip, ship);
    }

    /// <summary>Take a haul as posted.</summary>
    public static Signing TakeHaul(SimulationContext context, ActorState player, HaulContract haul, Ship ship) =>
        Market.TakeHaul(context, player, haul, ship, haul.OfferedPrice);

    /// <summary>
    /// Can the player fill this delivered Sale? The source is the one a trader would pick: its own goods if it has them,
    /// else the producer that gets the goods to the place for least (the price plus the running cost of the trip).
    /// </summary>
    public static FillFeasibility CheckFill(SimulationContext context, ActorState player, SaleContract wanted)
    {
        if (!wanted.IsOpen || wanted.Side != SaleSide.Wanted || wanted.PostedBy == player.Id)
        {
            return new FillFeasibility(DealBlock.NotOpen, null, null);
        }

        Ship? ship = ShipFor(context, player);
        if (ship is null)
        {
            return new FillFeasibility(DealBlock.NoFreeShip, null, null);
        }

        (LocationId from, _) = Market.NextFree(context, ship);
        FillSource? best = null;
        decimal bestCost = decimal.MaxValue;
        foreach (FillSource source in DecisionEngine.FillSources(context, player, wanted, ship.IsFree ? ship : null))
        {
            LocationId pickup = source.Kind == FillSourceKind.Aboard ? ship.Location : source.At;
            if (context.FindPath(from, pickup) is not { } repo || context.FindPath(pickup, wanted.Place) is not { } laden)
            {
                continue;
            }

            long hours = PriceModel.TravelTicks(repo.DistanceTicks, ship.Class.SpeedMultiplier) + PriceModel.TravelTicks(laden.DistanceTicks, ship.Class.SpeedMultiplier);
            decimal cost = source.PurchasePrice.Amount + (ship.Class.RunningCostPerTick.Amount * hours);
            if (cost < bestCost)
            {
                best = source;
                bestCost = cost;
            }
        }

        if (best is null)
        {
            return new FillFeasibility(DealBlock.NoSource, ship, null);
        }

        if (best.Kind != FillSourceKind.Aboard && wanted.Quantity > ship.FreeSpace)
        {
            return new FillFeasibility(DealBlock.TooLarge, ship, best);
        }

        decimal purchase = best.PurchasePrice.Amount * (1m + Terms.TaxRate(context, context.Location(best.At).Reach));
        if (player.Credits.Amount < purchase)
        {
            return new FillFeasibility(DealBlock.CannotAfford, ship, best);
        }

        return new FillFeasibility(ship.IsFree ? DealBlock.None : DealBlock.NoFreeShip, ship, best);
    }

    /// <summary>Fill a delivered Sale as posted, from the source the check chose.</summary>
    public static Signing Fill(SimulationContext context, ActorState player, SaleContract wanted, Ship ship, FillSource source) =>
        Market.FillWithShip(context, player, wanted, wanted.OfferedPrice, ship, source);

    /// <summary>Can the player buy this lot as posted and collect it with a ship?</summary>
    public static BuyFeasibility CheckBuy(SimulationContext context, ActorState player, SaleContract lot)
    {
        if (!lot.IsOpen || lot.Side != SaleSide.ForSale || lot.PostedBy == player.Id)
        {
            return new BuyFeasibility(DealBlock.NotOpen, null);
        }

        Ship? ship = ShipFor(context, player);
        if (ship is null)
        {
            return new BuyFeasibility(DealBlock.NoFreeShip, null);
        }

        (LocationId from, _) = Market.NextFree(context, ship);
        if (context.FindPath(from, lot.Place) is null)
        {
            return new BuyFeasibility(DealBlock.PickupUnreachable, ship);
        }

        if (lot.Quantity > ship.FreeSpace)
        {
            return new BuyFeasibility(DealBlock.TooLarge, ship);
        }

        if (!Market.CanFund(lot, player, lot.OfferedPrice) || player.Credits < lot.OfferedPrice)
        {
            return new BuyFeasibility(DealBlock.CannotAfford, ship);
        }

        return new BuyFeasibility(ship.IsFree ? DealBlock.None : DealBlock.NoFreeShip, ship);
    }

    /// <summary>Buy a lot as posted; the ship goes to collect it.</summary>
    public static Signing Buy(SimulationContext context, ActorState player, SaleContract lot, Ship ship) =>
        Market.BuyLot(context, player, lot, lot.OfferedPrice, Holding.Aboard(ship.Id, lot.Place));

    /// <summary>
    /// Can the player fill this delivered Sale through a carrier? The source is the producer that gets the goods to the
    /// place for least, the goods' price and the usual haul fee together. A carrier needs two days to find the haul and
    /// the trip at standard speed, as a trader brokering allows. The player must be able to pay for the goods and
    /// their tax, fund the haul, and post its bond on the sale.
    /// </summary>
    public static BrokerFeasibility CheckBroker(SimulationContext context, ActorState player, SaleContract wanted)
    {
        if (!wanted.IsOpen || wanted.Side != SaleSide.Wanted || wanted.PostedBy == player.Id)
        {
            return new BrokerFeasibility(DealBlock.NotOpen, null, Credits.Zero);
        }

        FillSource? best = null;
        Credits bestFee = Credits.Zero;
        decimal bestCost = decimal.MaxValue;
        bool late = false;
        foreach (FillSource source in DecisionEngine.FillSources(context, player, wanted, ship: null))
        {
            if (source.At == wanted.Place || context.FindPath(source.At, wanted.Place) is not { } laden || context.StationOperatorAt(source.At) is null)
            {
                continue;
            }

            if (context.CurrentTick + 48 + laden.DistanceTicks > wanted.DeadlineTick)
            {
                late = true;
                continue;
            }

            Credits fee = DecisionEngine.UsualHaulOffer(context, source.At, wanted.Place);
            decimal cost = source.PurchasePrice.Amount + fee.Amount;
            if (cost < bestCost)
            {
                best = source;
                bestFee = fee;
                bestCost = cost;
            }
        }

        if (best is null)
        {
            return new BrokerFeasibility(late ? DealBlock.TooLate : DealBlock.NoSource, null, Credits.Zero);
        }

        decimal goods = best.PurchasePrice.Amount * (1m + Terms.TaxRate(context, context.Location(best.At).Reach));
        decimal haul = bestFee.Amount * (1m + wanted.TaxRate);
        decimal bond = wanted.UsesEscrow ? wanted.PerformanceBond.Amount : 0m;
        if (player.Credits.Amount < goods + haul + bond)
        {
            return new BrokerFeasibility(DealBlock.CannotAfford, best, bestFee);
        }

        return new BrokerFeasibility(DealBlock.None, best, bestFee);
    }

    /// <summary>Fill a delivered Sale as posted through a carrier: buy the goods into storage at the source and post the haul.</summary>
    public static Signing Broker(SimulationContext context, ActorState player, SaleContract wanted, FillSource source, Credits fee) =>
        Market.FillViaHaul(context, player, wanted, wanted.OfferedPrice, source, fee, fee);

    /// <summary>
    /// Can the player post a haul again for a Sale it sold whose goods wait away from the place with nothing moving them?
    /// Its untaken haul is withdrawn and reposted a quarter dearer, as an AI seller raises its offer; one that expired
    /// or was cancelled is replaced the same way. While a carrier near the goods could still deliver inside the sale's grace.
    /// </summary>
    public static HaulAgainFeasibility CheckHaulAgain(SimulationContext context, ActorState player, SaleContract sale)
    {
        if (sale.Status != ContractStatus.Active || sale.Seller != player.Id || sale.Reserved is not { } from || from.Location == sale.Place
            || Market.UnderWay(context, sale))
        {
            return new HaulAgainFeasibility(DealBlock.NotOpen, null, Credits.Zero);
        }

        if (context.FindPath(from.Location, sale.Place) is not { } plan)
        {
            return new HaulAgainFeasibility(DealBlock.NoRoute, from, Credits.Zero);
        }

        HaulContract? last = context.Contracts.Values.OfType<HaulContract>().LastOrDefault(h => h.Serves == sale.Id);
        Credits fee = last is not null ? DecisionEngine.RaisedHaulOffer(last) : DecisionEngine.UsualHaulOffer(context, from.Location, sale.Place);
        if (context.CurrentTick + plan.DistanceTicks + 24 > sale.GraceEndsTick)
        {
            return new HaulAgainFeasibility(DealBlock.TooLate, from, fee);
        }

        if (!Market.CanFundHaul(context, player, from.Location, sale.Place, fee))
        {
            return new HaulAgainFeasibility(DealBlock.CannotAfford, from, fee);
        }

        return new HaulAgainFeasibility(DealBlock.None, from, fee);
    }

    /// <summary>Post a haul again for a Sale the player sold, as the check found it: any untaken one is withdrawn first.</summary>
    public static HaulContract HaulAgain(SimulationContext context, ActorState player, SaleContract sale, Holding from, Credits fee)
    {
        foreach (Contract open in context.LiveContractsOf(player.Id))
        {
            if (open is HaulContract { IsOpen: true } haul && haul.Serves == sale.Id)
            {
                Market.Withdraw(context, haul);
            }
        }

        RoutePlan plan = context.FindPath(from.Location, sale.Place)!;
        return Market.PostHaul(context, player, sale.Good, sale.Quantity, from, sale.Place, fee, fee, DecisionEngine.HaulDeadline(context, sale, plan), sale.Id);
    }

    /// <summary>Walk away from a signed contract and pay the break fee.</summary>
    public static bool CancelAtWill(SimulationContext context, ActorState player, Contract contract) =>
        Market.CancelAtWill(context, contract, player);

    /// <summary>Cancel a contract whose counterparty is in default: the player's escrow back and their bond.</summary>
    public static bool CancelForDefault(SimulationContext context, ActorState player, Contract contract) =>
        Market.CancelForDefault(context, contract, player);

    /// <summary>
    /// Answer a decision the terms handed the player, through the same commands an AI's choice runs: cancel for default
    /// or wait; accept goods below the warranty's floor, with the refund, or reject them. False if the answer doesn't fit.
    /// </summary>
    public static bool Decide(SimulationContext context, ActorState player, PlayerDecision decision, DecisionChoice choice)
    {
        if (!decision.Choices.Contains(choice) || context.Contract(decision.Contract) is not { Status: ContractStatus.Active } contract
            || !context.ResolvePlayerDecision(decision))
        {
            return false;
        }

        switch (choice)
        {
            case DecisionChoice.Cancel:
                Market.CancelForDefault(context, contract, player);
                break;
            case DecisionChoice.Accept:
                Settlement.HandOverReserved(context, (SaleContract)contract, inspected: decision.Quality);
                break;
            case DecisionChoice.Reject:
                Settlement.Fail(context, contract, FailureReason.Rejected, decision.Quantity, decision.Quality);
                break;
            case DecisionChoice.Wait:
            default:
                break; // the right to cancel stays; the late fee keeps running to its cap
        }

        return true;
    }

    /// <summary>Put the player's goods aboard a free ship into storage where it is docked.</summary>
    public static bool Store(SimulationContext context, ActorState player, Ship ship, GoodType good)
    {
        if (!ship.IsFree || ship.Owner != player.Id || ship.LotOf(player.Id, good) is not { } lot || context.StationOperatorAt(ship.Location) is null)
        {
            return false;
        }

        Cargo goods = context.Unload(ship, player.Id, good, lot.Quantity);
        context.PutInStorage(player, ship.Location, goods);
        return true;
    }

    /// <summary>Load the player's free goods in storage where a free ship is docked, as much as the hold takes.</summary>
    public static bool Retrieve(SimulationContext context, ActorState player, Ship ship, GoodType good)
    {
        if (!ship.IsFree || ship.Owner != player.Id || context.StorageOf(player.Id, ship.Location) is not { } storage)
        {
            return false;
        }

        decimal quantity = Math.Min(storage.Available(good), ship.FreeSpace);
        if (quantity <= 0m)
        {
            return false;
        }

        Cargo goods = context.TakeFromStorage(player, ship.Location, good, quantity, reserved: false);
        context.Load(ship, player.Id, goods);
        return true;
    }
}
