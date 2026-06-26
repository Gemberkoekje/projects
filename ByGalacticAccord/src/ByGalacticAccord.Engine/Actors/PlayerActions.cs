using System;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Events;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>
/// Player-initiated contract actions, shared by every front-end (console + GUI) so the player commits
/// to a haul through exactly the same path as the simulation uses elsewhere. The player is just an
/// actor (§4.7.3); the only difference is a human picks the fee instead of the decision engine.
/// </summary>
public static class PlayerActions
{
    public sealed record HaulFeasibility(bool Ok, string Reason, Ship? Ship, long RepoTicks);

    /// <summary>Can the player take this haul right now? Returns the ship that would fly it and any repositioning time.</summary>
    public static HaulFeasibility CheckHaul(SimulationContext context, ActorState player, Contract contract)
    {
        if (contract.Type != ContractType.Haul || contract.Status != ContractStatus.Open)
        {
            return new HaulFeasibility(false, "That contract isn't open for haulage.", null, 0);
        }

        Ship? ship = player.Ships.Select(context.Ship).FirstOrDefault(s => s.Status == ShipStatus.Docked);
        if (ship is null)
        {
            return new HaulFeasibility(false, "All your ships are committed.", null, 0);
        }

        if (contract.Quantity > ship.CargoCapacity)
        {
            return new HaulFeasibility(false, $"Your {ship.Class.Name} holds {ship.CargoCapacity:0}; this haul is {contract.Quantity:0}.", null, 0);
        }

        var producer = context.ProducerAt(contract.OriginLocationId, contract.Good);
        if (producer is null || producer.Stock(contract.Good) < contract.Quantity)
        {
            return new HaulFeasibility(false, "The origin can't supply that quantity right now.", null, 0);
        }

        if (context.FindPath(contract.OriginLocationId, contract.DestinationLocationId) is null)
        {
            return new HaulFeasibility(false, "There's no navigable route for this haul.", null, 0);
        }

        long repoTicks = 0;
        if (ship.Location != contract.OriginLocationId)
        {
            RoutePlan? repo = context.FindPath(ship.Location, contract.OriginLocationId);
            if (repo is null)
            {
                return new HaulFeasibility(false, "Your ship can't reach the pickup.", null, 0);
            }

            repoTicks = PriceModel.TravelTicks(repo.DistanceTicks, ship.Class.SpeedMultiplier);
        }

        return new HaulFeasibility(true, "ok", ship, repoTicks);
    }

    /// <summary>Commit a ship to a haul at an agreed fee: lock escrow, reserve the ship, schedule departure.</summary>
    public static (bool Ok, string Message) AcceptHaul(
        SimulationContext context, ActorState player, Contract contract, Ship ship, Credits fee, long repoTicks)
    {
        var consumer = context.Actor(contract.PostedBy);

        if (contract.UsesEscrow)
        {
            Credits tax = Credits.Of(fee.Amount * contract.TaxRate);
            Credits outlay = contract.GoodsCost + fee + tax;
            if (consumer.Credits.Amount < outlay.Amount)
            {
                return (false, "The poster can no longer fund the escrow. Deal collapses.");
            }
        }
        else if (player.Credits.Amount < contract.GoodsCost.Amount)
        {
            return (false, $"Frontier deal: you must front the {contract.GoodsCost} goods cost, and you're short.");
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

        ship.Reserve(context.CurrentTick, contract.Id);
        context.Schedule(new ShipDeparted(context.CurrentTick + repoTicks, contract.Id));
        context.Record("Accepted", $"{contract.Id}: {player.Name} took the {contract.Good} haul for {fee}.");
        return (true, $"Deal. Fee {fee}. Departing in {repoTicks} tick(s).");
    }
}
