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
    public void Invariants_hold_throughout_a_long_run(long seed)
    {
        SimulationContext context = WorldGenerator.Generate(seed, withPlayer: false).Context;

        for (long checkpoint = 10_000; checkpoint <= 300_000; checkpoint += 10_000)
        {
            context.RunTo(checkpoint);
            IReadOnlyList<InvariantViolation> violations = Invariants.Check(context);
            Assert.True(violations.Count == 0, Describe(seed, context.CurrentTick, violations));
        }
    }

    [Theory]
    [InlineData(7L)]
    [InlineData(20260619L)]
    public void Credit_supply_is_exactly_conserved(long seed)
    {
        SimulationContext context = TestWorld.RunHeadless(seed, 250_000);

        decimal expected = context.InitialCredits.Amount + context.TotalFaucet.Amount - context.TotalSink.Amount;
        decimal actual = context.TotalCredits().Amount;

        // Credit movements are exact decimal arithmetic; conservation should hold to the cent.
        Assert.Equal(expected, actual, precision: 2);
    }

    [Theory]
    [InlineData(7L)]
    [InlineData(20260619L)]
    public void Escrow_reconciles_and_is_never_negative(long seed)
    {
        SimulationContext context = TestWorld.RunHeadless(seed, 250_000);

        decimal expected = context.EscrowLockedTotal.Amount - context.EscrowReleasedTotal.Amount - context.EscrowForfeitedTotal.Amount;
        Assert.Equal(expected, context.TotalEscrowHeld().Amount, precision: 2);

        foreach (Contract c in context.Contracts.Values)
        {
            Assert.True(c.EscrowHeld.Amount >= 0m, $"{c.Id} holds negative escrow {c.EscrowHeld}.");
        }
    }

    [Theory]
    [InlineData(7L)]
    [InlineData(20260619L)]
    public void No_actor_ever_goes_into_negative_credits(long seed)
    {
        SimulationContext context = TestWorld.RunHeadless(seed, 250_000);
        foreach (var actor in context.ActorsOrdered)
        {
            Assert.True(actor.Credits.Amount >= 0m, $"{actor.Name} is at {actor.Credits}.");
        }
    }

    private static string Describe(long seed, long tick, IReadOnlyList<InvariantViolation> violations)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("seed ").Append(seed).Append(" tick ").Append(tick).Append(": ");
        foreach (InvariantViolation v in violations)
        {
            sb.Append('[').Append(v.Name).Append("] ").Append(v.Detail).Append(' ');
        }

        return sb.ToString();
    }
}
