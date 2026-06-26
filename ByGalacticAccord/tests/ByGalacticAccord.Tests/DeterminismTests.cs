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
    public void Same_seed_reproduces_identical_state(long seed)
    {
        SimulationContext a = TestWorld.RunHeadless(seed, 150_000);
        SimulationContext b = TestWorld.RunHeadless(seed, 150_000);

        Assert.Equal(TestWorld.Signature(a), TestWorld.Signature(b));
    }

    [Fact]
    public void Replaying_in_steps_matches_one_long_run()
    {
        const long seed = 20260619L;
        SimulationContext whole = TestWorld.RunHeadless(seed, 120_000);

        SimulationContext stepped = WorldGenerator.Generate(seed, withPlayer: false).Context;
        for (long t = 5_000; t <= 120_000; t += 5_000)
        {
            stepped.RunTo(t);
        }

        Assert.Equal(TestWorld.Signature(whole), TestWorld.Signature(stepped));
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        SimulationContext a = TestWorld.RunHeadless(1L, 150_000);
        SimulationContext b = TestWorld.RunHeadless(2L, 150_000);

        Assert.NotEqual(TestWorld.Signature(a), TestWorld.Signature(b));
    }
}
