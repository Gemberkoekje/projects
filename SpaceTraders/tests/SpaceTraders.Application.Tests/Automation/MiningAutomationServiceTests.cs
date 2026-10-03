using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Tests.Roles;
using SpaceTraders.Application.Tests.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.4: every free miner takes one trip at a time, for the market shortest of an ore first (D28;
/// within a supply level, a surveyed ore first); ore it holds is sold first. A ship that can survey doesn't
/// mine while the survey plan is on (D20), no drone is bought while the contract takes the miners (D23), and
/// a drone is bought only when its first trip would serve a market short of an ore (D22, D28). Slice 6.10b: a
/// scarce ore no miner works on comes first, and a drone is bought for each scarce ore before anything else that
/// mines (D48), when the order ships are bought in lets it (D43).
/// </summary>
public sealed class MiningAutomationServiceTests
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
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private MiningAutomationPlanState? _state;

    public MiningAutomationServiceTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _goals.When(goals => goals.SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _activeGoals[call.ArgAt<string>(0)] = call.ArgAt<ShipGoal>(1));
        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>()).Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(PlanTypes.MiningAutomation, Arg.Any<MiningAutomationPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.ArgAt<MiningAutomationPlanState>(1));
        _settings.GetAsync<int>("Mining.MaxDrones", Arg.Any<CancellationToken>()).Returns(20);
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
    }

    [Fact]
    public async Task AFreeMiner_ServesTheScarcestMarketFirst_AndSellsItThere()
    {
        // D28: F49 is SCARCE of silicon; H51's copper (LIMITED) pays more, but comes after.
        Fleet(Drone());

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        trip.TradeSymbol.Should().Be("SILICON_CRYSTALS");
        trip.SourceWaypointSymbol.Should().Be(XB5C);
        trip.SellWaypointSymbol.Should().Be(F49);
        trip.Selling.Should().BeFalse();

        var started = _log.Journal.Should().ContainSingle().Subject;
        started.EventKind.Should().Be("MiningStarted");
        started.Properties["Reason"].Should().Be("low_supply");
    }

    [Fact]
    public async Task AFreeMiner_MinesASurveyedOreFirst()
    {
        // A survey of XB5C that is mostly silicon: SILICON_CRYSTALS isn't the best paid, but it is surveyed.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>())
            .Returns(Context(Survey("S-1", XB5C, "SILICON_CRYSTALS", "SILICON_CRYSTALS", "ICE_WATER")));
        Fleet(Drone());

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        trip.TradeSymbol.Should().Be("SILICON_CRYSTALS");
        trip.SellWaypointSymbol.Should().Be(F49);
        _log.Journal.Should().ContainSingle(entry => Equals(entry.Properties["Reason"], "surveyed"));
    }

    [Fact]
    public async Task AFreeMiner_TakesAScarceMarket_BeforeASurveyedOreForAMarketThatIsntShort()
    {
        // D28, seen on the cluster on 2026-10-02: SPECTER-4, bought for A3's scarce silicon, mined surveyed iron
        // for H51, where iron was MODERATE. Here the survey is mostly aluminum, MODERATE at H51.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>())
            .Returns(Context(Survey("S-1", XB5C, "ALUMINUM_ORE", "ALUMINUM_ORE", "ALUMINUM_ORE", "COPPER_ORE")));
        Fleet(Drone());

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        trip.TradeSymbol.Should().Be("SILICON_CRYSTALS");
        trip.SellWaypointSymbol.Should().Be(F49);
    }

    [Fact]
    public async Task OnceEveryMarketShortOfAnOreHasAMiner_AFreeMinerServesTheLowestSupplyLeft()
    {
        // D28: "keep mining for whatever the lowest supply ore is, even if it's not that profitable".
        HeldBy("SHIP-4", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-5", F49, "QUARTZ_SAND");
        HeldBy("SHIP-6", H51, "COPPER_ORE");
        HeldBy("SHIP-7", H51, "IRON_ORE");
        Fleet(Drone(), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7"));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol).Should().Be(("ALUMINUM_ORE", H51));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted")
            .Which.Properties["Reason"].Should().Be("lowest_supply");
    }

    [Fact]
    public async Task WithEveryMarketShortOfAnOreServed_NoDroneIsBought_ThoughThereIsMoreToMine()
    {
        // D28: a new drone would mine H51's aluminum, which is MODERATE: it would not serve a market that is
        // short, so nothing would stop the next one being bought for the same reason.
        HeldBy("SHIP-3", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-4", F49, "QUARTZ_SAND");
        HeldBy("SHIP-5", H51, "COPPER_ORE");
        HeldBy("SHIP-6", H51, "IRON_ORE");
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"));

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task TwoMiners_NeverShareASellMarketAndOre()
    {
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"));

        await RunAsync();

        _activeGoals.Values.Cast<MineAndSellGoal>()
            .Select(trip => (trip.SellWaypointSymbol, trip.TradeSymbol))
            .Should().BeEquivalentTo([(F49, "SILICON_CRYSTALS"), (F49, "QUARTZ_SAND")]);
    }

    [Fact]
    public async Task AMinerHoldingOre_SellsItFirst()
    {
        // Ore left over from the contract (D23).
        Fleet(Drone(waypoint: XB5C, status: "IN_ORBIT", cargo: [new CargoItemModel("COPPER_ORE", 9)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        trip.Selling.Should().BeTrue();
        trip.TradeSymbol.Should().Be("COPPER_ORE");
        trip.SellWaypointSymbol.Should().Be(H51);
        _log.Journal.Should().ContainSingle(entry => Equals(entry.Properties["Reason"], "held_cargo"));
    }

    [Fact]
    public async Task AShipThatCanSurvey_DoesNotMine_WhileTheSurveyPlanIsOn()
    {
        Fleet(CommandShip());
        SurveyPlanIs(on: true);

        await RunAsync();
        _activeGoals.Should().BeEmpty();

        SurveyPlanIs(on: false);

        await RunAsync();
        _activeGoals["SHIP-1"].Should().BeOfType<MineAndSellGoal>();
    }

    [Fact]
    public async Task AMinerWithAnotherGoal_OrAnAssignment_OrInTransit_IsNotFree()
    {
        _activeGoals["SHIP-3"] = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = H51, SellWaypointSymbol = F49 };
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new ShipAssignmentDto("SHIP-4", "Contract", XB5C, H51, "COPPER_ORE", "C-1", 0, Now, null)]);
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(1) });

        await RunAsync();

        _activeGoals.Should().ContainSingle().Which.Value.Should().BeOfType<TradeBetweenMarketsGoal>();
    }

    [Fact]
    public async Task WithNoMinerFree_ItBuysOneDrone_WhoseFirstTripServesAMarketShortOfAnOre()
    {
        // Every scarce ore near the middle has a drone (D48), every miner works, and H51's iron waits: SHIP-6 trades. One
        // a pass: it used to buy one for each opening, and the next pass counts the new drone's trip (D28). It takes turns
        // with the cargo ships (D43).
        FourDronesOneTrading();

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Be(new PurchaseNeed(PurchaseTier.Alternating, "SHIP_MINING_DRONE", H52, 48_328));
        await _purchases.Received(1).TryPurchaseAsync("SHIP_MINING_DRONE", H52, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task WithTheRoleBoardOn_ADroneIsBought_OnlyWhenTheBoardWouldHaveItMine(bool wouldMine, int purchases)
    {
        // Slice 6.9: a drone that would earn more trading would trade, and the next pass would buy another for the same
        // opening, and the next.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-3", FleetRole.Mine), ("SHIP-4", FleetRole.Mine), ("SHIP-5", FleetRole.Mine), ("SHIP-6", FleetRole.Trade));
        _roleAdvisor.WouldTakeAsync(Arg.Is<ShipModel>(ship => ship.ShipType == "SHIP_MINING_DRONE"), FleetRole.Mine, Arg.Any<CancellationToken>())
            .Returns(wouldMine);
        FourDronesOneTrading();

        await RunAsync();

        await _purchases.Received(purchases).TryPurchaseAsync("SHIP_MINING_DRONE", H52, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADroneForEachScarceOre_IsBoughtFirst_ThoughAMinerIsFree_AndWithoutAskingTheRoleBoard()
    {
        // Slice 6.10b (D48): "at least 1 drone per mineral that is scarce or limited". Silicon, quartz, copper and iron are
        // SCARCE or LIMITED near the middle, and there is one drone. The role board keeps one drone mining per scarce ore,
        // so whether trading would pay the new drone more doesn't come into it.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-3", FleetRole.Mine));
        Fleet(Drone());

        await RunAsync();

        _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>();
        _order.Of(AutomationPlan.Mining).Should().Be(new PurchaseNeed(PurchaseTier.Coverage, "SHIP_MINING_DRONE", H52, 48_328));
        await _purchases.Received(1).TryPurchaseAsync("SHIP_MINING_DRONE", H52, Arg.Any<CancellationToken>());
        await _roleAdvisor.DidNotReceiveWithAnyArgs().WouldTakeAsync(default!, default, default);
    }

    [Fact]
    public async Task WithADroneForEachScarceOre_AndAMinerFree_NoDroneIsBought()
    {
        // D28: a drone beyond one per scarce ore waits until every miner works; until then its turn passes to the cargo
        // ships (D43).
        HeldBy("SHIP-3", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-4", F49, "QUARTZ_SAND");
        HeldBy("SHIP-5", H51, "COPPER_ORE");
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"));

        await RunAsync();

        ((MineAndSellGoal)_activeGoals["SHIP-6"]).TradeSymbol.Should().Be("IRON_ORE");
        _order.Of(AutomationPlan.Mining).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task AFreeMiner_TakesAScarceOreNoMinerWorksOn_BeforeOneAMinerWorksOn()
    {
        // D48: H52 is SCARCE of silicon too and pays more for it than F49 for quartz, but SHIP-4 works on silicon. Quartz has
        // nobody, so it comes first.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(
            Map(
            [
                .. Markets().Where(market => market.WaypointSymbol != H52),
                Market(H52, Good("SILICON_CRYSTALS", "IMPORT", 100, 50, 60, "SCARCE"), Good("FUEL", "EXCHANGE", 76, 69, 180, "MODERATE")),
            ]),
            [],
            129_357,
            Now));
        HeldBy("SHIP-4", F49, "SILICON_CRYSTALS");
        Fleet(Drone(), Drone("SHIP-4"));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol).Should().Be(("QUARTZ_SAND", F49));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted")
            .Which.Properties["Reason"].Should().Be("uncovered");
    }

    [Fact]
    public async Task WhileSomethingComesFirstInTheOrder_NoDroneIsBought()
    {
        // D43: the contract's drone and a surveyor come before a drone for a scarce ore.
        _order.Allows = false;
        HeldBy("SHIP-3", H51, "COPPER_ORE");
        Fleet(Drone());

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Tier.Should().Be(PurchaseTier.Coverage);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task WithTheRoleBoardOn_ADroneWithTheTradeRole_GetsNoMiningTrip()
    {
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-3", FleetRole.Trade), ("SHIP-4", FleetRole.Mine));
        Fleet(Drone(), Drone("SHIP-4"));

        await RunAsync();

        _activeGoals.Keys.Should().Equal("SHIP-4");
    }

    [Fact]
    public async Task NoDroneIsBought_WhileTheContractTakesTheMiners()
    {
        // D23: every free miner joins the contract, a new drone too; the contract plan buys at most one.
        _activeGoals["SHIP-3"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Contract), Arg.Any<CancellationToken>()).Returns(true);
        _contractPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-1",
            ShipSymbol = "SHIP-4",
            TradeSymbol = "COPPER_ORE",
            SourceWaypoint = XB5C,
            DestinationWaypoint = H51,
            UnitsRequired = 145,
            UnitsFulfilled = 45,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        Fleet(Drone());

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
        _order.Of(AutomationPlan.Mining).Should().Be(PurchaseNeed.None, "the contract would take the drone (D23)");
    }

    [Fact]
    public async Task TheState_ListsTheOpenings_WithTheMinersThatCouldTakeThem()
    {
        // The drone takes silicon; quartz, copper and iron stay open, and so do B7's, which it can't reach.
        Fleet(Drone(), Drone("SHIP-4") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(1) });

        await RunAsync();

        _state!.Opportunities.Should().ContainSingle(o => o.Status == MarketAutomationOpportunityStatus.Assigned)
            .Which.AssignedShipSymbol.Should().Be("SHIP-3");
        _state.Opportunities.Where(o => o.Status == MarketAutomationOpportunityStatus.Pending).Should().HaveCount(5)
            .And.OnlyContain(o => o.CandidateShipSymbols.Count == 0, "the only free miner took a trip");
    }

    [Fact]
    public async Task TheState_IsWrittenOnlyWhenItChanges()
    {
        _activeGoals["SHIP-3"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = false, FailureReason = "budget" });
        Fleet(Drone());

        await RunAsync();
        await RunAsync();
        await RunAsync();

        await _plans.Received(1).UpsertAsync(PlanTypes.MiningAutomation, Arg.Any<MiningAutomationPlanState>(), Arg.Any<CancellationToken>());
    }

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    /// <summary>Four drones: three on silicon, quartz and copper, and SHIP-6 on a trade, so H51's iron waits.</summary>
    private void FourDronesOneTrading()
    {
        HeldBy("SHIP-3", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-4", F49, "QUARTZ_SAND");
        HeldBy("SHIP-5", H51, "COPPER_ORE");
        _activeGoals["SHIP-6"] = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = H51, SellWaypointSymbol = F49 };
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"));
    }

    private void HeldBy(string ship, string market, string ore)
        => _activeGoals[ship] = new MineAndSellGoal { TradeSymbol = ore, SourceWaypointSymbol = XB5C, SellWaypointSymbol = market };

    private void SurveyPlanIs(bool on)
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Survey), Arg.Any<CancellationToken>()).Returns(on);

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
                _log.For<MiningAutomationService>())
            .EnsureBootstrappedAsync();
}
