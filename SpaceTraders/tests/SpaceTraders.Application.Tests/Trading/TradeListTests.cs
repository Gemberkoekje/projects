using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Slice 6.63 (D148), asked on 2026-11-13: "As soon as the gate is built [...] Have a pre-set (asynchronously generated) list of
/// high value trades. [...] A ship can then take a close high value trade to do." The list picks which trades a ship looks at;
/// what each earns the ship is worked out as before.
/// </summary>
public sealed class TradeListTests
{
    private const long Credits = 400_000;
    private const int MinProfitPerUnit = 40;

    private static readonly IReadOnlySet<string> NoneHeld = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AShipsRoutesFromTheList_AreItsRoutesOfEveryRoute_ThatTheListHolds_ToTheCredit()
    {
        var list = TradeListBuilder.Select(TradeListBuilder.Estimate(GeneratedWorld.Map(), 490, TripTime.DefaultEngineSpeed, MinProfitPerUnit), GeneratedWorld.Now, perSystem: 3, total: 10, Systems());
        var listed = list.Entries.Select(Key).ToHashSet(StringComparer.Ordinal);
        list.Entries.Should().NotBeEmpty();

        foreach (var ship in GeneratedWorld.Ships())
        {
            var every = TradeRoutePlanner.Rank(GeneratedWorld.Map(), ship, Credits, MinProfitPerUnit, NoneHeld, HeldBuys.None);
            var fromList = TradeRoutePlanner.Rank(GeneratedWorld.Map().WithTradeList(list), ship, Credits, MinProfitPerUnit, NoneHeld, HeldBuys.None);

            fromList.Should().BeEquivalentTo(every.Where(route => listed.Contains(route.Key)), options => options.WithStrictOrdering(), ship.Symbol);
        }
    }

    [Fact]
    public void WithAList_OnlyItsTradesAreJudged()
    {
        var list = TradeListBuilder.Select(TradeListBuilder.Estimate(GeneratedWorld.Map(), 490, TripTime.DefaultEngineSpeed, MinProfitPerUnit), GeneratedWorld.Now, perSystem: 2, total: 0, Systems());
        var listed = list.Entries.Select(Key).ToHashSet(StringComparer.Ordinal);

        var judgements = TradeRoutePlanner.Judge(GeneratedWorld.Map().WithTradeList(list), GeneratedWorld.Ships()[0], Credits, MinProfitPerUnit, NoneHeld, HeldBuys.None);

        judgements.Should().NotBeEmpty().And.OnlyContain(judgement => listed.Contains(judgement.Route.Key));
    }

    [Fact]
    public void WithoutAList_EveryRouteIsWorkedOut_AsBeforeTheGate()
    {
        var ship = GeneratedWorld.Ships()[0];

        var none = TradeRoutePlanner.Rank(GeneratedWorld.Map().WithTradeList(TradeList.None), ship, Credits, MinProfitPerUnit, NoneHeld, HeldBuys.None);

        none.Should().BeEquivalentTo(TradeRoutePlanner.Rank(GeneratedWorld.Map(), ship, Credits, MinProfitPerUnit, NoneHeld, HeldBuys.None), options => options.WithStrictOrdering());
    }

    [Fact]
    public void AMapWithoutJumps_WorksOutEveryRouteInItsSystem_WithoutTheList()
    {
        // A trader that doesn't cross systems, or a gatherer trading in its spare time, trades within its system: there every
        // route is worked out with each market's fuel price (D148), and a system's listed trades mostly leave it.
        var list = TradeListBuilder.Select(TradeListBuilder.Estimate(GeneratedWorld.Map(), 490, TripTime.DefaultEngineSpeed, MinProfitPerUnit), GeneratedWorld.Now, perSystem: 1, total: 0, Systems());
        var ship = GeneratedWorld.Ships()[1];

        var inSystem = GeneratedWorld.Map().WithTradeList(list).WithoutJumps();

        inSystem.TradeList.Should().BeSameAs(TradeList.None);
        TradeRoutePlanner.Rank(inSystem, ship, Credits, MinProfitPerUnit, NoneHeld, HeldBuys.None)
            .Should().BeEquivalentTo(TradeRoutePlanner.Rank(GeneratedWorld.Map().WithoutJumps(), ship, Credits, MinProfitPerUnit, NoneHeld, HeldBuys.None), options => options.WithStrictOrdering());
    }

    [Fact]
    public void TheEstimates_KeepTheRulesARouteKeeps()
    {
        // Only markets recent or predicted (D96, D132); no gate material bought in the gate's system (D128); a hold at most.
        var map = GeneratedWorld.Map();

        var estimates = TradeListBuilder.Estimate(map, 80, TripTime.DefaultEngineSpeed, MinProfitPerUnit);

        estimates.Should().NotBeEmpty();
        estimates.Should().OnlyContain(entry => map.IsFresh(entry.BuyWaypointSymbol) && map.IsFresh(entry.SellWaypointSymbol));
        estimates.Should().NotContain(entry => map.IsConstructionMaterialAt(entry.BuyWaypointSymbol, entry.TradeSymbol));
        estimates.Should().OnlyContain(entry => entry.Units > 0 && entry.Units <= 80);
        estimates.Where(entry => entry.Jumps > 0).Should().NotBeEmpty("the world's systems are joined by gates");
    }

    [Fact]
    public void AnEstimate_FillsTheHoldBatchByBatch_WhileEachUnitEarnsTheMinimum()
    {
        // D79: a batch of each market's trade volume shares its prices; the next is bought a step dearer and sold a step cheaper.
        // No market sells fuel here, so the haul's fuel costs nothing.
        TradeGoodSnapshot atBuy = new("IRON", "EXPORT", 100, 90, 10, "HIGH");
        TradeGoodSnapshot atSell = new("IRON", "IMPORT", 160, 150, 12, "SCARCE");
        var map = TwoMarkets(atBuy, atSell);

        var estimate = TradeListBuilder.Estimate(map, 25, TripTime.DefaultEngineSpeed, 1).Should().ContainSingle().Subject;

        var margin = 0.0;
        for (var unit = 0; unit < 25; unit++)
        {
            margin += PriceSteps.SalePriceOf(150, unit, 12) - PriceSteps.PurchasePriceOf(100, unit, 10);
        }

        estimate.Units.Should().Be(25);
        estimate.Profit.Should().Be((long)Math.Floor(margin + 1e-6));
        estimate.Jumps.Should().Be(0);
        estimate.Seconds.Should().BePositive();
    }

    [Fact]
    public void TheList_KeepsEachSystemsBest_AndTheBestOfAll_BestFirst()
    {
        TradeListEntry[] estimates =
        [
            Entry("X1-AA-1", "IRON", profit: 9_000),
            Entry("X1-AA-1", "COPPER", profit: 8_000),
            Entry("X1-AA-2", "GOLD", profit: 7_000),
            Entry("X1-BB-1", "IRON", profit: 1_000),
            Entry("X1-BB-1", "COPPER", profit: 500),
            Entry("X1-CC-1", "IRON", profit: -50),
        ];

        var list = TradeListBuilder.Select(estimates, GeneratedWorld.Now, perSystem: 1, total: 2, ["X1-AA", "X1-BB", "X1-CC", "X1-ZZ"]);

        list.Entries.Select(entry => entry.TradeSymbol + "@" + entry.BuyWaypointSymbol)
            .Should().Equal("IRON@X1-AA-1", "COPPER@X1-AA-1", "IRON@X1-BB-1");
        list.BuiltAt.Should().Be(GeneratedWorld.Now);
        list.SellsFor("X1-AA-1", "COPPER").Should().Equal("X1-ZZ-9");
        list.HasGoodAt("X1-AA-2", "GOLD").Should().BeFalse("neither its system's best nor among the best of all");
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(31, false)]
    public void TheStore_HandsOutAListWhileItIsRecentEnough(int minutesOld, bool handed)
    {
        var store = new TradeListStore();
        var list = new TradeList(GeneratedWorld.Now.AddMinutes(-minutesOld), [Entry("X1-AA-1", "IRON", profit: 9_000)], ["X1-AA"]);
        store.FreshAt(GeneratedWorld.Now, TimeSpan.FromMinutes(30)).Should().BeSameAs(TradeList.None, "none was built yet");

        store.Set(list);

        store.FreshAt(GeneratedWorld.Now, TimeSpan.FromMinutes(30)).Should().BeSameAs(handed ? list : TradeList.None);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(null, false, true)]
    [InlineData(null, true, false)]
    public async Task TheHomeGate_IsBuilt_WhenItsSiteIsComplete_OrWithoutASiteWhenNotUnderConstruction(bool? siteComplete, bool underConstruction, bool built)
    {
        var (gate, _) = HomeGateWith(siteComplete, underConstruction);

        (await gate.IsBuiltAsync(CancellationToken.None)).Should().Be(built);
    }

    [Fact]
    public async Task TheWorker_KeepsNoList_WhileTheHomeGateIsNotBuilt()
    {
        // After a reset the new agent's gate isn't built: the old agent's list goes, so its markets aren't traded by.
        var store = new TradeListStore();
        store.Set(new TradeList(DateTimeOffset.UtcNow, [Entry("X1-AA-1", "IRON", profit: 9_000)], ["X1-AA"]));
        using var services = Services(gateBuilt: false, out _);

        using var worker = new TradeListService(services.GetRequiredService<IServiceScopeFactory>(), Leader(), store, NullLogger<TradeListService>.Instance);
        await worker.BuildAsync(CancellationToken.None);

        store.Latest.Should().BeSameAs(TradeList.None);
    }

    [Fact]
    public async Task TheWorker_BuildsTheList_ForTheFleetsLargestHold_OnceTheHomeGateIsBuilt()
    {
        var store = new TradeListStore();
        using var services = Services(gateBuilt: true, out var reader);

        using var worker = new TradeListService(services.GetRequiredService<IServiceScopeFactory>(), Leader(), store, NullLogger<TradeListService>.Instance);
        var wait = await worker.BuildAsync(CancellationToken.None);

        wait.Should().Be(TimeSpan.FromSeconds(TradeListService.DefaultRebuildSeconds));
        store.Latest.IsNone.Should().BeFalse();
        store.Latest.Entries.Should().NotBeEmpty().And.OnlyContain(entry => entry.Units <= 490);
        store.Latest.Entries.Should().Contain(entry => entry.Units > 80, "the largest hold, 490, sets the units up");
        await reader.Received(1).ReadReachAsync("X1-S0", Arg.Any<CancellationToken>());
    }

    private static IEnumerable<string> Systems() => GeneratedWorld.Map().Waypoints.Select(waypoint => waypoint.SystemSymbol).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string Key(TradeListEntry entry) => TradeRoutePlanner.RouteKey(entry.TradeSymbol, entry.BuyWaypointSymbol, entry.SellWaypointSymbol);

    private static TradeListEntry Entry(string buy, string good, long profit)
        => new(good, buy, "X1-ZZ-9", 40, profit, 600, 0);

    private static TradeMarketMap TwoMarkets(TradeGoodSnapshot atBuy, TradeGoodSnapshot atSell)
        => new(
            [
                new WaypointCacheModel("X1-AA-1", "X1-AA", "PLANET", 0, 0, true, false, DateTimeOffset.UnixEpoch),
                new WaypointCacheModel("X1-AA-2", "X1-AA", "MOON", 30, 40, true, false, DateTimeOffset.UnixEpoch),
            ],
            [
                new MarketSnapshot("X1-AA-1", "X1-AA", [atBuy], [], ["IRON"], []),
                new MarketSnapshot("X1-AA-2", "X1-AA", [atSell], ["IRON"], [], []),
            ],
            new Dictionary<string, IReadOnlyList<string>>());

    private static ILeaderElection Leader()
    {
        var leader = Substitute.For<ILeaderElection>();
        leader.IsLeader.Returns(true);
        return leader;
    }

    private static (HomeGate Gate, IConstructionSites Sites) HomeGateWith(bool? siteComplete, bool underConstruction)
    {
        var sites = Substitute.For<IConstructionSites>();
        sites.HomeSystemAsync(Arg.Any<CancellationToken>()).Returns("X1-S0");
        var waypoints = Substitute.For<IWaypointRepository>();
        waypoints.GetBySystemAsync("X1-S0", Arg.Any<CancellationToken>()).Returns(
        [
            new WaypointCacheModel("X1-S0-W01", "X1-S0", "PLANET", 0, 0, true, false, DateTimeOffset.UnixEpoch),
            new WaypointCacheModel("X1-S0-G", "X1-S0", "JUMP_GATE", 10, 10, true, false, DateTimeOffset.UnixEpoch, IsUnderConstruction: underConstruction),
        ]);
        var constructions = Substitute.For<IConstructionRepository>();
        constructions.FindAsync("X1-S0-G", Arg.Any<CancellationToken>())
            .Returns(siteComplete is { } complete ? new ConstructionSiteModel("X1-S0-G", complete, []) : null);
        return (new HomeGate(sites, waypoints, constructions), sites);
    }

    private static ServiceProvider Services(bool gateBuilt, out ITradeContextReader reader)
    {
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<bool>(AutomationSwitches.EnabledSetting, Arg.Any<CancellationToken>()).Returns(true);
        var (gate, sites) = HomeGateWith(siteComplete: gateBuilt, underConstruction: false);
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(GeneratedWorld.Ships());
        reader = Substitute.For<ITradeContextReader>();
        reader.ReadReachAsync("X1-S0", Arg.Any<CancellationToken>()).Returns(new TradeContext(GeneratedWorld.Map(), Credits, MinProfitPerUnit, 0));
        return new ServiceCollection()
            .AddSingleton(settings)
            .AddSingleton(gate)
            .AddSingleton(sites)
            .AddSingleton(ships)
            .AddSingleton(reader)
            .BuildServiceProvider();
    }
}
