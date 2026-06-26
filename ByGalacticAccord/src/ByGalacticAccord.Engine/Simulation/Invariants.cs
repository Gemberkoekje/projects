using System;
using System.Collections.Generic;
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
        CheckCargo(context, violations);
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

    /// <summary>Σ escrow_held == Σlocked − Σreleased − Σforfeited, and never negative (§5.6).</summary>
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
        }
    }

    /// <summary>Goods removed from origins == delivered + lost + still aboard in-transit ships (§5.6).</summary>
    private static void CheckCargo(SimulationContext context, List<InvariantViolation> violations)
    {
        decimal aboard = 0m;
        foreach (Ship ship in context.Ships.Values)
        {
            if (ship.Status == ShipStatus.InTransit && ship.Manifest is { } m)
            {
                aboard += m.Quantity;
            }
        }

        decimal accountedFor = context.CargoDelivered + context.CargoLost + aboard;
        if (Math.Abs(context.CargoPickedUp - accountedFor) > 0.01m)
        {
            violations.Add(new InvariantViolation(
                "CargoConservation",
                $"picked up {context.CargoPickedUp}, accounted {accountedFor} (delivered {context.CargoDelivered}, lost {context.CargoLost}, aboard {aboard})."));
        }
    }

    /// <summary>No orphan contracts: every Active contract is assigned to an in-transit ship that will arrive (§5.6).</summary>
    private static void CheckActiveContracts(SimulationContext context, List<InvariantViolation> violations)
    {
        foreach (Contract c in context.Contracts.Values)
        {
            if (c.Status != ContractStatus.Active)
            {
                continue;
            }

            if (c.AssignedShip is null)
            {
                violations.Add(new InvariantViolation("OrphanContract", $"{c.Id} is Active with no assigned ship."));
                continue;
            }

            Ship ship = context.Ship(c.AssignedShip.Value);
            if (ship.Status != ShipStatus.InTransit || ship.ActiveContract != c.Id)
            {
                violations.Add(new InvariantViolation("OrphanContract", $"{c.Id} is Active but ship {ship.Id} is not carrying it."));
            }
        }
    }
}
