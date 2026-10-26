using System;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Events;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>Why the player can't take a haul right now. The interface words each one (design §11.5).</summary>
public enum HaulBlock
{
    None,
    NotOpen,
    NoFreeShip,
    TooLarge,
    OriginShort,
    NoRoute,
    PickupUnreachable,
}

/// <summary>How an attempt to sign a haul ended.</summary>
public enum HaulAcceptance
{
    Accepted,
    NotOpen,
    PosterCannotFund,
    CannotFrontGoods,
}

/// <summary>
/// Player-initiated contract actions, shared by every front-end so the player commits to a haul through
/// exactly the same path as the simulation uses elsewhere. The player is just an actor (§4.7.3); the only
/// difference is a human picks the contract instead of the decision engine. Results are codes, not
/// sentences: the interface's templates turn them into text.
/// </summary>
public static class PlayerActions
{
    /// <summary>Whether the player can take a haul, and if so with which ship and after how long repositioning.</summary>
    public sealed record HaulFeasibility(HaulBlock Block, Ship? Ship, long RepoTicks)
    {
        public bool Ok => Block == HaulBlock.None;
    }

    /// <summary>Can the player take this haul right now? Returns the ship that would fly it and any repositioning time.</summary>
    public static HaulFeasibility CheckHaul(SimulationContext context, ActorState player, Contract contract)
    {
        if (contract.Type != ContractType.Haul || contract.Status != ContractStatus.Open)
        {
            return new HaulFeasibility(HaulBlock.NotOpen, null, 0);
        }

        Ship? ship = player.Ships.Select(context.Ship).FirstOrDefault(s => s.Status == ShipStatus.Docked);
        if (ship is null)
        {
            return new HaulFeasibility(HaulBlock.NoFreeShip, null, 0);
        }

        if (contract.Quantity > ship.CargoCapacity)
        {
            return new HaulFeasibility(HaulBlock.TooLarge, ship, 0);
        }

        var producer = context.ProducerAt(contract.OriginLocationId, contract.Good);
        if (producer is null || producer.Stock(contract.Good) < contract.Quantity)
        {
            return new HaulFeasibility(HaulBlock.OriginShort, ship, 0);
        }

        if (context.FindPath(contract.OriginLocationId, contract.DestinationLocationId) is null)
        {
            return new HaulFeasibility(HaulBlock.NoRoute, ship, 0);
        }

        long repoTicks = 0;
        if (ship.Location != contract.OriginLocationId)
        {
            RoutePlan? repo = context.FindPath(ship.Location, contract.OriginLocationId);
            if (repo is null)
            {
                return new HaulFeasibility(HaulBlock.PickupUnreachable, ship, 0);
            }

            repoTicks = PriceModel.TravelTicks(repo.DistanceTicks, ship.Class.SpeedMultiplier);
        }

        return new HaulFeasibility(HaulBlock.None, ship, repoTicks);
    }

    /// <summary>Commit a ship to a haul at an agreed fee: lock escrow, reserve the ship, schedule departure.</summary>
    public static HaulAcceptance AcceptHaul(
        SimulationContext context, ActorState player, Contract contract, Ship ship, Credits fee, long repoTicks)
    {
        if (contract.Status != ContractStatus.Open)
        {
            return HaulAcceptance.NotOpen;
        }

        var consumer = context.Actor(contract.PostedBy);

        if (contract.UsesEscrow)
        {
            Credits tax = Credits.Of(fee.Amount * contract.TaxRate);
            Credits outlay = contract.GoodsCost + fee + tax;
            if (consumer.Credits.Amount < outlay.Amount)
            {
                return HaulAcceptance.PosterCannotFund;
            }
        }
        else if (player.Credits.Amount < contract.GoodsCost.Amount)
        {
            return HaulAcceptance.CannotFrontGoods;
        }

        contract.BeginNegotiation();
        contract.Activate(player.Id, ship.Id, fee);

        if (contract.UsesEscrow)
        {
            Credits tax = Credits.Of(fee.Amount * contract.TaxRate);
            context.LockToEscrow(contract, consumer, contract.GoodsCost + fee + tax);
            Credits bond = Credits.Of(Math.Min(contract.PerformanceBond.Amount, player.Credits.Amount));
            context.LockToEscrow(contract, player, bond);
            contract.SetLockedBond(bond);
        }

        ship.Reserve(context.CurrentTick, contract.Id, DecisionEngine.RepositioningCourse(context, ship, contract, repoTicks));
        context.Schedule(new ShipDeparted(context.CurrentTick + repoTicks, contract.Id));
        context.Record(new HaulTakenEntry(context.CurrentTick, contract.Id, player.Id, fee));
        return HaulAcceptance.Accepted;
    }
}
