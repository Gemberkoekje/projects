using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
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
/// mines (D48), when the order ships are bought in lets it (D43). D53: a trip covers its ore only near the market it sells
/// at, and a drone is bought for each scarce ore in each area. D77: a drone mines until every ore is ABUNDANT, sharing a
/// pair once every pair below ABUNDANT has a miner, and trades only then.
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
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly LogRecorder _log = new();
    private readonly PassedOverShips _passedOver = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private MiningAutomationPlanState? _state;

    public MiningAutomationServiceTests()
    {
        // Business stays home (D60, slice 6.28): the headquarters are in the test's system.
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, $"{SystemSymbol}-A1", 1_000_000, "COBALT", 3));
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
        _passedOver.MayTrade("SHIP-3", [AutomationPlan.Mining]).Should().BeFalse("this pass gave it a trip (B63)");
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
        // D28: "keep mining for whatever the lowest supply ore is, even if it's not that profitable". B7's markets, a
        // drift away (D45), have their miners too.
        HeldBy("SHIP-4", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-5", F49, "QUARTZ_SAND");
        HeldBy("SHIP-6", H51, "COPPER_ORE");
        HeldBy("SHIP-7", H51, "IRON_ORE");
        HeldBy("SHIP-8", B7, "GOLD_ORE", B14);
        HeldBy("SHIP-9", B7, "COPPER_ORE", B14);
        Fleet(Drone(), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7"), Drone("SHIP-8", B7), Drone("SHIP-9", B7));

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
        // short, so nothing would stop the next one being bought for the same reason. B7's markets, a drift away (D45),
        // have their miners too.
        HeldBy("SHIP-3", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-4", F49, "QUARTZ_SAND");
        HeldBy("SHIP-5", H51, "COPPER_ORE");
        HeldBy("SHIP-6", H51, "IRON_ORE");
        HeldBy("SHIP-7", B7, "GOLD_ORE", B14);
        HeldBy("SHIP-8", B7, "COPPER_ORE", B14);
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7", B7), Drone("SHIP-8", B7));

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task TwoMiners_TakeTwoPairs_WhilePairsNobodyWorksAreLeft()
    {
        // One miner per sell market and ore, while a pair below ABUNDANT has none (D77).
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"));

        await RunAsync();

        _activeGoals.Values.Cast<MineAndSellGoal>()
            .Select(trip => (trip.SellWaypointSymbol, trip.TradeSymbol))
            .Should().BeEquivalentTo([(F49, "SILICON_CRYSTALS"), (F49, "QUARTZ_SAND")]);
    }

    [Fact]
    public async Task OnceEveryPairBelowAbundantHasADrone_AFreeDroneSharesOne_InsteadOfTrading()
    {
        // D77, asked on 2026-10-05: "I'd like the miners to only mine, even if there is more profit in trading. They can mine
        // until every mineral is ABUNDANT." Every pair has a drone, F49's silicon two. SHIP-3 shares the SCARCE pair in reach
        // with the fewest, F49's quartz, instead of being passed over to the trading plan (B63).
        HeldBy("SHIP-11", F49, "SILICON_CRYSTALS");
        Fleet([Drone(), .. EveryPairHeld(), Drone("SHIP-11")]);

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SourceWaypointSymbol, trip.SellWaypointSymbol, trip.Drifting).Should().Be(("QUARTZ_SAND", XB5C, F49, false));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted")
            .Which.Properties["Reason"].Should().Be("shared");
        _passedOver.MayTrade("SHIP-3", [AutomationPlan.Mining]).Should().BeFalse("this pass gave it a trip (B63)");
    }

    [Fact]
    public async Task TwoFreeDrones_ShareTwoPairs_TheFewestMinersFirst()
    {
        // D77: the first drone to share counts for the next, in the same pass. Silicon and quartz have a drone each; SHIP-3
        // takes silicon, which pays more, and SHIP-11 quartz, which then has fewer.
        Fleet([Drone(), .. EveryPairHeld(), Drone("SHIP-11")]);

        await RunAsync();

        ((MineAndSellGoal)_activeGoals["SHIP-3"]).TradeSymbol.Should().Be("SILICON_CRYSTALS");
        ((MineAndSellGoal)_activeGoals["SHIP-11"]).TradeSymbol.Should().Be("QUARTZ_SAND");
    }

    [Fact]
    public async Task ADroneShares_RatherThanMineForAMarketThatHasAllItWants()
    {
        // D77: H51 has all the aluminum it wants, and nobody mines it for H51; every other pair has a drone. SHIP-3 shares F49's
        // silicon, SCARCE and in reach, which pays more than F49's quartz.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>())
            .Returns(new MiningContext(Map([.. Markets().Select(market => market.WaypointSymbol == H51 ? AluminumAbundant(market) : market)]), [], 129_357, Now));
        HeldBy("SHIP-4", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-5", F49, "QUARTZ_SAND");
        HeldBy("SHIP-6", H51, "COPPER_ORE");
        HeldBy("SHIP-7", H51, "IRON_ORE");
        HeldBy("SHIP-9", B7, "GOLD_ORE", B14);
        HeldBy("SHIP-10", B7, "COPPER_ORE", B14);
        Fleet(Drone(), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7"), Drone("SHIP-9", B7), Drone("SHIP-10", B7));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol).Should().Be(("SILICON_CRYSTALS", F49));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted")
            .Which.Properties["Reason"].Should().Be("shared");
    }

    [Fact]
    public async Task ADrone_SharesAPairWhoseMarketMakesSomethingFromItsOre_BeforeMiningForOneThatOnlyPaysForIt()
    {
        // D91, asked on 2026-10-05: "first redirect the ore to a place that actually generates iron". SHIP-4 mines IRON_ORE
        // for H51, which makes IRON from it. F49, which makes nothing from it, and XB5C, which exchanges it, are SCARCE and
        // nobody mines for them: SHIP-3 shares H51's pair all the same.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(IronMap(), [], 129_357, Now));
        HeldBy("SHIP-4", H51, "IRON_ORE");
        Fleet(Drone(), Drone("SHIP-4"));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol).Should().Be(("IRON_ORE", H51));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted")
            .Which.Properties["Reason"].Should().Be("shared");
    }

    [Fact]
    public async Task WithNoMarketThatMakesSomethingFromAnOre_ADroneMinesForOneThatOnlyPaysForIt()
    {
        // D91: "lowest priority and only considered as wealth trades". H51 buys no IRON_ORE here: of what is left, XB5C's
        // exchange pays most.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(IronMap(withMaker: false), [], 129_357, Now));
        Fleet(Drone());

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol).Should().Be(("IRON_ORE", XB5C));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted")
            .Which.Properties["Reason"].Should().Be("wealth");
    }

    [Fact]
    public async Task NoDroneIsBought_ForMarketsThatOnlyPayForTheirOre()
    {
        // D91: F49 and XB5C are SCARCE of IRON_ORE, and SHIP-4 mines it for F49, but they only pay for it: no drone is bought
        // to serve them.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(IronMap(withMaker: false), [], 129_357, Now));
        HeldBy("SHIP-4", F49, "IRON_ORE");
        Fleet(Drone("SHIP-4"));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Be(PurchaseNeed.None);
    }

    [Fact]
    public async Task AMinerHoldingOre_SellsItWhereItIsMadeIntoSomething_ThoughOtherMarketsPayMore()
    {
        // D91: XB5C, where it is, exchanges IRON_ORE for 64, and F49 pays 62 for it without making anything from it; H51
        // makes IRON from it, for 58.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(IronMap(), [], 129_357, Now));
        Fleet(Drone(waypoint: XB5C, status: "IN_ORBIT", cargo: [new CargoItemModel("IRON_ORE", 9)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol, trip.Selling).Should().Be(("IRON_ORE", H51, true));
    }

    [Fact]
    public async Task WithEveryOreAtAbundant_ADroneGetsNoTrip_AndMayTrade()
    {
        // D77: "Trade until then". Every market that buys an ore has all it wants: nothing is left to mine for, and the trading
        // plan may give the drone a route until an ore drops below ABUNDANT again (B63).
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>())
            .Returns(new MiningContext(Map([.. Markets().Select(OresAbundant)]), [], 129_357, Now));
        Fleet(Drone());

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _passedOver.MayTrade("SHIP-3", [AutomationPlan.Mining]).Should().BeTrue("the pass had nothing for it to mine (B63)");
    }

    [Fact]
    public async Task TheCommandShip_SharesNoPair_ItTakesWhatPaysItMost()
    {
        // D77 is for the drones. The command ship, in the mining role, keeps D38: with every pair taken the plan has no trip for
        // it, and the trading plan may give it a route, until the role board weighs its roles again.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-1", FleetRole.Mine));
        Fleet([CommandShip(), .. EveryPairHeld()]);

        await RunAsync();

        _activeGoals.Should().NotContainKey("SHIP-1");
        _passedOver.MayTrade("SHIP-1", [AutomationPlan.Mining]).Should().BeTrue();
    }

    [Fact]
    public async Task NoDroneIsBought_ForAPairItWouldOnlyShare()
    {
        // D77 changes what a drone does, not when one is bought (D28): with a drone on every pair and none free, a new drone
        // would only share one, so none is bought.
        Fleet(EveryPairHeld());

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
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
    public async Task ATripThatEndsWhileThePassReadsTheGoals_LeavesNoSaleOfOreItHasSold()
    {
        // B71: a trip's last sale lands in its arrival handler, outside the tick: the hold is emptied first, and the trip ends
        // after. Read the other way round, a pass saw the drone free with the ore it had just sold, and sent it to sell it again.
        var trip = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51, Selling = true };
        _activeGoals["SHIP-3"] = trip;
        ShipModel[] fleet = [Drone(cargo: [new CargoItemModel("COPPER_ORE", 9)])];
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => fleet);
        _goals.GetActiveGoalAsync("SHIP-3", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (ReferenceEquals(_activeGoals["SHIP-3"], trip))
            {
                fleet = [Drone()];
                _activeGoals["SHIP-3"] = trip with { Status = GoalStatus.Completed };
            }

            return _activeGoals["SHIP-3"];
        });

        await RunAsync();

        _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Which.Selling.Should().BeFalse("it holds nothing to sell");
        _log.Journal.Should().NotContain(entry => Equals(entry.Properties["Reason"], "held_cargo"));
    }

    [Fact]
    public async Task AMinerWithAFullHold_SellsWhatItHolds_EvenWhenTheSaleDoesntPayForItsFuel()
    {
        // D71: a trip keeps the other ores a market buys within one tank, so its hold can fill with them. Only F49 buys this
        // quartz, for 1 a unit: 15 credits against 82 for the fuel there. A mining trip would turn to selling at once and end
        // without its ore aboard, on every tick.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(
            Map(
                Market(XB5C, Good("FUEL", "EXCHANGE", 97, 82, 180, "MODERATE")),
                Market(H51, Good("COPPER_ORE", "IMPORT", 138, 67, 123, "LIMITED"), Good("FUEL", "EXCHANGE", 95, 80, 180, "MODERATE")),
                Market(F49, Good("QUARTZ_SAND", "IMPORT", 2, 1, 60, "ABUNDANT"), Good("FUEL", "EXCHANGE", 82, 72, 180, "MODERATE"))),
            [],
            129_357,
            Now));
        Fleet(Drone(waypoint: XB5C, status: "IN_ORBIT", cargo: [new CargoItemModel("QUARTZ_SAND", 15)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol, trip.Selling).Should().Be(("QUARTZ_SAND", F49, true));
    }

    [Fact]
    public async Task AMinerWithAFullHold_ThatNoMarketBuys_GetsNoTrip()
    {
        Fleet(Drone(waypoint: XB5C, status: "IN_ORBIT", cargo: [new CargoItemModel("ICE_WATER", 15)]));

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _log.Journal.Should().BeEmpty();
        _passedOver.MayTrade("SHIP-3", [AutomationPlan.Mining]).Should().BeTrue("the pass had nothing for it, so the trading plan may sell or jettison its hold (B63)");
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
        // Every scarce ore in each area has a drone (D48, D53), every miner works, and H51's iron waits: SHIP-6 trades. One a
        // pass: it used to buy one for each opening, and the next pass counts the new drone's trip (D28). It takes turns with
        // the cargo ships (D43).
        SixDronesOneTrading();

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
        RoleBoardTestSupport.RolesAre(
            _settings,
            _plans,
            ("SHIP-3", FleetRole.Mine),
            ("SHIP-4", FleetRole.Mine),
            ("SHIP-5", FleetRole.Mine),
            ("SHIP-6", FleetRole.Trade),
            ("SHIP-7", FleetRole.Mine),
            ("SHIP-8", FleetRole.Mine));
        _roleAdvisor.WouldTakeAsync(Arg.Is<ShipModel>(ship => ship.ShipType == "SHIP_MINING_DRONE"), FleetRole.Mine, Arg.Any<CancellationToken>())
            .Returns(wouldMine);
        SixDronesOneTrading();

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
    public async Task WhereOnlyTheExploringCommandShipIs_NoDroneIsBought_AndNoOpeningsAreListed()
    {
        // Asked on 2026-10-04: while the command ship explores, business stays home ("For now: come home … start simple").
        // X1-KR90 has a shipyard that sells drones, and ores in short supply; the command ship, exploring, is docked there.
        const string Kr90 = "X1-KR90";
        Fleet(CommandShip(waypoint: H52, status: "DOCKED") with { SystemSymbol = Kr90 });
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new ShipAssignmentDto("SHIP-1", "Explore", H52, null, null, null, 0, DateTimeOffset.UtcNow, null)]);
        _contexts.ReadAsync(Kr90, Arg.Any<CancellationToken>()).Returns(Context());
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = H52,
                SystemSymbol = Kr90,
                ShipTypes = ["SHIP_MINING_DRONE"],
                Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 48_328, FuelCapacity = 80, CargoCapacity = 15 }],
            },
        ]);

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
        _state?.Opportunities.Should().BeEmpty();
    }

    [Fact]
    public async Task WhereOnlyAProbeWatchesTheMarkets_NoDroneIsBought()
    {
        // Slice 6.28 (D60): business stays home. A probe now watches the markets of systems abroad; X1-KR90 sells drones, and
        // its ores are short. Before the change a probe there made X1-KR90 a system the plans did business in.
        const string Kr90 = "X1-KR90";
        const string Yard = "X1-KR90-YARD";
        Fleet(new ShipModel("SHIP-9", Kr90, Yard, "DOCKED", "CRUISE", 0, 0, ShipType: "SHIP_PROBE"));
        _contexts.ReadAsync(Kr90, Arg.Any<CancellationToken>()).Returns(Context());
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = Yard,
                SystemSymbol = Kr90,
                ShipTypes = ["SHIP_MINING_DRONE"],
                Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 48_328, FuelCapacity = 80, CargoCapacity = 15 }],
            },
        ]);

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
        await _contexts.DidNotReceive().ReadAsync(Kr90, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithADroneForEachScarceOre_AndAMinerFree_NoDroneIsBought()
    {
        // D28: a drone beyond one per scarce ore and area waits until every miner works; until then its turn passes to the
        // cargo ships (D43). Six ores and areas are scarce for a drone: four near the middle, and B7's gold and copper, a
        // drift away (D45, D53). There are six drones.
        HeldBy("SHIP-3", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-4", F49, "QUARTZ_SAND");
        HeldBy("SHIP-5", H51, "COPPER_ORE");
        HeldBy("SHIP-7", B7, "GOLD_ORE", B14);
        HeldBy("SHIP-8", B7, "COPPER_ORE", B14);
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7", B7), Drone("SHIP-8", B7));

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
    public async Task AFreeDrone_TakesAScarceMarketADriftAway_AndItsTripDriftsThereFirst()
    {
        // D45, asked on 2026-10-03: "I'd like a way to add mining/siphoning drones for the minerals outside of fuel range, e.g.
        // by having a drone drift to the marketplace that buys the mineral first, then refueling and resuming normal
        // behavior." Every opening near the middle has a drone; B7, SCARCE of gold, is beyond the drone's tank.
        HeldBy("SHIP-4", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-5", F49, "QUARTZ_SAND");
        HeldBy("SHIP-6", H51, "COPPER_ORE");
        HeldBy("SHIP-7", H51, "IRON_ORE");
        Fleet(Drone(), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7"));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SourceWaypointSymbol, trip.SellWaypointSymbol, trip.Drifting).Should().Be(("GOLD_ORE", B14, B7, true));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted")
            .Which.Properties["Reason"].Should().Be("low_supply");
    }

    [Fact]
    public async Task AFreeDrone_TakesAScarceOreNoDroneWorksOn_ADriftAway_BeforeAShorterOneADroneWorksOn()
    {
        // D48 with D45: H52 is SCARCE of silicon too, and in reach, but a drone works on silicon; nobody mines gold.
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
        HeldBy("SHIP-5", F49, "QUARTZ_SAND");
        HeldBy("SHIP-6", H51, "COPPER_ORE");
        HeldBy("SHIP-7", H51, "IRON_ORE");
        Fleet(Drone(), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7"));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol, trip.Drifting).Should().Be(("GOLD_ORE", B7, true));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted")
            .Which.Properties["Reason"].Should().Be("uncovered");
    }

    [Fact]
    public async Task ADroneIsBought_ForAScarceOreADriftAway()
    {
        // D45, "new and free drones": with a drone on each ore near the middle and none free, B7's gold still wants one, a
        // drift away. A drone per scarce mineral (D48) comes before the cargo ships (D43).
        HeldBy("SHIP-3", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-4", F49, "QUARTZ_SAND");
        HeldBy("SHIP-5", H51, "COPPER_ORE");
        HeldBy("SHIP-6", H51, "IRON_ORE");
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Be(new PurchaseNeed(PurchaseTier.Coverage, "SHIP_MINING_DRONE", H52, 48_328));
        await _purchases.Received(1).TryPurchaseAsync("SHIP_MINING_DRONE", H52, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFreeDrone_TakesAnOreShortInTheMiddle_ThoughADroneMinesItForAFarMarket()
    {
        // D53, seen on the cluster on 2026-10-03: SPECTER-10 drifted to B7 for silicon, so silicon counted as covered, and the
        // middle's SCARCE silicon had no drone while four of five mining drones went to B7. "A drone covers a mineral only for
        // the markets it can reach in CRUISE from where it works (the middle, or B7)." Here SHIP-4 mines copper for B7:
        // H51's LIMITED copper, in reach, still has nobody, and comes before B7's SCARCE gold, a drift away.
        HeldBy("SHIP-4", B7, "COPPER_ORE", B14);
        HeldBy("SHIP-5", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-6", F49, "QUARTZ_SAND");
        HeldBy("SHIP-7", H51, "IRON_ORE");
        Fleet(Drone(), Drone("SHIP-4", B7), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7"));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol, trip.Drifting).Should().Be(("COPPER_ORE", H51, false));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted")
            .Which.Properties["Reason"].Should().Be("uncovered");
    }

    [Fact]
    public async Task ADroneIsBought_ForAnOreShortInASecondArea()
    {
        // D53: "the coverage tier may buy a drone per scarce mineral per area (more drones)". Copper is short at H51 and at
        // B7, and a drone on H51's copper doesn't cover B7's. Five drones work on five ores; B7's copper waits for a drone,
        // which comes before the cargo ships (D43), without asking the role board.
        HeldBy("SHIP-3", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-4", F49, "QUARTZ_SAND");
        HeldBy("SHIP-5", H51, "COPPER_ORE");
        HeldBy("SHIP-6", H51, "IRON_ORE");
        HeldBy("SHIP-7", B7, "GOLD_ORE", B14);
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7", B7));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Be(new PurchaseNeed(PurchaseTier.Coverage, "SHIP_MINING_DRONE", H52, 48_328));
        await _purchases.Received(1).TryPurchaseAsync("SHIP_MINING_DRONE", H52, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheMarketItDriftedTo_ADroneMinesFromThere_InCruise()
    {
        // D45: "resuming normal behavior". Docked at B7, B7's gold is in reach: B14 is 25 away.
        Fleet(Drone(waypoint: B7));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        (trip.TradeSymbol, trip.SourceWaypointSymbol, trip.SellWaypointSymbol, trip.Drifting).Should().Be(("GOLD_ORE", B14, B7, false));
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
        // The drone takes silicon; quartz, copper and iron stay open, and so do B7's, a drift away (D45).
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

    /// <summary>
    /// Six drones: five on silicon, quartz, copper, and B7's gold and copper, a drift away (D45, D53), and SHIP-6 on a trade,
    /// so H51's iron waits.
    /// </summary>
    private void SixDronesOneTrading()
    {
        HeldBy("SHIP-3", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-4", F49, "QUARTZ_SAND");
        HeldBy("SHIP-5", H51, "COPPER_ORE");
        _activeGoals["SHIP-6"] = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = H51, SellWaypointSymbol = F49 };
        HeldBy("SHIP-7", B7, "GOLD_ORE", B14);
        HeldBy("SHIP-8", B7, "COPPER_ORE", B14);
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7", B7), Drone("SHIP-8", B7));
    }

    /// <summary>
    /// A drone on each of the fixture's seven pairs: silicon and quartz for F49 and copper, iron and aluminum for H51, in reach
    /// of the middle, and B7's gold and copper, a drift away (D45).
    /// </summary>
    private ShipModel[] EveryPairHeld()
    {
        HeldBy("SHIP-4", F49, "SILICON_CRYSTALS");
        HeldBy("SHIP-5", F49, "QUARTZ_SAND");
        HeldBy("SHIP-6", H51, "COPPER_ORE");
        HeldBy("SHIP-7", H51, "IRON_ORE");
        HeldBy("SHIP-8", H51, "ALUMINUM_ORE");
        HeldBy("SHIP-9", B7, "GOLD_ORE", B14);
        HeldBy("SHIP-10", B7, "COPPER_ORE", B14);
        return [Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7"), Drone("SHIP-8"), Drone("SHIP-9", B7), Drone("SHIP-10", B7)];
    }

    [Fact]
    public async Task ADroneWithNothingUncoveredToServe_TakesAPlaceAtTheFarAsteroid_DriftingToItsMarketFirst()
    {
        // D83, asked on 2026-10-05: "We park a light shuttle ... at the asteroid, and have the drones drop their ore into the
        // light shuttle." B7's iron comes only from B13, 48 away: no drone mines it on a round trip of B7 (D45), so the iron
        // waits for a drone parked there. Every other pair has a miner; the drone drifts to B7 first, out of its CRUISE reach.
        CollectingAtB13();
        Fleet([.. EveryPairHeld(), Drone("SHIP-3")]);

        await RunAsync();

        var job = _activeGoals["SHIP-3"].Should().BeOfType<MineForShuttleGoal>().Subject;
        (job.TradeSymbol, job.AsteroidWaypointSymbol, job.SellWaypointSymbol, job.Drifting).Should().Be(("IRON_ORE", B13, B7, true));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "MiningStarted" && Equals(entry.Properties["Reason"], "collection"));
    }

    [Fact]
    public async Task AnUncoveredOreADroneServesOnItsOwn_ComesBeforeAPlaceAtTheFarAsteroid()
    {
        // D48, near before far: F49's silicon has no miner yet.
        CollectingAtB13();
        Fleet(Drone("SHIP-3"));

        await RunAsync();

        _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Which.SellWaypointSymbol.Should().Be(F49);
    }

    [Fact]
    public async Task ADroneAtTheFarAsteroid_StaysThere_ForTheShuttle()
    {
        // Its goal ended (a failed extraction drops it): it is free at B13, with too little fuel to fly anywhere in CRUISE.
        CollectingAtB13();
        Fleet(Drone("SHIP-3", B13, "IN_ORBIT", cargo: [new CargoItemModel("IRON_ORE", 6)]) with { FuelCurrent = 32 });

        await RunAsync();

        var job = _activeGoals["SHIP-3"].Should().BeOfType<MineForShuttleGoal>().Subject;
        (job.AsteroidWaypointSymbol, job.Drifting).Should().Be((B13, false));
    }

    [Fact]
    public async Task TheDesignatedShuttle_CollectsOnceADroneIsParkedThere()
    {
        CollectingAtB13();
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-3", FleetRole.Mine), ("SHIP-20", FleetRole.Collect));
        _state = StateWithShuttles("SHIP-20");
        _activeGoals["SHIP-3"] = new MineForShuttleGoal { TradeSymbol = "IRON_ORE", AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7 };
        Fleet(Drone("SHIP-3", B13, "IN_ORBIT"), Shuttle("SHIP-20", B7));

        await RunAsync();

        var round = _activeGoals["SHIP-20"].Should().BeOfType<CollectOreGoal>().Subject;
        (round.AsteroidWaypointSymbol, round.SellWaypointSymbol, round.Selling).Should().Be((B13, B7, false));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "CollectionStarted");
    }

    [Fact]
    public async Task TheDesignatedShuttle_WaitsWhileItsDronesAreOnTheirWay()
    {
        // A drift takes hours: a shuttle waiting at the asteroid meanwhile would only wait there.
        CollectingAtB13();
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-3", FleetRole.Mine), ("SHIP-20", FleetRole.Collect));
        _state = StateWithShuttles("SHIP-20");
        _activeGoals["SHIP-3"] = new MineForShuttleGoal { TradeSymbol = "IRON_ORE", AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7, Drifting = true };
        Fleet(Drone("SHIP-3", H51, "IN_TRANSIT"), Shuttle("SHIP-20", B7));

        await RunAsync();

        _activeGoals.Should().NotContainKey("SHIP-20");
    }

    [Fact]
    public async Task AFarAsteroidWhereADroneHasAPlace_GetsAShuttle_WithTheDronesForScarceMinerals_DesignatedForIt()
    {
        // D83: "With scarce-mineral drones": the Coverage tier. The drone sells nothing without one.
        CollectingAtB13();
        _activeGoals["SHIP-3"] = new MineForShuttleGoal { TradeSymbol = "IRON_ORE", AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7, Drifting = true };
        _purchases.TryPurchaseAsync("SHIP_LIGHT_SHUTTLE", H52, Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = true, PurchasedShip = Shuttle("SHIP-20", H52) });
        Fleet(Drone("SHIP-3", H51, "IN_TRANSIT"));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Match<PurchaseNeed>(need => need.Tier == PurchaseTier.Coverage && need.ShipType == "SHIP_LIGHT_SHUTTLE" && need.ShipyardWaypointSymbol == H52);
        var point = _state!.CollectionPoints.Should().ContainSingle().Subject;
        (point.AsteroidWaypointSymbol, point.SellWaypointSymbol).Should().Be((B13, B7));
        point.ShuttleSymbols.Should().Equal("SHIP-20");
        point.DroneSymbols.Should().Equal("SHIP-3");
    }

    [Fact]
    public async Task AFarAsteroidsScarceOres_CountADroneEach_WithTheOtherScarceMinerals()
    {
        // D48: one drone per SCARCE or LIMITED ore and area: silicon, quartz, copper and iron in the middle, gold and copper at
        // B7, six drones. B7's iron, which only a drone parked at B13 serves, is one more.
        CollectingAtB13();
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7"), Drone("SHIP-8"));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Match<PurchaseNeed>(need => need.Tier == PurchaseTier.Coverage && need.ShipType == "SHIP_MINING_DRONE");
    }

    [Fact]
    public async Task ASecondShuttle_WhenAParkedDroneWaitsWithAFullHold_WhileTheFirstIsAwaySelling()
    {
        // "A second shuttle is bought when drones wait for one."
        CollectingAtB13();
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-3", FleetRole.Mine), ("SHIP-20", FleetRole.Collect));
        _state = StateWithShuttles("SHIP-20");
        _activeGoals["SHIP-3"] = new MineForShuttleGoal { TradeSymbol = "IRON_ORE", AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7 };
        _activeGoals["SHIP-20"] = new CollectOreGoal { AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7, Selling = true };
        _purchases.TryPurchaseAsync("SHIP_LIGHT_SHUTTLE", H52, Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = true, PurchasedShip = Shuttle("SHIP-21", H52) });
        Fleet(Drone("SHIP-3", B13, "IN_ORBIT", cargo: [new CargoItemModel("IRON_ORE", 15)]), Shuttle("SHIP-20", B7, "IN_TRANSIT"));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).Should().Match<PurchaseNeed>(need => need.Tier == PurchaseTier.Coverage && need.ShipType == "SHIP_LIGHT_SHUTTLE");
        _state.CollectionPoints.Single().ShuttleSymbols.Should().Equal("SHIP-20", "SHIP-21");
    }

    [Fact]
    public async Task NoSecondShuttle_WhileTheFirstCollects()
    {
        CollectingAtB13();
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-3", FleetRole.Mine), ("SHIP-20", FleetRole.Collect));
        _state = StateWithShuttles("SHIP-20");
        _activeGoals["SHIP-3"] = new MineForShuttleGoal { TradeSymbol = "IRON_ORE", AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7 };
        _activeGoals["SHIP-20"] = new CollectOreGoal { AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7 };
        Fleet(Drone("SHIP-3", B13, "IN_ORBIT", cargo: [new CargoItemModel("IRON_ORE", 15)]), Shuttle("SHIP-20", B7, "IN_TRANSIT"));

        await RunAsync();

        _order.Of(AutomationPlan.Mining).ShipType.Should().NotBe("SHIP_LIGHT_SHUTTLE");
    }

    /// <summary>
    /// B7 imports iron as well, which only B13, 48 from B7, yields near it: a far asteroid no drone mines on a round trip (D83).
    /// H52 sells light shuttles besides drones.
    /// </summary>
    private void CollectingAtB13()
    {
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(
            Map(
            [
                .. Markets().Where(market => market.WaypointSymbol != B7),
                Market(
                    B7,
                    Good("GOLD_ORE", "IMPORT", 230, 114, 60, "SCARCE"),
                    Good("COPPER_ORE", "IMPORT", 68, 58, 180, "SCARCE"),
                    Good("IRON_ORE", "IMPORT", 118, 61, 60, "LIMITED"),
                    Good("FUEL", "EXCHANGE", 79, 71, 180, "MODERATE")),
            ]),
            [],
            129_357,
            Now));
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

    /// <summary>The plan's state with B13's collection point and these shuttles designated for it.</summary>
    private static MiningAutomationPlanState StateWithShuttles(params string[] shuttles) => new()
    {
        PlanId = Guid.NewGuid(),
        Opportunities = [],
        CollectionPoints = [new CollectionPointState { AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7, Ores = ["IRON_ORE"], ScarceOres = ["IRON_ORE"], ShuttleSymbols = shuttles }],
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    /// <summary>A light shuttle: a 40-unit hold and a 300-unit tank, nothing to mine with.</summary>
    private static ShipModel Shuttle(string symbol, string waypoint, string status = "DOCKED")
        => new(symbol, SystemSymbol, waypoint, status, "CRUISE", 300, 300, CargoCapacity: 40, ShipType: "SHIP_LIGHT_SHUTTLE", MountSymbols: [], CargoInventory: []);

    /// <summary>The market, with all the aluminum it wants (D77).</summary>
    private static MarketSnapshot AluminumAbundant(MarketSnapshot market)
        => market with { TradeGoods = [.. market.TradeGoods.Select(good => good.Symbol == "ALUMINUM_ORE" ? good with { Supply = "ABUNDANT" } : good)] };

    /// <summary>The market, with all of every good it wants but fuel (D77).</summary>
    private static MarketSnapshot OresAbundant(MarketSnapshot market)
        => market with { TradeGoods = [.. market.TradeGoods.Select(good => good.Symbol == "FUEL" ? good : good with { Supply = "ABUNDANT" })] };

    private void HeldBy(string ship, string market, string ore, string asteroid = XB5C)
        => _activeGoals[ship] = new MineAndSellGoal { TradeSymbol = ore, SourceWaypointSymbol = asteroid, SellWaypointSymbol = market };

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
                _passedOver,
                _agents,
                _log.For<MiningAutomationService>())
            .EnsureBootstrappedAsync();
}
