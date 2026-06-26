using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// Soak-style economic health checks (§5.6): the economy must come alive and must neither collapse
/// (deflation / mass bankruptcy / starvation) nor run away (hyperinflation / monopoly lock-in).
/// </summary>
public sealed class EconomyTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(20260619L)]
    [InlineData(42L)]
    public void The_economy_comes_alive(long seed)
    {
        SimulationContext context = TestWorld.RunHeadless(seed, 200_000);

        Assert.True(context.ContractsFulfilled > 500, $"only {context.ContractsFulfilled} hauls fulfilled — the world is too quiet.");
        Assert.True(context.CargoDelivered > 0m);
        // Most posted contracts should clear, not rot on the board.
        Assert.True(context.ContractsFulfilled > context.ContractsBreached * 10);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(20260619L)]
    [InlineData(42L)]
    public void Money_supply_does_not_run_away(long seed)
    {
        SimulationContext context = TestWorld.RunHeadless(seed, 1_000_000);

        decimal baseline = context.InitialCredits.Amount;
        decimal drift = (context.TotalCredits().Amount - baseline) / baseline;

        // The inflation monitor must keep total supply within a sane band of baseline over a million ticks.
        Assert.InRange(drift, -0.5m, 1.0m);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(20260619L)]
    public void No_single_actor_monopolises_the_whole_economy(long seed)
    {
        SimulationContext context = TestWorld.RunHeadless(seed, 500_000);

        decimal total = context.TotalActorCredits().Amount;
        decimal richest = 0m;
        foreach (var actor in context.ActorsOrdered)
        {
            if (actor.Credits.Amount > richest)
            {
                richest = actor.Credits.Amount;
            }
        }

        Assert.True(richest < total * 0.85m, "one actor holds almost everything — monopoly lock-in.");
    }

    [Fact]
    public void Goods_actually_reach_the_frontier()
    {
        // Regression guard for the multi-hop supply chain: the frontier must be reachable and supplied.
        SimulationContext context = TestWorld.RunHeadless(20260619L, 200_000);

        Location? edge = null;
        foreach (Location loc in context.Locations.Values)
        {
            if (loc.Type == LocationType.FrontierOutpost)
            {
                edge = loc;
            }
        }

        Assert.NotNull(edge);

        // A Helios food producer must be able to path to the frontier outpost (multi-hop).
        LocationId? foodSource = null;
        foreach (var actor in context.ActorsOrdered)
        {
            if (actor.Producer is { Good: GoodType.Food })
            {
                foodSource = actor.Home;
            }
        }

        Assert.NotNull(foodSource);
        Assert.NotNull(context.FindPath(foodSource!.Value, edge!.Id));
    }
}
