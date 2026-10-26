using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>A broken simulation invariant (§5.6).</summary>
public readonly record struct InvariantViolation(string Name, string Detail);

/// <summary>
/// The continuously-checkable invariants from §5.6. An emergent economy is untunable and untrustworthy
/// without these; the soak harness asserts them, and the test suite turns them into property tests.
/// Credit movements are exact decimal arithmetic, so the conservation checks use a tiny epsilon only
/// to guard against accumulated representation noise.
/// </summary>
public static class Invariants
{
    private const decimal Epsilon = 0.001m;

    public static IReadOnlyList<InvariantViolation> Check(SimulationContext context)
    {
        var violations = new List<InvariantViolation>();
        CheckCreditConservation(context, violations);
        CheckEscrow(context, violations);
        CheckGoods(context, violations);
        CheckActiveContracts(context, violations);
        return violations;
    }

    /// <summary>total_credits == initial + Σfaucet − Σsink (§5.6).</summary>
    private static void CheckCreditConservation(SimulationContext context, List<InvariantViolation> violations)
    {
        decimal expected = context.InitialCredits.Amount + context.TotalFaucet.Amount - context.TotalSink.Amount;
        decimal actual = context.TotalCredits().Amount;
        if (Math.Abs(expected - actual) > Epsilon)
        {
            violations.Add(new InvariantViolation(
                "CreditConservation",
                $"expected {expected}, actual {actual} (initial {context.InitialCredits.Amount}, faucet {context.TotalFaucet.Amount}, sink {context.TotalSink.Amount})."));
        }
    }

    /// <summary>Σ escrow_held == Σlocked − Σreleased − Σforfeited; never negative; and a settled contract holds exactly nothing (design §20.2).</summary>
    private static void CheckEscrow(SimulationContext context, List<InvariantViolation> violations)
    {
        decimal expected = context.EscrowLockedTotal.Amount - context.EscrowReleasedTotal.Amount - context.EscrowForfeitedTotal.Amount;
        decimal actual = context.TotalEscrowHeld().Amount;
        if (Math.Abs(expected - actual) > Epsilon)
        {
            violations.Add(new InvariantViolation("EscrowReconciliation", $"expected {expected}, actual {actual}."));
        }

        foreach (Contract c in context.Contracts.Values)
        {
            if (c.EscrowHeld.Amount < -Epsilon)
            {
                violations.Add(new InvariantViolation("EscrowNegative", $"{c.Id} holds {c.EscrowHeld}."));
            }
            else if (!c.IsLive && c.EscrowHeld.Amount != 0m)
            {
                violations.Add(new InvariantViolation("EscrowLeft", $"{c.Id} is {c.Status} and still holds {c.EscrowHeld.Amount}."));
            }
        }
    }

    /// <summary>
    /// Goods conservation (design §20.2): for every good, produced = consumed + held anywhere (stock, storage, holds) +
    /// lost; and no stock, store, reservation or hold is ever negative or set aside beyond what is there.
    /// </summary>
    private static void CheckGoods(SimulationContext context, List<InvariantViolation> violations)
    {
        foreach (GoodType good in Goods.All)
        {
            decimal produced = context.GoodsProduced(good);
            decimal accounted = context.GoodsConsumed(good) + context.GoodsHeld(good) + context.GoodsLost(good);
            if (Math.Abs(produced - accounted) > 0.01m)
            {
                violations.Add(new InvariantViolation(
                    "GoodsConservation",
                    $"{good}: produced {produced}, accounted {accounted} (consumed {context.GoodsConsumed(good)}, held {context.GoodsHeld(good)}, lost {context.GoodsLost(good)})."));
            }
        }

        foreach (ActorState actor in context.Actors.Values)
        {
            foreach (GoodType good in Goods.All)
            {
                if (actor.Stock(good) < 0m || actor.Reserved(good) > actor.Stock(good) + Epsilon)
                {
                    violations.Add(new InvariantViolation("NegativeStock", $"{actor.Name} holds {actor.Stock(good)} {good}, {actor.Reserved(good)} set aside."));
                }
            }
        }

        foreach (Contract contract in context.Contracts.Values)
        {
            if (contract is StorageContract { Status: ContractStatus.Active } storage)
            {
                foreach (GoodType good in Goods.All)
                {
                    if (storage.Held(good) < 0m || storage.ReservedOf(good) > storage.Held(good) + Epsilon)
                    {
                        violations.Add(new InvariantViolation("NegativeStock", $"{storage.Id} holds {storage.Held(good)} {good}, {storage.ReservedOf(good)} set aside."));
                    }
                }
            }
        }

        foreach (Ship ship in context.Ships.Values)
        {
            if (ship.Hold.Any(l => l.Quantity < 0m) || ship.HoldQuantity > ship.CargoCapacity + Epsilon)
            {
                violations.Add(new InvariantViolation("Hold", $"{ship.Id} carries {ship.HoldQuantity} in a hold of {ship.CargoCapacity}."));
            }
        }
    }

    /// <summary>
    /// No orphan contracts (design §20.2): every signed Haul is flown by a ship whose job it is, and every signed Sale
    /// is being performed or has a default check coming (its handover waits on goods that are set aside or on their way).
    /// </summary>
    private static void CheckActiveContracts(SimulationContext context, List<InvariantViolation> violations)
    {
        foreach (Contract c in context.Contracts.Values)
        {
            if (c.Status != ContractStatus.Active)
            {
                continue;
            }

            switch (c)
            {
                case HaulContract haul when haul.AssignedShip is not { } shipId:
                    violations.Add(new InvariantViolation("OrphanContract", $"{haul.Id} is signed with no ship."));
                    break;
                case HaulContract haul when context.Ship(haul.AssignedShip!.Value).Job?.Haul != haul.Id:
                    violations.Add(new InvariantViolation("OrphanContract", $"{haul.Id} is signed but its ship is not flying it."));
                    break;
                case SaleContract sale when sale.Reserved is null && !Market.UnderWay(context, sale):
                    violations.Add(new InvariantViolation("OrphanContract", $"{sale.Id} is signed with no goods set aside and nothing on its way."));
                    break;
            }
        }
    }
}
