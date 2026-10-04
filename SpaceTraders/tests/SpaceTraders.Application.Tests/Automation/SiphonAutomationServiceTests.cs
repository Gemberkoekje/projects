using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Tests.Roles;
using SpaceTraders.Application.Tests.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Siphoning.SiphonFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.7: the mining plan for gases. Every free siphoner takes one trip at a time, for the market shortest of
/// a gas first (D28); gases it holds are sold first, as a trip keeps every gas it siphons (D33). Only a ship that
/// can neither mine nor survey siphons, and a drone is bought only when its first trip would serve a market short
/// of a gas, up to <c>Siphon.MaxDrones</c> (D32). Slice 6.10b: a scarce gas no siphoner works on comes first, and a drone
/// is bought for each scarce gas first (D48), when the order ships are bought in lets it (D43). D53: a trip covers its gas
/// only near the market it sells at, and a drone is bought for each scarce gas in each area.
/// </summary>
public sealed class SiphonAutomationServiceTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly ITradeContextReader _contexts = Substitute.For<ITradeContextReader>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly IShipPurchaseService _purchases = Substitute.For<IShipPurchaseService>();
    private readonly IRoleAdvisor _roleAdvisor = Substitute.For<IRoleAdvisor>();
    private readonly OpenPurchaseOrder _order = new();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private MiningAutomationPlanState? _state;

    public SiphonAutomationServiceTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _goals.When(goals => goals.SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _activeGoals[call.ArgAt<string>(0)] = call.ArgAt<ShipGoal>(1));
        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.SiphonAutomation, Arg.Any<CancellationToken>()).Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(PlanTypes.SiphonAutomation, Arg.Any<MiningAutomationPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.ArgAt<MiningAutomationPlanState>(1));
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = C39,
                SystemSymbol = SystemSymbol,
                ShipTypes = ["SHIP_PROBE", "SHIP_SIPHON_DRONE"],
                Ships = [new ShipyardShipDto { Type = "SHIP_SIPHON_DRONE", PurchasePrice = 42_000, FuelCapacity = 80, CargoCapacity = 15 }],
            },
        ]);
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = true });
    }

    [Fact]
    public async Task AFreeSiphoner_ServesTheScarcestMarketFirst_AtTheGasGiantNearestIt()
    {
        Fleet(SiphonDrone());

        await RunAsync();

        var trip = _activeGoals["SHIP-5"].Should().BeOfType<SiphonAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SourceWaypointSymbol, trip.SellWaypointSymbol, trip.Selling).Should().Be(("LIQUID_HYDROGEN", C38, G50, false));

        var started = _log.Journal.Should().ContainSingle().Subject;
        started.EventKind.Should().Be("SiphonStarted");
        started.Properties["Reason"].Should().Be("low_supply");
    }

    [Fact]
    public async Task OnceEveryMarketShortOfAGasHasASiphoner_AFreeSiphonerServesTheLowestSupplyLeft()
    {
        // D28: "keep mining for whatever the lowest supply ore is", for gases too.
        HeldBy("SHIP-6", G50, "LIQUID_HYDROGEN");
        HeldBy("SHIP-7", E47, "LIQUID_NITROGEN");
        HeldBy("SHIP-8", G50, "HYDROCARBON");
        Fleet(SiphonDrone(), SiphonDrone("SHIP-6"), SiphonDrone("SHIP-7"), SiphonDrone("SHIP-8"));

        await RunAsync();

        var trip = _activeGoals["SHIP-5"].Should().BeOfType<SiphonAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol).Should().Be(("HYDROCARBON", C39));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "SiphonStarted")
            .Which.Properties["Reason"].Should().Be("lowest_supply");
    }

    [Fact]
    public async Task AFreeSiphoner_TakesAScarceMarketADriftAway_AndItsTripDriftsThereFirst()
    {
        // D45 for gases (slice 6.10c): with a gas giant 13 from F48, which is beyond a drone's tank, F48's scarce nitrogen
        // comes before the markets that aren't short. X1-DC53 has no such gas giant.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new TradeContext(MapWithAGasGiantNearF48(), 250_000, 200));
        HeldBy("SHIP-6", G50, "LIQUID_HYDROGEN");
        HeldBy("SHIP-7", E47, "LIQUID_NITROGEN");
        HeldBy("SHIP-8", G50, "HYDROCARBON");
        Fleet(SiphonDrone(), SiphonDrone("SHIP-6"), SiphonDrone("SHIP-7"), SiphonDrone("SHIP-8"));

        await RunAsync();

        var trip = _activeGoals["SHIP-5"].Should().BeOfType<SiphonAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SourceWaypointSymbol, trip.SellWaypointSymbol, trip.Drifting).Should().Be(("LIQUID_NITROGEN", D90, F48, true));
    }

    [Fact]
    public async Task TwoSiphoners_NeverShareASellMarketAndGas()
    {
        Fleet(SiphonDrone("SHIP-5"), SiphonDrone("SHIP-6"));

        await RunAsync();

        _activeGoals.Values.Cast<SiphonAndSellGoal>()
            .Select(trip => (trip.SellWaypointSymbol, trip.TradeSymbol))
            .Should().BeEquivalentTo([(G50, "LIQUID_HYDROGEN"), (E47, "LIQUID_NITROGEN")]);
    }

    [Fact]
    public async Task ASiphonerHoldingGas_SellsIt_WhereItFetchesMost()
    {
        // D33: a trip keeps every gas, and sells only its own; the others are sold one a trip.
        Fleet(SiphonDrone(waypoint: C38, status: "IN_ORBIT", cargo: [new CargoItemModel("LIQUID_NITROGEN", 6)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-5"].Should().BeOfType<SiphonAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol, trip.Selling).Should().Be(("LIQUID_NITROGEN", E47, true));
        _log.Journal.Should().ContainSingle(entry => Equals(entry.Properties["Reason"], "held_cargo"));
    }

    [Fact]
    public async Task ASiphonerWithAFullHold_SellsWhatItHolds_EvenWhenTheSaleDoesntPayForItsFuel()
    {
        // Only C39 buys its nitrogen, for 1 a unit: 15 credits against 80 for the fuel there. A siphon trip would
        // turn to selling at once and end without its gas aboard, on every tick.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(
            Market(C39, Good("LIQUID_NITROGEN", "EXCHANGE", 2, 1, 60, "MODERATE"), Good("FUEL", "EXCHANGE", 80, 70, 180, "MODERATE")),
            Market(C40, Good("FUEL", "EXCHANGE", 75, 66, 180, "MODERATE")),
            Market(G50, Good("LIQUID_HYDROGEN", "IMPORT", 110, 55, 60, "SCARCE"), Good("FUEL", "EXCHANGE", 82, 72, 180, "MODERATE"))));
        Fleet(SiphonDrone(waypoint: C38, status: "IN_ORBIT", cargo: [new CargoItemModel("LIQUID_NITROGEN", 15)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-5"].Should().BeOfType<SiphonAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol, trip.Selling).Should().Be(("LIQUID_NITROGEN", C39, true));
    }

    [Fact]
    public async Task ASiphonerWithAFullHold_ThatNoMarketBuys_GetsNoTrip()
    {
        Fleet(SiphonDrone(waypoint: C38, status: "IN_ORBIT", cargo: [new CargoItemModel("EXOTIC_MATTER", 15)]));

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _log.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyAShipThatCanNeitherMineNorSurvey_Siphons()
    {
        // The command ship has a gas siphon, but it mines or surveys (D20); a mining drone has none.
        Fleet(CommandShip(), SiphonDrone() with { MountSymbols = ["MOUNT_MINING_LASER_I"], ShipType = "SHIP_MINING_DRONE" });

        await RunAsync();

        _activeGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task ASiphonerWithAnotherGoal_OrAnAssignment_OrInTransit_IsNotFree()
    {
        _activeGoals["SHIP-5"] = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = C39, SellWaypointSymbol = G50 };
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new ShipAssignmentDto("SHIP-6", "Contract", C38, G50, "HYDROCARBON", "C-1", 0, DateTimeOffset.UtcNow, null)]);
        Fleet(SiphonDrone("SHIP-5"), SiphonDrone("SHIP-6"), SiphonDrone("SHIP-7") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(1) });

        await RunAsync();

        _activeGoals.Should().ContainSingle().Which.Value.Should().BeOfType<TradeBetweenMarketsGoal>();
    }

    [Fact]
    public async Task WithNoSiphoner_ItBuysTheFirstDrone_WhereItSellsForTheLeast()
    {
        // D48: G50's hydrogen and hydrocarbon and E47's nitrogen are SCARCE or LIMITED, and no siphon drone serves them.
        Fleet(CommandShip(waypoint: "X1-DC53-H51"));

        await RunAsync();

        _order.Of(AutomationPlan.Siphon).Should().Be(new PurchaseNeed(PurchaseTier.Coverage, "SHIP_SIPHON_DRONE", C39, 42_000));
        await _purchases.Received(1).TryPurchaseAsync("SHIP_SIPHON_DRONE", C39, Arg.Any<CancellationToken>());
        _activeGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task WhereOnlyTheExploringCommandShipIs_NoDroneIsBought()
    {
        // Asked on 2026-10-04: while the command ship explores, business stays home. X1-KR90 sells siphon drones, and its
        // gases are short; the command ship, exploring, is docked at its shipyard.
        const string Kr90 = "X1-KR90";
        Fleet(CommandShip(waypoint: C39) with { SystemSymbol = Kr90, Status = "DOCKED" });
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new ShipAssignmentDto("SHIP-1", "Explore", C39, null, null, null, 0, DateTimeOffset.UtcNow, null)]);
        _contexts.ReadAsync(Kr90, Arg.Any<CancellationToken>()).Returns(Context());
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = C39,
                SystemSymbol = Kr90,
                ShipTypes = ["SHIP_SIPHON_DRONE"],
                Ships = [new ShipyardShipDto { Type = "SHIP_SIPHON_DRONE", PurchasePrice = 42_000, FuelCapacity = 80, CargoCapacity = 15 }],
            },
        ]);

        await RunAsync();

        _order.Of(AutomationPlan.Siphon).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
        _state?.Opportunities.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task WithTheRoleBoardOn_ADroneIsBought_OnlyWhenTheBoardWouldHaveItSiphon(bool wouldSiphon, int purchases)
    {
        // Slice 6.9: as for the miners, a drone that would trade instead isn't bought. Each scarce gas has a drone (D48):
        // two siphon, SHIP-7 trades, and G50's hydrocarbon waits.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-5", FleetRole.Siphon), ("SHIP-6", FleetRole.Siphon), ("SHIP-7", FleetRole.Trade));
        _roleAdvisor.WouldTakeAsync(Arg.Is<ShipModel>(ship => ship.ShipType == "SHIP_SIPHON_DRONE"), FleetRole.Siphon, Arg.Any<CancellationToken>())
            .Returns(wouldSiphon);
        ThreeDronesOneTrading();

        await RunAsync();

        await _purchases.Received(purchases).TryPurchaseAsync("SHIP_SIPHON_DRONE", C39, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADroneForEachScarceGas_IsBoughtFirst_ThoughASiphonerIsFree_AndWithoutAskingTheRoleBoard()
    {
        // Slice 6.10b (D48): "at least 1 drone per mineral that is scarce or limited"; the role board keeps one drone
        // siphoning per scarce gas, so whether trading would pay the new drone more doesn't come into it.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-5", FleetRole.Siphon));
        Fleet(SiphonDrone());

        await RunAsync();

        _activeGoals["SHIP-5"].Should().BeOfType<SiphonAndSellGoal>();
        _order.Of(AutomationPlan.Siphon).Tier.Should().Be(PurchaseTier.Coverage);
        await _purchases.Received(1).TryPurchaseAsync("SHIP_SIPHON_DRONE", C39, Arg.Any<CancellationToken>());
        await _roleAdvisor.DidNotReceiveWithAnyArgs().WouldTakeAsync(default!, default, default);
    }

    [Fact]
    public async Task OnceEachScarceGasHasADrone_ADroneIsBoughtWhenEverySiphonerWorks_InTurnWithTheCargoShips()
    {
        // D43, D32: SHIP-7 trades, so G50's hydrocarbon waits for a drone.
        ThreeDronesOneTrading();

        await RunAsync();

        _order.Of(AutomationPlan.Siphon).Should().Be(new PurchaseNeed(PurchaseTier.Alternating, "SHIP_SIPHON_DRONE", C39, 42_000));
        await _purchases.Received(1).TryPurchaseAsync("SHIP_SIPHON_DRONE", C39, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFreeSiphoner_TakesAScarceGasNoSiphonerWorksOn_BeforeOneASiphonerWorksOn()
    {
        // D48: C39 is SCARCE of hydrocarbon here and pays most for it, but SHIP-6 works on hydrocarbon. Hydrogen has nobody.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(
        [
            .. Markets().Where(market => market.WaypointSymbol != C39),
            Market(
                C39,
                Good("HYDROCARBON", "EXCHANGE", 70, 60, 60, "SCARCE"),
                Good("LIQUID_HYDROGEN", "EXCHANGE", 40, 35, 60, "MODERATE"),
                Good("LIQUID_NITROGEN", "EXCHANGE", 34, 30, 60, "MODERATE"),
                Good("FUEL", "EXCHANGE", 80, 70, 180, "MODERATE")),
        ]));
        HeldBy("SHIP-6", G50, "HYDROCARBON");
        Fleet(SiphonDrone(), SiphonDrone("SHIP-6"));

        await RunAsync();

        var trip = _activeGoals["SHIP-5"].Should().BeOfType<SiphonAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol).Should().Be(("LIQUID_HYDROGEN", G50));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "SiphonStarted")
            .Which.Properties["Reason"].Should().Be("uncovered");
    }

    [Fact]
    public async Task ADroneIsBought_ForAGasShortInASecondArea()
    {
        // D53: with D90 near F48, F48's SCARCE nitrogen and LIMITED hydrogen count, a drift away (D45). The drones on E47's
        // nitrogen and G50's hydrogen don't cover F48, beyond a drone's tank: five gases and areas for three drones, so a
        // drone is bought for coverage, before the cargo ships (D43).
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new TradeContext(MapWithAGasGiantNearF48(), 250_000, 200));
        HeldBy("SHIP-5", G50, "LIQUID_HYDROGEN");
        HeldBy("SHIP-6", E47, "LIQUID_NITROGEN");
        HeldBy("SHIP-7", G50, "HYDROCARBON");
        Fleet(SiphonDrone("SHIP-5"), SiphonDrone("SHIP-6"), SiphonDrone("SHIP-7"));

        await RunAsync();

        _order.Of(AutomationPlan.Siphon).Should().Be(new PurchaseNeed(PurchaseTier.Coverage, "SHIP_SIPHON_DRONE", C39, 42_000));
    }

    [Fact]
    public async Task WhileSomethingComesFirstInTheOrder_NoDroneIsBought()
    {
        _order.Allows = false;
        Fleet(CommandShip(waypoint: "X1-DC53-H51"));

        await RunAsync();

        _order.Of(AutomationPlan.Siphon).Tier.Should().Be(PurchaseTier.Coverage);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task WithEveryMarketShortOfAGasServed_NoDroneIsBought_ThoughThereIsMoreToSiphon()
    {
        // D28, D32: a new drone would serve a MODERATE market, which would leave nothing to stop the next.
        HeldBy("SHIP-5", G50, "LIQUID_HYDROGEN");
        HeldBy("SHIP-6", E47, "LIQUID_NITROGEN");
        HeldBy("SHIP-7", G50, "HYDROCARBON");
        Fleet(SiphonDrone("SHIP-5"), SiphonDrone("SHIP-6"), SiphonDrone("SHIP-7"));

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task WithADroneForEachScarceGas_AndASiphonerFree_NoDroneIsBought()
    {
        // D32: a drone beyond one per scarce gas waits until every siphoner works; until then its turn passes to the cargo
        // ships (D43).
        HeldBy("SHIP-5", G50, "LIQUID_HYDROGEN");
        HeldBy("SHIP-6", E47, "LIQUID_NITROGEN");
        Fleet(SiphonDrone("SHIP-5"), SiphonDrone("SHIP-6"), SiphonDrone("SHIP-7"));

        await RunAsync();

        ((SiphonAndSellGoal)_activeGoals["SHIP-7"]).TradeSymbol.Should().Be("HYDROCARBON");
        _order.Of(AutomationPlan.Siphon).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task AtTheCap_NoDroneIsBought()
    {
        // D32: Siphon.MaxDrones, 10 unless set.
        _settings.GetAsync<int>(SiphonAutomationService.MaxDronesSetting, Arg.Any<CancellationToken>()).Returns(1);
        HeldBy("SHIP-5", G50, "LIQUID_HYDROGEN");
        Fleet(SiphonDrone("SHIP-5"));

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task TheState_ListsTheOpenings_WithTheSiphonersThatCouldTakeThem()
    {
        // The drone takes G50's hydrogen; E47's nitrogen and G50's hydrocarbon stay open, and so do F48's, beyond
        // its tank.
        Fleet(SiphonDrone(), SiphonDrone("SHIP-6") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(1) });

        await RunAsync();

        _state!.Opportunities.Should().ContainSingle(o => o.Status == MarketAutomationOpportunityStatus.Assigned)
            .Which.Should().Match<MiningAutomationOpportunityState>(o => o.AssignedShipSymbol == "SHIP-5" && o.SourceWaypointSymbol == C38);
        _state.Opportunities.Where(o => o.Status == MarketAutomationOpportunityStatus.Pending).Should().HaveCount(4)
            .And.OnlyContain(o => o.CandidateShipSymbols.Count == 0, "the only free siphoner took a trip");
    }

    [Fact]
    public async Task TheState_IsWrittenOnlyWhenItChanges()
    {
        HeldBy("SHIP-5", G50, "LIQUID_HYDROGEN");
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = false, FailureReason = "budget" });
        Fleet(SiphonDrone());

        await RunAsync();
        await RunAsync();
        await RunAsync();

        await _plans.Received(1).UpsertAsync(PlanTypes.SiphonAutomation, Arg.Any<MiningAutomationPlanState>(), Arg.Any<CancellationToken>());
    }

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    /// <summary>Three siphon drones: two on hydrogen and nitrogen, and SHIP-7 on a trade, so G50's hydrocarbon waits.</summary>
    private void ThreeDronesOneTrading()
    {
        HeldBy("SHIP-5", G50, "LIQUID_HYDROGEN");
        HeldBy("SHIP-6", E47, "LIQUID_NITROGEN");
        _activeGoals["SHIP-7"] = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = C39, SellWaypointSymbol = G50 };
        Fleet(SiphonDrone("SHIP-5"), SiphonDrone("SHIP-6"), SiphonDrone("SHIP-7"));
    }

    private void HeldBy(string ship, string market, string gas)
        => _activeGoals[ship] = new SiphonAndSellGoal { TradeSymbol = gas, SourceWaypointSymbol = C38, SellWaypointSymbol = market };

    private Task RunAsync()
        => new SiphonAutomationService(
                _ships,
                _goals,
                _assignments,
                _shipyards,
                _contexts,
                _settings,
                _plans,
                _purchases,
                _roleAdvisor,
                _order,
                _log.For<SiphonAutomationService>())
            .EnsureBootstrappedAsync();
}
