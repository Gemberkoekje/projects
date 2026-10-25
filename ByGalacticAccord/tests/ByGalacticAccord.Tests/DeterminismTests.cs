using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// Deterministic replay (§5.5) — the property event sourcing exists to give you. The same seed must
/// reproduce identical state; a different seed must (almost surely) diverge.
/// </summary>
public sealed class DeterminismTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(20260619L)]
    [InlineData(123456L)]
    public void Same_seed_reproduces_identical_state(long seed) =>
        AssertSameSeedReproduces(seed, TestWorld.FastTicks);

    [Theory]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    [InlineData(1L)]
    [InlineData(20260619L)]
    [InlineData(123456L)]
    public void Same_seed_reproduces_identical_state_over_150k_ticks(long seed) =>
        AssertSameSeedReproduces(seed, 150_000);

    [Fact]
    public void Replaying_in_steps_matches_one_long_run() =>
        AssertSteppedReplayMatches(TestWorld.FastTicks, 1_000);

    [Fact]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    public void Replaying_in_steps_matches_one_long_run_over_120k_ticks() =>
        AssertSteppedReplayMatches(120_000, 5_000);

    [Fact]
    public void Different_seeds_diverge()
    {
        SimulationContext a = TestWorld.RunHeadless(1L, TestWorld.FastTicks);
        SimulationContext b = TestWorld.RunHeadless(2L, TestWorld.FastTicks);

        Assert.NotEqual(TestWorld.Signature(a), TestWorld.Signature(b));
    }

    private static void AssertSameSeedReproduces(long seed, long ticks)
    {
        SimulationContext a = TestWorld.RunHeadless(seed, ticks);
        SimulationContext b = TestWorld.RunHeadless(seed, ticks);

        Assert.Equal(TestWorld.Signature(a), TestWorld.Signature(b));
    }

    private static void AssertSteppedReplayMatches(long ticks, long step)
    {
        const long seed = 20260619L;
        SimulationContext whole = TestWorld.RunHeadless(seed, ticks);

        SimulationContext stepped = WorldGenerator.Generate(seed, withPlayer: false).Context;
        for (long t = step; t <= ticks; t += step)
        {
            stepped.RunTo(t);
        }

        Assert.Equal(TestWorld.Signature(whole), TestWorld.Signature(stepped));
    }
}
