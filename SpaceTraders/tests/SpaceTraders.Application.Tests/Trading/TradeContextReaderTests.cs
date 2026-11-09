using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// D89: while the system's jump gate needs materials and the construction plan buys them, the trade map names those
/// materials, so the routes that feed the markets making them come first.
/// </summary>
public sealed class TradeContextReaderTests
{
    private const string System = "X1-FJ91";

    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly ISupplyChainCache _chains = Substitute.For<ISupplyChainCache>();
    private readonly IConstructionSites _sites = Substitute.For<IConstructionSites>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IGateNetwork _gates = Substitute.For<IGateNetwork>();
    private readonly ISystemRepository _systems = Substitute.For<ISystemRepository>();
    private readonly IMarketPriceSampleRepository _samples = Substitute.For<IMarketPriceSampleRepository>();
    private readonly MarketHistoryCache _history = new();
    private readonly PurchaseNeeds _purchaseNeeds = new();
    private readonly IMarketTradeRepository _trades = Substitute.For<IMarketTradeRepository>();

    public TradeContextReaderTests()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<MarketSnapshot>());
        _waypoints.GetBySystemAsync(System, Arg.Any<CancellationToken>()).Returns(Array.Empty<WaypointCacheModel>());
        _chains.GetAsync(_port, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, IReadOnlyList<string>>());

        // On 2026-10-05 at 15:39Z the home gate had 340 of its 1,600 FAB_MATS and 220 of 400 ADVANCED_CIRCUITRY.
        _sites.CachedNeedingMaterialsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ConstructionSiteModel("X1-FJ91-I64", false, [new ConstructionMaterialModel("FAB_MATS", 1_600, 340), new ConstructionMaterialModel("ADVANCED_CIRCUITRY", 400, 400)]),
            new ConstructionSiteModel("X1-KR90-AF5F", false, [new ConstructionMaterialModel("QUANTUM_STABILIZERS", 1, 0)]),
        ]);
    }

    [Fact]
    public async Task WhileTheGateNeedsMaterials_TheMapNamesThoseOfItsSystemItStillNeeds()
    {
        ConstructionPlanOn(true);

        var context = await Reader().ReadAsync(System, CancellationToken.None);

        context.Map.ConstructionMaterials.Should().BeEquivalentTo(["FAB_MATS"]);
    }

    [Fact]
    public async Task WithTheConstructionPlanOff_TheMapNamesNone()
    {
        ConstructionPlanOn(false);

        var context = await Reader().ReadAsync(System, CancellationToken.None);

        context.Map.ConstructionMaterials.Should().BeEmpty();
        await _sites.DidNotReceiveWithAnyArgs().CachedNeedingMaterialsAsync(default);
    }

    [Fact]
    public async Task TheReach_MapsTheSystemsWithinTwiceItsJumps_WithTheirGates_AndTheMarketsWhosePricesAreTooOld()
    {
        // Slice 6.29 (D96): with a reach of 1, a route buys one jump from home and sells one jump from there: X1-HN44 and
        // X1-NF46 are on the map, X1-AD37, three jumps away, isn't. HN44's market was seen 40 minutes ago: its prices are
        // predicted (D132); NF46's was never seen with prices, so it stays stale. The home gate's materials count, and only home's markets feed them (D68, D89).
        ConstructionPlanOn(true);
        var now = DateTimeOffset.UtcNow;
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, "X1-FJ91-A1", 2_000_000, "COBALT", 3));
        _settings.GetAsync<int>(TradeContextReader.MaxHaulDistanceSetting, Arg.Any<CancellationToken>()).Returns(1);
        _settings.GetAsync<long>(CreditReserve.FloorSetting, Arg.Any<CancellationToken>()).Returns(60_000L);
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns(new ExplorePlanState
        {
            ShipSymbol = "SPECTER-1",
            HomeSystemSymbol = System,
            Status = ExploreStatus.Exploring,
            UpdatedAt = now,
            Systems =
            [
                Known(System, "X1-FJ91-I64", now, "X1-HN44-BF9D"),
                Known("X1-HN44", "X1-HN44-BF9D", now, "X1-FJ91-I64", "X1-NF46-C23D"),
                Known("X1-NF46", "X1-NF46-C23D", now, "X1-HN44-BF9D", "X1-AD37-A26B"),
                Known("X1-AD37", "X1-AD37-A26B", now, "X1-NF46-C23D"),
            ],
        });
        foreach (var (system, gate) in new[] { (System, "X1-FJ91-I64"), ("X1-HN44", "X1-HN44-BF9D"), ("X1-NF46", "X1-NF46-C23D"), ("X1-AD37", "X1-AD37-A26B") })
        {
            _waypoints.GetBySystemAsync(system, Arg.Any<CancellationToken>()).Returns(
            [
                new WaypointCacheModel(gate, system, "JUMP_GATE", 0, 0, true, false, now),
                new WaypointCacheModel(system + "-A1", system, "PLANET", 10, 10, true, false, now),
            ]);
        }

        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Snapshot("X1-FJ91-I64", System, new TradeGoodSnapshot("ANTIMATTER", "EXCHANGE", 5_024, 4_800, 10, "MODERATE")),
            Snapshot("X1-FJ91-A1", System, new TradeGoodSnapshot("FAB_MATS", "EXPORT", 400, 200, 20, "MODERATE")),
            Snapshot("X1-HN44-A1", "X1-HN44", new TradeGoodSnapshot("EQUIPMENT", "IMPORT", 9_000, 5_000, 20, "MODERATE")),
            Snapshot("X1-AD37-A1", "X1-AD37", new TradeGoodSnapshot("EQUIPMENT", "IMPORT", 9_000, 5_000, 20, "MODERATE")),
        ]);
        _markets.GetAllFreshnessAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new MarketFreshnessRecord("X1-FJ91-I64", System, now.AddMinutes(-5)),
            new MarketFreshnessRecord("X1-FJ91-A1", System, now.AddMinutes(-5)),
            new MarketFreshnessRecord("X1-HN44-A1", "X1-HN44", now.AddMinutes(-40)),
            new MarketFreshnessRecord("X1-NF46-A1", "X1-NF46", now.AddMinutes(-1), HasPrices: false),
        ]);
        _systems.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new SystemCacheModel(System, "X1", "BLUE_STAR", 20_763, 2_994, now),
            new SystemCacheModel("X1-HN44", "X1", "BLUE_STAR", 20_128, 2_444, now),
        ]);

        var map = (await Reader().ReadReachAsync(System, CancellationToken.None)).Map;

        map.Waypoints.Select(waypoint => waypoint.SystemSymbol).Distinct().Should().BeEquivalentTo([System, "X1-HN44", "X1-NF46"]);
        map.MarketWaypoints.Should().BeEquivalentTo(["X1-FJ91-I64", "X1-FJ91-A1", "X1-HN44-A1"]);
        map.StaleMarkets.Should().BeEquivalentTo(["X1-NF46-A1"]);
        map.PredictedMarkets.Should().BeEquivalentTo(["X1-HN44-A1"]);
        map.Gates.MaxJumps.Should().Be(1);
        map.Gates.CreditFloor.Should().Be(60_000);
        map.Gates.TryFindWay(System, "X1-HN44", out var jumps).Should().BeTrue();
        jumps.Should().ContainSingle();
        map.Gates.TryFindWay(System, "X1-NF46", out _).Should().BeFalse("two jumps, beyond the reach of 1");
        map.Gates.AntimatterAt("X1-FJ91-I64").Should().Be(5_024);
        map.Gates.CooldownSeconds(System, "X1-HN44").Should().BeApproximately(17 + (0.311 * 840.1), 0.1);
        map.ConstructionMaterials.Should().BeEquivalentTo(["FAB_MATS"]);
        map.ConstructionSystemSymbol.Should().Be(System);
    }

    [Fact]
    public async Task AMarketNobodyLookedAtLately_IsPredictedForNow_InsteadOfStale_UnlessItWasNeverSeenWithPrices()
    {
        // D131, asked on 2026-11-08: "Can we make a dead reckoning prediction rule for any markets without active ships or
        // probes so that our command ship goes there to trade eventually?", and since D132 in every system. K88 was
        // seen 18 hours ago with CLOTHING at 3,400, which our purchases had raised; before, it stood at 2,950. A1 was never seen
        // with prices: it stays stale. The plain map of the system (ReadAsync) predicts nothing; a trade trip's (ReadTradeAsync)
        // does.
        var now = DateTimeOffset.UtcNow;
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, "X1-FJ91-A1", 2_000_000, "COBALT", 3));
        _waypoints.GetBySystemAsync(System, Arg.Any<CancellationToken>()).Returns(
        [
            new WaypointCacheModel("X1-FJ91-K88", System, "PLANET", 0, 0, true, false, now),
            new WaypointCacheModel("X1-FJ91-A1", System, "PLANET", 10, 10, true, false, now),
        ]);
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Snapshot("X1-FJ91-K88", System, new TradeGoodSnapshot("CLOTHING", "EXPORT", 3_400, 1_600, 20, "LIMITED")),
            Snapshot("X1-FJ91-A1", System),
            Snapshot("X1-HN44-A1", "X1-HN44", new TradeGoodSnapshot("EQUIPMENT", "IMPORT", 9_000, 5_000, 20, "MODERATE")),
        ]);
        _markets.GetAllFreshnessAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new MarketFreshnessRecord("X1-FJ91-K88", System, now.AddHours(-18)),
            new MarketFreshnessRecord("X1-FJ91-A1", System, now.AddHours(-1), HasPrices: false),
            new MarketFreshnessRecord("X1-HN44-A1", "X1-HN44", now.AddMinutes(-40)),
        ]);
        _samples.GetWaypointPricesAsync("X1-FJ91-K88", Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(
        [
            new MarketPriceSampleDto(now.AddHours(-22), "X1-FJ91-K88", "CLOTHING", 2_950, 1_400, "MODERATE", "WEAK", 20),
            new MarketPriceSampleDto(now.AddHours(-21), "X1-FJ91-K88", "CLOTHING", 2_950, 1_400, "MODERATE", "WEAK", 20),
            new MarketPriceSampleDto(now.AddHours(-20), "X1-FJ91-K88", "CLOTHING", 2_950, 1_400, "MODERATE", "WEAK", 20),
            new MarketPriceSampleDto(now.AddHours(-19), "X1-FJ91-K88", "CLOTHING", 2_950, 1_400, "MODERATE", "WEAK", 20),
            new MarketPriceSampleDto(now.AddHours(-18), "X1-FJ91-K88", "CLOTHING", 3_400, 1_600, "LIMITED", "WEAK", 20),
        ]);

        var reach = (await Reader().ReadReachAsync(System, CancellationToken.None)).Map;
        var trip = (await Reader().ReadTradeAsync(System, CancellationToken.None)).Map;
        var plain = (await Reader().ReadAsync(System, CancellationToken.None)).Map;

        reach.PredictedMarkets.Should().BeEquivalentTo(["X1-FJ91-K88"]);
        reach.StaleMarkets.Should().BeEquivalentTo(["X1-FJ91-A1"]);
        reach.IsFresh("X1-FJ91-K88").Should().BeTrue("a predicted market takes routes again");
        reach.TryGetGood("X1-FJ91-K88", "CLOTHING", out var predicted).Should().BeTrue();
        // An export drifts back to the lowest price it showed this run, a gap of e^(-18/16) left after 18 hours.
        var expected = (int)Math.Round(Math.Exp(Math.Log(2_950) + ((Math.Log(3_400) - Math.Log(2_950)) * Math.Exp(-18.0 / 16))));
        predicted.PurchasePrice.Should().BeCloseTo(expected, 2);
        predicted.Supply.Should().Be("HIGH", "nearly back at its resting price, nobody buying");
        trip.PredictedMarkets.Should().BeEquivalentTo(["X1-FJ91-K88"]);
        trip.TryGetGood("X1-FJ91-K88", "CLOTHING", out var forTrip).Should().BeTrue();
        forTrip.PurchasePrice.Should().BeCloseTo(expected, 2);
        await _samples.Received(1).GetWaypointPricesAsync("X1-FJ91-K88", Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        plain.PredictedMarkets.Should().BeEmpty();
        plain.TryGetGood("X1-FJ91-K88", "CLOTHING", out var asSeen).Should().BeTrue();
        asSeen.PurchasePrice.Should().Be(3_400);
    }

    [Fact]
    public async Task APredictedMarketsWeight_FallsWithTheOtherAgentsTrades_AtItMostThenInItsSystemThenOneJumpOut()
    {
        // D133, asked on 2026-11-08: "the market itself should get the biggest accuracy hit, the system should get a smaller
        // accuracy hit, and the systems 1 jump out should get an even smaller accuracy hit"; chosen "1 : ½ : ¼". Three stale
        // markets: K88, where other agents traded 10 times in the last day; A2, in the same system; and HN44-A1, one jump away.
        // With none of the settings set: 90% with nobody trading, halved by every 10 weighed transactions.
        var now = DateTimeOffset.UtcNow;
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, "X1-FJ91-A1", 2_000_000, "COBALT", 3));
        _settings.GetAsync<int>(TradeContextReader.MaxHaulDistanceSetting, Arg.Any<CancellationToken>()).Returns(1);
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns(new ExplorePlanState
        {
            ShipSymbol = "SPECTER-1",
            HomeSystemSymbol = System,
            Status = ExploreStatus.Exploring,
            UpdatedAt = now,
            Systems = [Known(System, "X1-FJ91-I64", now, "X1-HN44-BF9D"), Known("X1-HN44", "X1-HN44-BF9D", now, "X1-FJ91-I64")],
        });
        foreach (var (system, gate) in new[] { (System, "X1-FJ91-I64"), ("X1-HN44", "X1-HN44-BF9D") })
        {
            _waypoints.GetBySystemAsync(system, Arg.Any<CancellationToken>()).Returns(
            [
                new WaypointCacheModel(gate, system, "JUMP_GATE", 0, 0, true, false, now),
                new WaypointCacheModel(system + "-K88", system, "PLANET", 10, 10, true, false, now),
                new WaypointCacheModel(system + "-A2", system, "PLANET", 20, 20, true, false, now),
                new WaypointCacheModel(system + "-A1", system, "PLANET", 30, 30, true, false, now),
            ]);
        }

        var clothing = new TradeGoodSnapshot("CLOTHING", "EXPORT", 3_000, 1_500, 20, "MODERATE");
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Snapshot("X1-FJ91-K88", System, clothing),
            Snapshot("X1-FJ91-A2", System, clothing),
            Snapshot("X1-HN44-A1", "X1-HN44", clothing),
        ]);
        _markets.GetAllFreshnessAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new MarketFreshnessRecord("X1-FJ91-K88", System, now.AddHours(-2)),
            new MarketFreshnessRecord("X1-FJ91-A2", System, now.AddHours(-2)),
            new MarketFreshnessRecord("X1-HN44-A1", "X1-HN44", now.AddHours(-2)),
        ]);
        _trades.CountSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([new MarketTradeCount(System, "X1-FJ91-K88", 10)]);

        var map = (await Reader().ReadReachAsync(System, CancellationToken.None)).Map;

        map.PredictionWeightOf("X1-FJ91-K88").Should().BeApproximately(0.9 * 0.5, 1e-9, "its own 10 transactions halve it");
        map.PredictionWeightOf("X1-FJ91-A2").Should().BeApproximately(0.9 * Math.Pow(0.5, 0.5), 1e-9, "its system's count at half");
        map.PredictionWeightOf("X1-HN44-A1").Should().BeApproximately(0.9 * Math.Pow(0.5, 0.25), 1e-9, "one jump out at a quarter");
        await _trades.Received(1).CountSinceAsync(Arg.Is<DateTimeOffset>(since => since <= now.AddHours(-23.9)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheGatesChainGoesFirst_OnlyOnceThePurchaseOrderHasReachedTheGatesLoads()
    {
        // D136, asked on 2026-11-08: "Gate-first once the gate can be built". While the trading plan still saves up for a cargo
        // ship of Trade.ShipPurchases, the gate's chain doesn't go first; once nothing before the gate's loads is needed, it does.
        ConstructionPlanOn(true);
        _purchaseNeeds.Report(AutomationPlan.Trading, new PurchaseNeed(PurchaseTier.CargoShips, "SHIP_LIGHT_HAULER", "X1-FJ91-A2", 257_486), DateTimeOffset.UtcNow);

        var saving = (await Reader().ReadAsync(System, CancellationToken.None)).Map;

        _purchaseNeeds.Report(AutomationPlan.Trading, PurchaseNeed.None, DateTimeOffset.UtcNow);
        _purchaseNeeds.Report(AutomationPlan.Construction, new PurchaseNeed(PurchaseTier.Construction, "FAB_MATS", "X1-FJ91-A1", 80_000), DateTimeOffset.UtcNow);

        var buildable = (await Reader().ReadAsync(System, CancellationToken.None)).Map;

        saving.ConstructionMaterials.Should().BeEquivalentTo(["FAB_MATS"], "the gate still needs them; only the order of trades waits");
        saving.ConstructionFirst.Should().BeFalse();
        buildable.ConstructionFirst.Should().BeTrue();

        // D140: once the gate can be built, the context says whether its next load waits for a ship.
        (await Reader().ReadReachAsync(System, CancellationToken.None)).GateLoadWaitsForShip.Should().BeFalse();
        _purchaseNeeds.Report(AutomationPlan.Construction, new PurchaseNeed(PurchaseTier.Construction, "FAB_MATS", "X1-FJ91-A1", 80_000) { WaitsForShip = true }, DateTimeOffset.UtcNow);
        (await Reader().ReadReachAsync(System, CancellationToken.None)).GateLoadWaitsForShip.Should().BeTrue();

        _purchaseNeeds.Report(AutomationPlan.Trading, new PurchaseNeed(PurchaseTier.CargoShips, "SHIP_LIGHT_HAULER", "X1-FJ91-A2", 257_486), DateTimeOffset.UtcNow);
        (await Reader().ReadReachAsync(System, CancellationToken.None)).GateLoadWaitsForShip.Should().BeFalse("the gate can't be built yet");
    }

    [Fact]
    public async Task TheFreshMarkets_AreThoseSeenWithPricesWithinTheAge_InEverySystem()
    {
        // Slice 6.40 (D122): mining abroad reads only these, as a trade route does (D96). Trade.MaxPriceAgeMinutes unset: 30.
        var now = DateTimeOffset.UtcNow;
        _markets.GetAllFreshnessAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new MarketFreshnessRecord("X1-FJ91-A1", System, now.AddMinutes(-5)),
            new MarketFreshnessRecord("X1-HN44-A1", "X1-HN44", now.AddMinutes(-29)),
            new MarketFreshnessRecord("X1-HN44-A2", "X1-HN44", now.AddMinutes(-31)),
            new MarketFreshnessRecord("X1-NF46-A1", "X1-NF46", now.AddMinutes(-1), HasPrices: false),
        ]);

        (await Reader().FreshMarketsAsync(CancellationToken.None)).Should().BeEquivalentTo(["X1-FJ91-A1", "X1-HN44-A1"]);
    }

    private static KnownSystem Known(string system, string gate, DateTimeOffset now, params string[] connections) => new()
    {
        SystemSymbol = system,
        GateWaypointSymbol = gate,
        Gate = GateState.Active,
        GateCheckedAt = now,
        Connections = connections,
        ConnectionsCheckedAt = now,
        ExploredAt = now,
    };

    private static MarketSnapshot Snapshot(string waypoint, string system, params TradeGoodSnapshot[] goods)
        => new(waypoint, system, goods, [], [], []);

    private void ConstructionPlanOn(bool on)
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Construction), Arg.Any<CancellationToken>()).Returns(on);

    private TradeContextReader Reader()
        => new(_markets, _waypoints, _agents, _settings, _chains, _sites, _port, _gates, _systems, new MarketPredictions(_samples, _history), new PredictionConfidence(_trades, _settings, new MarketTradeCountCache()), _purchaseNeeds);
}
