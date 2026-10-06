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
        // X1-NF46 are on the map, X1-AD37, three jumps away, isn't. HN44's market was seen 40 minutes ago, NF46's never with
        // prices: neither chooses a route. The home gate's materials count, and only home's markets feed them (D68, D89).
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
        map.StaleMarkets.Should().BeEquivalentTo(["X1-HN44-A1", "X1-NF46-A1"]);
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

    private TradeContextReader Reader() => new(_markets, _waypoints, _agents, _settings, _chains, _sites, _port, _gates, _systems);
}
