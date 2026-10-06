using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Tests.Mining;
using SpaceTraders.Application.Tests.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.25 (D92), asked on 2026-10-05: "as part of the jump gate build phase, extra miners to be bought for the ores that
/// supply the build gate materials once every half hour (and those miners being dedicated to those ores) until each of the
/// smelters have at least HIGH saturation." Chosen the same day: smelters only, one drone per ore every half hour, at the
/// gate's place in the order and within <c>Mining.MaxDrones</c> ("If the gate can be built, it should be built, otherwise
/// extra miners can be built."), dedicated until the gate is done. H51 smelts IRON for FAB_MATS, F49 COPPER for
/// ADVANCED_CIRCUITRY (<see cref="GateFixture"/>).
/// </summary>
public sealed class GateMinerTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly IContractMineralPlanRepository _contractPlans = Substitute.For<IContractMineralPlanRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly IMiningContextReader _contexts = Substitute.For<IMiningContextReader>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly IShipPurchaseService _purchases = Substitute.For<IShipPurchaseService>();
    private readonly IRoleAdvisor _roleAdvisor = Substitute.For<IRoleAdvisor>();
    private readonly OpenPurchaseOrder _order = new();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly LogRecorder _log = new();
    private readonly PassedOverShips _passedOver = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private MiningAutomationPlanState? _state;

    public GateMinerTests()
    {
        // Business stays home (D60, slice 6.28): the headquarters are in the test's system.
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, $"{SystemSymbol}-A1", 1_000_000, "COBALT", 3));
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _goals.When(goals => goals.SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _activeGoals[call.ArgAt<string>(0)] = call.ArgAt<ShipGoal>(1));
        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>()).Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(PlanTypes.MiningAutomation, Arg.Any<MiningAutomationPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.ArgAt<MiningAutomationPlanState>(1));
        _settings.GetAsync<int>("Mining.MaxDrones", Arg.Any<CancellationToken>()).Returns(20);
        _settings.GetAsync<int>(MiningAutomationService.GateMinerIntervalSetting, Arg.Any<CancellationToken>()).Returns(30);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = H52,
                SystemSymbol = SystemSymbol,
                ShipTypes = ["SHIP_MINING_DRONE"],
                Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 48_328, FuelCapacity = 80, CargoCapacity = 15 }],
            },
        ]);
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = true });
        _purchases.TryPurchaseAsync("SHIP_MINING_DRONE", H52, Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = true, PurchasedShip = Drone("SHIP-30", H52) });
        MarketsAre(GateFixture.Map());
    }

    [Fact]
    public async Task WhileTheGateNeedsMaterials_ADroneIsBought_ForTheOreItsSmeltersAreShortestOf_AtTheGatesPlaceInTheOrder()
    {
        // F49's COPPER_ORE is LIMITED, H51's IRON_ORE MODERATE: copper first. SHIP-3 mines F49's copper, so the copper
        // needs no drone for a scarce mineral (D48) and only the gate's rule buys one.
        MarketsAre(GateFixture.Map(markets: GateFixture.Markets(copper: "LIMITED")));
        Busy("SHIP-3", F49, "COPPER_ORE");
        Fleet(Drone("SHIP-3", F49, "IN_ORBIT"));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Match<PurchaseNeed>(need => need.Tier == PurchaseTier.Construction
            && need.ShipType == "SHIP_MINING_DRONE"
            && need.ShipyardWaypointSymbol == H52
            && need.Price == 48_328);
        await _purchases.Received(1).TryPurchaseAsync("SHIP_MINING_DRONE", H52, Arg.Any<CancellationToken>());
        var miner = _state!.GateMiners.Should().ContainSingle().Subject;
        (miner.ShipSymbol, miner.TradeSymbol).Should().Be(("SHIP-30", "COPPER_ORE"));
        miner.BoughtAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task TheNextDroneForAnOre_WaitsHalfAnHourAfterTheLast()
    {
        // "once every half hour": F49 has its COPPER_ORE HIGH, so only H51's IRON_ORE wants drones.
        MarketsAre(GateFixture.Map(markets: GateFixture.Markets(copper: "HIGH")));
        _activeGoals["SHIP-20"] = new MineAndSellGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        Fleet(Drone("SHIP-20", H51, "IN_ORBIT"));
        _state = WithGateMiners(("SHIP-20", "IRON_ORE", DateTimeOffset.UtcNow.AddMinutes(-29)));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);

        _state = WithGateMiners(("SHIP-20", "IRON_ORE", DateTimeOffset.UtcNow.AddMinutes(-31)));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Tier.Should().Be(PurchaseTier.Construction);
        _state.GateMiners.Select(miner => (miner.ShipSymbol, miner.TradeSymbol)).Should().Equal(("SHIP-20", "IRON_ORE"), ("SHIP-30", "IRON_ORE"));
    }

    [Fact]
    public async Task EachOre_GetsItsOwnDroneEveryHalfHour()
    {
        // "One per ore": F49's COPPER_ORE comes first, but its drone was bought ten minutes ago; H51's IRON_ORE gets its own.
        MarketsAre(GateFixture.Map(markets: GateFixture.Markets(copper: "LIMITED")));
        Busy("SHIP-20", F49, "COPPER_ORE");
        Fleet(Drone("SHIP-20", F49, "IN_ORBIT"));
        _state = WithGateMiners(("SHIP-20", "COPPER_ORE", DateTimeOffset.UtcNow.AddMinutes(-10)));

        await RunAsync();

        _state.GateMiners.Select(miner => (miner.ShipSymbol, miner.TradeSymbol)).Should().Equal(("SHIP-20", "COPPER_ORE"), ("SHIP-30", "IRON_ORE"));
    }

    [Fact]
    public async Task OnceEachSmelterHasItsOreHigh_NoDroneIsBought()
    {
        // "until each of the smelters have at least HIGH saturation".
        MarketsAre(GateFixture.Map(markets: GateFixture.Markets(iron: "HIGH", copper: "ABUNDANT")));
        Busy("SHIP-3", H51, "IRON_ORE");
        Fleet(Drone("SHIP-3", H51, "IN_ORBIT"));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task WhileTheGateNeedsNothing_NoDroneIsBoughtForItsSmelters()
    {
        // The gate is complete, or the construction plan is off: the trade context lists no materials then.
        MarketsAre(GateFixture.Map([]));
        Busy("SHIP-3", H51, "IRON_ORE");
        Fleet(Drone("SHIP-3", H51, "IN_ORBIT"));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Be(PurchaseNeed.None);
    }

    [Fact]
    public async Task TheGatesDrones_StayWithinMiningMaxDrones()
    {
        // "capped".
        _settings.GetAsync<int>("Mining.MaxDrones", Arg.Any<CancellationToken>()).Returns(1);
        Busy("SHIP-3", H51, "IRON_ORE");
        Fleet(Drone("SHIP-3", H51, "IN_ORBIT"));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Be(PurchaseNeed.None);
    }

    [Fact]
    public async Task ADroneForAScarceMineral_ComesFirst()
    {
        // D48 comes earlier in the order: H52's QUARTZ_SAND is SCARCE, and the fleet has no drone yet, only its survey ship.
        MarketsAre(GateFixture.Map(markets: GateFixture.Markets(quartz: "SCARCE")));
        Fleet(SurveyShip(H51));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Tier.Should().Be(PurchaseTier.Coverage);
        _state!.GateMiners.Should().BeEmpty("a drone for a scarce mineral isn't the gate's");
    }

    [Fact]
    public async Task AGateMiner_CountsOnlyForItsOwnOre_AmongTheDronesForScarceMinerals()
    {
        // H52's QUARTZ_SAND is SCARCE and nobody mines it. SHIP-20 mines only IRON_ORE, so it doesn't count as its drone (D48),
        // and a drone is bought for the quartz. Bought five minutes ago, it holds back the next IRON_ORE drone meanwhile.
        MarketsAre(GateFixture.Map(markets: GateFixture.Markets(copper: "HIGH", quartz: "SCARCE")));
        _activeGoals["SHIP-20"] = new MineAndSellGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        Fleet(Drone("SHIP-20", H51, "IN_ORBIT"));
        _state = WithGateMiners(("SHIP-20", "IRON_ORE", DateTimeOffset.UtcNow.AddMinutes(-5)));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Tier.Should().Be(PurchaseTier.Coverage);
        _state.GateMiners.Should().ContainSingle(miner => miner.ShipSymbol == "SHIP-20", "the drone for the quartz isn't the gate's");
    }

    [Fact]
    public async Task AGateMiner_MinesOnlyItsOre_ForTheGatesSmelter_BeforeAScarceMineralNobodyMines()
    {
        // "those miners being dedicated to those ores": an ordinary drone would take H52's SCARCE quartz first (D48).
        MarketsAre(GateFixture.Map(markets: GateFixture.Markets(quartz: "SCARCE")));
        Fleet(Drone("SHIP-20", H51));
        _state = WithGateMiners(("SHIP-20", "IRON_ORE", DateTimeOffset.UtcNow.AddHours(-2)));

        await RunAsync();

        var trip = _activeGoals["SHIP-20"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SourceWaypointSymbol, trip.SellWaypointSymbol, trip.Drifting).Should().Be(("IRON_ORE", XB5C, H51, false));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted")
            .Which.Properties["Reason"].Should().Be("gate");
    }

    [Fact]
    public async Task AGateMiner_SharesItsSmelter_WithTheMinersAlreadyThere()
    {
        Busy("SHIP-3", H51, "IRON_ORE");
        Fleet(Drone("SHIP-3", H51, "IN_ORBIT"), Drone("SHIP-20", H51));
        _state = WithGateMiners(("SHIP-20", "IRON_ORE", DateTimeOffset.UtcNow.AddHours(-2)));

        await RunAsync();

        var trip = _activeGoals["SHIP-20"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol).Should().Be(("IRON_ORE", H51));
    }

    [Fact]
    public async Task AGateMiner_SellsWhatItHoldsFirst()
    {
        // D71's other ores: the hold would otherwise fill with them.
        Fleet(Drone("SHIP-20", H51, cargo: [new CargoItemModel("COPPER_ORE", 6)]));
        _state = WithGateMiners(("SHIP-20", "IRON_ORE", DateTimeOffset.UtcNow.AddHours(-2)));

        await RunAsync();

        var trip = _activeGoals["SHIP-20"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol, trip.Selling).Should().Be(("COPPER_ORE", F49, true));
    }

    [Fact]
    public async Task OnceTheGateNeedsNothingMadeFromItsOre_AGateMinerIsAnOrdinaryDrone()
    {
        // FAB_MATS is done: H51's IRON goes into nothing the gate still needs, so SHIP-20 takes H52's SCARCE quartz (D48).
        MarketsAre(GateFixture.Map(["ADVANCED_CIRCUITRY"], GateFixture.Markets(copper: "HIGH", quartz: "SCARCE")));
        Fleet(Drone("SHIP-20", H51));
        _state = WithGateMiners(("SHIP-20", "IRON_ORE", DateTimeOffset.UtcNow.AddHours(-2)));

        await RunAsync();

        var trip = _activeGoals["SHIP-20"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol).Should().Be(("QUARTZ_SAND", H52));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted")
            .Which.Properties["Reason"].Should().NotBe("gate");
    }

    [Fact]
    public async Task AGateMiner_ParksAtNoCollectionPoint()
    {
        // D83: an ordinary drone at B13, the collection point of B7's iron, would stay there for good. SHIP-20 flies back to
        // H51, out of its CRUISE reach from B13, the fastest way (D45, D84).
        CollectingAtB13();
        Fleet(Drone("SHIP-20", B13, "IN_ORBIT"));
        _state = WithGateMiners(("SHIP-20", "IRON_ORE", DateTimeOffset.UtcNow.AddHours(-2)));

        await RunAsync();

        var trip = _activeGoals["SHIP-20"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol, trip.Drifting).Should().Be(("IRON_ORE", H51, true));
        _state.CollectionPoints.Should().ContainSingle().Which.DroneSymbols.Should().BeEmpty();
    }

    [Fact]
    public async Task AGateMinerThatLeftTheFleet_IsForgotten()
    {
        MarketsAre(GateFixture.Map(markets: GateFixture.Markets(copper: "HIGH")));
        Busy("SHIP-3", H51, "IRON_ORE");
        Fleet(Drone("SHIP-3", H51, "IN_ORBIT"));
        _state = WithGateMiners(("SHIP-20", "IRON_ORE", DateTimeOffset.UtcNow.AddMinutes(-5)));
        _purchases.TryPurchaseAsync(default!, default!, default).ReturnsForAnyArgs(new ShipPurchaseResult { IsSuccess = false });

        await RunAsync();

        _state.GateMiners.Should().BeEmpty();
        _order.Of(AutomationPlan.Mining).Tier.Should().Be(PurchaseTier.Construction, "the IRON_ORE drone it remembered is gone");
    }

    [Fact]
    public void TheGatesMiners_AreStoredWithThePlansState_AndAStateStoredBeforeThemHasNone()
    {
        // PlanRepository stores a plan's state as JSON, with the default options.
        var state = WithGateMiners(("SHIP-20", "IRON_ORE", new DateTimeOffset(2026, 10, 05, 18, 00, 00, TimeSpan.Zero)));

        var read = System.Text.Json.JsonSerializer.Deserialize<MiningAutomationPlanState>(System.Text.Json.JsonSerializer.Serialize(state));
        var before = System.Text.Json.JsonSerializer.Deserialize<MiningAutomationPlanState>(
            """{"PlanId":"5f0c3c56-1b55-4c55-9c2e-6f0f6d1d0d55","Opportunities":[],"CollectionPoints":[],"CreatedAt":"2026-10-05T12:00:00+00:00","UpdatedAt":"2026-10-05T12:00:00+00:00"}""");

        read!.GateMiners.Should().BeEquivalentTo(state.GateMiners);
        before!.GateMiners.Should().BeEmpty();
    }

    /// <summary>
    /// B7 imports iron as well, which only B13, 48 from B7, yields near it: a far asteroid no drone mines on a round trip
    /// (D83). H52 sells light shuttles besides drones.
    /// </summary>
    private void CollectingAtB13()
    {
        MarketsAre(GateFixture.Map(markets:
        [
            .. GateFixture.Markets(copper: "HIGH").Where(market => market.WaypointSymbol != B7),
            Market(B7, Good("IRON_ORE", "IMPORT", 118, 61, 60, "LIMITED"), Good("FUEL", "EXCHANGE", 79, 71, 180, "MODERATE")),
        ]));
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = H52,
                SystemSymbol = SystemSymbol,
                ShipTypes = ["SHIP_MINING_DRONE", "SHIP_LIGHT_SHUTTLE"],
                Ships =
                [
                    new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 48_328, FuelCapacity = 80, CargoCapacity = 15 },
                    new ShipyardShipDto { Type = "SHIP_LIGHT_SHUTTLE", PurchasePrice = 82_905, FuelCapacity = 300, CargoCapacity = 40 },
                ],
            },
        ]);
    }

    private void MarketsAre(TradeMarketMap map)
        => _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(map, [], 1_000_000, Now));

    /// <summary>The mining plan's state with these drones bought for the gate's smelters.</summary>
    private static MiningAutomationPlanState WithGateMiners(params (string Ship, string Ore, DateTimeOffset BoughtAt)[] miners) => new()
    {
        PlanId = Guid.NewGuid(),
        Opportunities = [],
        GateMiners = [.. miners.Select(miner => new GateMinerState { ShipSymbol = miner.Ship, TradeSymbol = miner.Ore, BoughtAt = miner.BoughtAt })],
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private void Busy(string ship, string market, string ore)
        => _activeGoals[ship] = new MineAndSellGoal { TradeSymbol = ore, SourceWaypointSymbol = XB5C, SellWaypointSymbol = market };

    private void Fleet(params ShipModel[] ships)
        => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(ships);

    private Task RunAsync()
        => new MiningAutomationService(
                _ships,
                _goals,
                _assignments,
                _contractPlans,
                _shipyards,
                _contexts,
                _settings,
                _plans,
                _purchases,
                _roleAdvisor,
                _order,
                _passedOver,
                _agents,
                _log.For<MiningAutomationService>())
            .EnsureBootstrappedAsync();
}
