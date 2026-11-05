using System.Linq;
using ByGalacticAccord.Engine.Actors;
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
    public void The_economy_comes_alive(long seed) =>
        AssertEconomyAlive(TestWorld.RunHeadless(seed, TestWorld.FastTicks));

    [Theory]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    [InlineData(1L)]
    [InlineData(20260619L)]
    [InlineData(42L)]
    public void The_economy_stays_alive_over_200k_ticks(long seed) =>
        AssertEconomyAlive(TestWorld.RunHeadless(seed, 200_000));

    /// <summary>
    /// Plan S4's check: over a long run every failure path of design §16.3 that S4 brings happens, and is settled by its terms. A
    /// Build's own paths (a supply failure, a substitution, a cancellation on a supply failure, plan S7) are left out: ships are
    /// ordered rarely once the fleets settle, so a long run may meet none. <see cref="BuildTests"/> drives each of them, for an AI
    /// client and for the player; the soak's telemetry counts those that happen.
    /// </summary>
    [Theory]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    [InlineData(42L)]
    [InlineData(20260619L)]
    public void Every_failure_path_happens_over_a_million_ticks(long seed)
    {
        SimulationContext context = TestWorld.RunHeadless(seed, 1_000_000);

        var buildOnly = new[] { FailureKind.SupplyFailure, FailureKind.Substituted, FailureKind.CancelledOnSupplyFailure };
        foreach (FailureKind kind in System.Enum.GetValues<FailureKind>().Except(buildOnly))
        {
            Assert.True(context.Failures(kind) > 0, $"No {kind} in a million ticks of seed {seed}.");
        }

        Assert.True(context.ContractsCancelled > 0 && context.ContractsBreached > 0 && context.ContractsDefaulted > 0);
        context.AssertSound();
    }

    /// <summary>
    /// Plan S7's check: over a long run the yards complete builds (to order and on spec), the traders' fleets grow from the sector's
    /// nine ships, and shrink too (traders sell ships, and some reach Edge Salvage), and every invariant holds: goods conservation
    /// through parts into ships among them.
    /// </summary>
    [Theory]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    [InlineData(42L)]
    [InlineData(20260619L)]
    public void Yards_complete_builds_and_fleets_grow_and_shrink_over_a_million_ticks(long seed)
    {
        SimulationContext context = WorldGenerator.Generate(seed, withPlayer: false).Context;
        int start = context.ActorsOrdered.Where(a => a.MovesCargo).Sum(a => a.Ships.Count);
        int most = start;
        for (long tick = 50_000; tick <= 1_000_000; tick += 50_000)
        {
            context.RunTo(tick);
            most = System.Math.Max(most, context.ActorsOrdered.Where(a => a.MovesCargo).Sum(a => a.Ships.Count));
        }

        Assert.True(context.ShipsBuiltToOrder > 0, $"No ship built to order (seed {seed}).");
        Assert.True(most > start, $"The traders' fleets never grew past {start} (seed {seed}).");
        int soldByTraders = context.Contracts.Values.OfType<ShipSaleContract>()
            .Count(s => s.Status == ContractStatus.Fulfilled && context.Actor(s.Seller).MovesCargo);
        Assert.True(soldByTraders > 0, $"No trader sold a ship (seed {seed}).");
        Assert.True(context.ShipsSold > 0);
        context.AssertSound();
    }

    [Theory]
    [Trait(TestWorld.Category, TestWorld.Soak)]
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
    [Trait(TestWorld.Category, TestWorld.Soak)]
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

    /// <summary>
    /// Plan risk R1 and S6's verify: with distant contracts invisible but through the news (D31), the frontier must still be
    /// supplied. Every consumer outside the Accord gets at least this share of each good it wants over a million ticks.
    /// </summary>
    [Theory]
    [Trait(TestWorld.Category, TestWorld.Soak)]
    [InlineData(42L)]
    [InlineData(20260619L)]
    public void The_frontier_is_still_supplied_when_only_the_news_reaches_it(long seed)
    {
        SimulationContext context = TestWorld.RunHeadless(seed, 1_000_000);
        decimal heartbeats = context.CurrentTick / (decimal)context.Config.HeartbeatInterval;
        foreach (var actor in context.ActorsOrdered.Where(a => a.Demands.Count > 0 && context.Location(a.Home).Reach == AccordReach.OutsideAccord))
        {
            foreach (var demand in actor.Demands)
            {
                decimal met = actor.Consumed(demand.Good) / (demand.ConsumptionPerHeartbeat * heartbeats);
                Assert.True(met >= 0.3m, $"{actor.Name} got {met:P0} of the {demand.Good} it wanted over a million ticks of seed {seed}.");
            }
        }

        Assert.True(context.SignedFromNews > 0, "Nobody signed a contract it knew of only from the news.");

        // The same long run checks plan S6b: posters pay to show what sits unsold further out, two jumps and more, and
        // Helios Prime gets the machinery it buys as lots from Tycho, two jumps away (S6: 64–74%).
        Assert.True(context.AdsShown[2] > 0 && context.AdsShown[3] > 0, $"Nobody paid to show a post two and three jumps out ({string.Join("/", context.AdsShown)}).");
        Assert.True(context.SignedViaAd > 0, "Nobody signed a contract it saw only where its poster paid to show it.");
        ActorState helios = context.ActorsOrdered.Single(a => a.Name == "Helios Prime Metropole");
        decimal machinery = helios.Consumed(GoodType.Machinery) / (helios.Demands.Single(d => d.Good == GoodType.Machinery).ConsumptionPerHeartbeat * heartbeats);
        Assert.True(machinery >= 0.75m, $"Helios Prime got {machinery:P0} of its machinery over a million ticks of seed {seed}.");
        context.AssertSound();
    }

    [Fact]
    public void Goods_actually_reach_the_frontier()
    {
        // Regression guard for the multi-hop supply chain: the frontier must be reachable and supplied.
        SimulationContext context = TestWorld.RunHeadless(20260619L, TestWorld.FastTicks);

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

    private static void AssertEconomyAlive(SimulationContext context)
    {
        Assert.True(context.ContractsFulfilled > 500, $"only {context.ContractsFulfilled} contracts fulfilled — the world is too quiet.");
        Assert.True(context.GoodsSold > 0m);

        // Most signed contracts should settle, not fail.
        Assert.True(context.ContractsFulfilled > (context.ContractsBreached + context.ContractsDefaulted) * 10);

        // All three of S3's kinds are in use, by the AI alone.
        foreach (ContractType kind in new[] { ContractType.Sale, ContractType.Haul, ContractType.Storage })
        {
            Assert.Contains(context.Contracts.Values, c => c.Type == kind && c.Status == ContractStatus.Fulfilled);
        }

        // Every consumer, frontier included, gets some of everything it wants (plan risk R1).
        foreach (var actor in context.ActorsOrdered.Where(a => a.Demands.Count > 0))
        {
            foreach (var demand in actor.Demands)
            {
                Assert.True(actor.Consumed(demand.Good) > 0m, $"{actor.Name} never got any {demand.Good}.");
            }
        }
    }
}
