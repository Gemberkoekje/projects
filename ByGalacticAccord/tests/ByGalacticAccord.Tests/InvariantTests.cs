using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The §5.6 simulation invariants, turned into property-style tests over several seeds and horizons.
/// These are the things that must NEVER break, or the emergent economy is untrustworthy.
/// </summary>
public sealed class InvariantTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(20260619L)]
    [InlineData(42L)]
    [InlineData(99999L)]
    public void Invariants_hold_throughout_a_run(long seed) =>
        AssertInvariantsHold(seed, TestWorld.FastTicks, checkpointEvery: 1_000);

    [Theory]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    [InlineData(1L)]
    [InlineData(20260619L)]
    [InlineData(42L)]
    [InlineData(99999L)]
    public void Invariants_hold_throughout_a_million_tick_run(long seed) =>
        AssertInvariantsHold(seed, 1_000_000, checkpointEvery: 10_000);

    [Theory]
    [InlineData(7L)]
    [InlineData(20260619L)]
    public void Credit_supply_is_exactly_conserved(long seed) =>
        AssertCreditSupplyConserved(TestWorld.RunHeadless(seed, TestWorld.FastTicks));

    [Theory]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    [InlineData(7L)]
    [InlineData(20260619L)]
    public void Credit_supply_is_exactly_conserved_over_250k_ticks(long seed) =>
        AssertCreditSupplyConserved(TestWorld.RunHeadless(seed, 250_000));

    [Theory]
    [InlineData(7L)]
    [InlineData(20260619L)]
    public void Escrow_reconciles_and_is_never_negative(long seed) =>
        AssertEscrowReconciles(TestWorld.RunHeadless(seed, TestWorld.FastTicks));

    [Theory]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    [InlineData(7L)]
    [InlineData(20260619L)]
    public void Escrow_reconciles_and_is_never_negative_over_250k_ticks(long seed) =>
        AssertEscrowReconciles(TestWorld.RunHeadless(seed, 250_000));

    /// <summary>
    /// Since S8 a balance may go below ₵0 (design §16.8): only an actor still in business owes anything, a dissolved one holds nothing,
    /// and every insolvent actor was recorded as such.
    /// </summary>
    [Theory]
    [InlineData(7L)]
    [InlineData(20260619L)]
    public void Only_actors_in_business_owe_and_a_dissolved_actor_holds_nothing(long seed) =>
        AssertDebtIsInBusiness(TestWorld.RunHeadless(seed, TestWorld.FastTicks));

    [Theory]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    [InlineData(7L)]
    [InlineData(20260619L)]
    public void Only_actors_in_business_owe_and_a_dissolved_actor_holds_nothing_over_250k_ticks(long seed) =>
        AssertDebtIsInBusiness(TestWorld.RunHeadless(seed, 250_000));

    private static void AssertInvariantsHold(long seed, long ticks, long checkpointEvery)
    {
        SimulationContext context = WorldGenerator.Generate(seed, withPlayer: false).Context;

        for (long checkpoint = checkpointEvery; checkpoint <= ticks; checkpoint += checkpointEvery)
        {
            context.RunTo(checkpoint);
            IReadOnlyList<InvariantViolation> violations = Invariants.Check(context);
            Assert.True(violations.Count == 0, Describe(seed, context.CurrentTick, violations));
        }
    }

    private static void AssertCreditSupplyConserved(SimulationContext context)
    {
        decimal expected = context.InitialCredits.Amount + context.TotalFaucet.Amount - context.TotalSink.Amount + context.DebtWrittenOff.Amount;
        decimal actual = context.TotalCredits().Amount;

        // Credit movements are exact decimal arithmetic; conservation should hold to the cent.
        Assert.Equal(expected, actual, precision: 2);
    }

    // Escrow reconciles exactly (plan S4): what is held is exactly what was locked less what was paid out, no contract ever
    // holds a negative amount, and a contract that has settled, however it ended, holds exactly nothing.
    private static void AssertEscrowReconciles(SimulationContext context)
    {
        decimal expected = context.EscrowLockedTotal.Amount - context.EscrowReleasedTotal.Amount - context.EscrowForfeitedTotal.Amount;
        Assert.Equal(expected, context.TotalEscrowHeld().Amount);

        foreach (Contract c in context.Contracts.Values)
        {
            Assert.True(c.EscrowHeld.Amount >= 0m, $"{c.Id} holds negative escrow {c.EscrowHeld}.");
            Assert.True(c.IsLive || c.EscrowHeld.Amount == 0m, $"{c.Id} is {c.Status} and still holds {c.EscrowHeld.Amount}.");
        }
    }

    private static void AssertDebtIsInBusiness(SimulationContext context)
    {
        foreach (var actor in context.ActorsOrdered)
        {
            if (actor.Solvency == Engine.Economy.SolvencyStatus.Dissolved)
            {
                Assert.True(actor.Credits.Amount == 0m && actor.Ships.Count == 0, $"{actor.Name} is dissolved and still holds {actor.Credits}, {actor.Ships.Count} ships.");
            }

            if (actor.InsolventSince is not null)
            {
                Assert.Contains(context.Insolvencies, i => i.Actor == actor.Id);
            }
        }

        Assert.Empty(Invariants.Check(context));
    }

    private static string Describe(long seed, long tick, IReadOnlyList<InvariantViolation> violations)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("seed ").Append(seed).Append(" tick ").Append(tick)
          .Append(" (").Append(GameTime.FormatDate(tick)).Append("): ");
        foreach (InvariantViolation v in violations)
        {
            sb.Append('[').Append(v.Name).Append("] ").Append(v.Detail).Append(' ');
        }

        return sb.ToString();
    }
}
