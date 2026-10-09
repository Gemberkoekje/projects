using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Tests.Roles;
using SpaceTraders.Application.Tests.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.4: every ship that can survey surveys (D20): the contract's ore at the contract's asteroid
/// first, otherwise ores the markets buy, at the asteroid nearest their buyer that the miners can reach;
/// each until it has a stock of usable surveys, and then nothing (D27). Slice 6.10b: with the role board on, the plan
/// buys a designated surveyor for a system with mining drones (D47), first in the order after the contract's drone (D43).
/// </summary>
public sealed class SurveyPlanServiceTests
{
    private const string B37 = "X1-DC53-B37";

    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly IContractMineralPlanRepository _contractPlans = Substitute.For<IContractMineralPlanRepository>();
    private readonly IMiningContextReader _contexts = Substitute.For<IMiningContextReader>();
    private readonly ISurveyKeeper _surveyKeeper = Substitute.For<ISurveyKeeper>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly ShipGoalStepGuard _stepGuard = new();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly IShipPurchaseService _purchases = Substitute.For<IShipPurchaseService>();
    private readonly OpenPurchaseOrder _order = new();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private SurveyPlanState? _state;

    public SurveyPlanServiceTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _goals.When(goals => goals.SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _activeGoals[call.ArgAt<string>(0)] = call.ArgAt<ShipGoal>(1));
        _plans.GetAsync<SurveyPlanState>(PlanTypes.Survey, Arg.Any<CancellationToken>()).Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(PlanTypes.Survey, Arg.Any<SurveyPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.ArgAt<SurveyPlanState>(1));
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = H52,
                SystemSymbol = SystemSymbol,
                ShipTypes = ["SHIP_MINING_DRONE", "SHIP_SURVEYOR"],
                Ships =
                [
                    new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 49_011, FuelCapacity = 80, CargoCapacity = 15 },
                    new ShipyardShipDto { Type = "SHIP_SURVEYOR", PurchasePrice = 33_905, FuelCapacity = 80 },
                ],
            },
        ]);
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = true });
    }

    [Fact]
    public async Task WithTheRoleBoardOn_ItBuysASurveyor_ForASystemWithMiningDrones()
    {
        // Slice 6.10b (D47): "the purchase of a designated surveyor ship, so that the COMMAND ship is freed up to use it's
        // considerable cargo for trading and mining". First in the order after the contract's drone (D43).
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-1", FleetRole.Survey), ("SHIP-3", FleetRole.Mine));
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _order.Of(AutomationPlan.Survey).Should().Be(new PurchaseNeed(PurchaseTier.Surveyor, "SHIP_SURVEYOR", H52, 33_905));
        await _purchases.Received(1).TryPurchaseAsync("SHIP_SURVEYOR", H52, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASurveyorTheShipyardHasScarce_IsNotBought_NorANeed()
    {
        // D121, asked on 2026-10-09: "As with the other ships, do not buy INTERCEPTORS if the supply is SCARCE", then "Every
        // purchase". H52 has the surveyor SCARCE, as cached: a need that can't be met would hold back everything after it.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-1", FleetRole.Survey), ("SHIP-3", FleetRole.Mine));
        Fleet(CommandShip(), Drone());
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = H52,
                SystemSymbol = SystemSymbol,
                ShipTypes = ["SHIP_SURVEYOR"],
                Ships = [new ShipyardShipDto { Type = "SHIP_SURVEYOR", PurchasePrice = 33_905, FuelCapacity = 80, Supply = "SCARCE" }],
            },
        ]);

        await RunAsync();

        _order.Of(AutomationPlan.Survey).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("SHIP_SURVEYOR", new string[0])]
    [InlineData("SURVEYOR", new[] { "MOUNT_SURVEYOR_I" })]
    public async Task WithASurveyorInTheSystem_NoneIsBought(string shipType, string[] mounts)
    {
        // One bought since the last restart is known by its type; after startup sync, by its mount (B25).
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-1", FleetRole.Trade), ("SHIP-3", FleetRole.Mine));
        Fleet(CommandShip(), Drone(), SurveyShip() with { ShipType = shipType, MountSymbols = mounts });

        await RunAsync();

        _order.Of(AutomationPlan.Survey).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task WithTheRoleBoardOff_TheCommandShipSurveys_AndNoSurveyorIsBought()
    {
        // D20 holds without the board: the command ship surveys, and nothing would give a surveyor's role away.
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _order.Of(AutomationPlan.Survey).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task WithoutAMiningDrone_NoSurveyorIsBought()
    {
        // Surveys are for miners; the command ship alone has no one to survey for.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-1", FleetRole.Trade));
        Fleet(CommandShip());

        await RunAsync();

        _order.Of(AutomationPlan.Survey).Should().Be(PurchaseNeed.None);
    }

    [Fact]
    public async Task WhileTheContractsDroneWaits_NoSurveyorIsBought()
    {
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-1", FleetRole.Survey), ("SHIP-3", FleetRole.Mine));
        Fleet(CommandShip(), Drone());
        _order.Allows = false;

        await RunAsync();

        _order.Of(AutomationPlan.Survey).Tier.Should().Be(PurchaseTier.Surveyor);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task TheCommandShip_SurveysTheContractsOre_AtTheContractsAsteroid()
    {
        ContractMines("COPPER_ORE", XB5C, H51);
        Fleet(CommandShip(), Drone());

        await RunAsync();

        var survey = _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Subject;
        survey.TargetWaypointSymbol.Should().Be(XB5C);
        survey.TargetDepositSymbol.Should().Be("COPPER_ORE");
    }

    [Fact]
    public async Task WhereOnlyTheExploringCommandShipIs_NothingIsPlannedForSurveys()
    {
        // Asked on 2026-10-04: business stays where our ships work. The command ship explores X1-KR90, whose asteroids and
        // markets are as here: the survey plan plans no surveys there.
        const string Kr90 = "X1-KR90";
        Fleet(CommandShip() with { SystemSymbol = Kr90 });
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new ShipAssignmentDto("SHIP-1", "Explore", XB5C, null, null, null, 0, DateTimeOffset.UtcNow, null)]);
        _contexts.ReadAsync(Kr90, Arg.Any<CancellationToken>()).Returns(Context());

        await RunAsync();

        await _contexts.DidNotReceive().ReadAsync(Kr90, Arg.Any<CancellationToken>());
        _activeGoals.Should().BeEmpty();
        (_state?.Targets ?? []).Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutAContract_ItSurveysTheBestPaidOre_ThatTheMinersCanReach()
    {
        // GOLD pays most, at B7, but the drone can't reach B14: COPPER at XB5C, for H51, it is.
        Fleet(CommandShip(), Drone());

        await RunAsync();

        var survey = _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Subject;
        survey.TargetWaypointSymbol.Should().Be(XB5C);
        survey.TargetDepositSymbol.Should().Be("COPPER_ORE");
    }

    [Fact]
    public async Task ADroneStillDriftingToAFarMarket_DoesNotCountForTheSurveysThere()
    {
        // B54, seen on the cluster on 2026-10-03: SPECTER-4 set off at 12:35 on a drift of 2 hours 25 to B7 (D45), and the
        // survey plan at once wanted surveys at B14 for B7's ores, which would expire long before the drone got there, and
        // sent the command ship out to the far side of the system. A drone counts for the surveys once its drift is over.
        _activeGoals["SHIP-4"] = new MineAndSellGoal { TradeSymbol = "GOLD_ORE", SourceWaypointSymbol = B14, SellWaypointSymbol = B7, Drifting = true };
        Fleet(
            CommandShip(),
            Drone(),
            Drone("SHIP-4", B7, "IN_TRANSIT") with { DestWaypointSymbol = B7, ArrivesAt = DateTimeOffset.UtcNow.AddHours(2), FlightMode = "DRIFT" });

        await RunAsync();

        _state!.Targets.Should().NotContain(target => target.WaypointSymbol == B14);
        _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetWaypointSymbol.Should().Be(XB5C);
    }

    [Fact]
    public async Task AShipThatCanOnlySurvey_SurveysOn_OnceEveryOreHasItsStock_TheOreWithTheFewestUsableSurveysFirst()
    {
        // D52, asked on 2026-10-03: "A (single role) surveyor which is idle is allowed to keep surveying, starting with whichever
        // ore is lowest." Every ore at XB5C has its stock of two; aluminum and iron have two, the others three, and aluminum
        // pays more at H51.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-5", FleetRole.Survey), ("SHIP-3", FleetRole.Mine));
        SurveysAre(
            Survey("S-1", XB5C, "COPPER_ORE", "ALUMINUM_ORE", "IRON_ORE"),
            Survey("S-2", XB5C, "SILICON_CRYSTALS", "QUARTZ_SAND", "COPPER_ORE"),
            Survey("S-3", XB5C, "ALUMINUM_ORE", "IRON_ORE", "SILICON_CRYSTALS", "QUARTZ_SAND"),
            Survey("S-4", XB5C, "COPPER_ORE", "SILICON_CRYSTALS", "QUARTZ_SAND"));
        Fleet(SurveyShip(), Drone());

        await RunAsync();

        _state!.Targets.Should().NotBeEmpty().And.OnlyContain(target => !target.NeedsSurvey);
        var survey = _activeGoals["SHIP-5"].Should().BeOfType<SurveyWaypointGoal>().Subject;
        (survey.TargetWaypointSymbol, survey.TargetDepositSymbol).Should().Be((XB5C, "ALUMINUM_ORE"));
    }

    [Fact]
    public async Task AShipThatCanOnlySurvey_DriftsToTheAreaWhereMoreDronesMine()
    {
        // D54, asked on 2026-10-03: "Please add the option for the survey ship to get to the mining location without surveys",
        // to work where most drones mine. Seen on the cluster: from 15:02Z SPECTER-4 mined B14 for B7 without surveys, with
        // three more drones drifting there, while SPECTER-F surveyed XB5C for SPECTER-3 alone. Here SHIP-4 mines for B7, out of
        // the survey ship's CRUISE reach, and SHIP-6 is drifting there; SHIP-3 mines in the middle.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-5", FleetRole.Survey), ("SHIP-3", FleetRole.Mine), ("SHIP-4", FleetRole.Mine), ("SHIP-6", FleetRole.Mine));
        _activeGoals["SHIP-3"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        _activeGoals["SHIP-4"] = new MineAndSellGoal { TradeSymbol = "GOLD_ORE", SourceWaypointSymbol = B14, SellWaypointSymbol = B7 };
        _activeGoals["SHIP-6"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = B14, SellWaypointSymbol = B7, Drifting = true };
        Fleet(SurveyShip(), Drone("SHIP-3"), Drone("SHIP-4", B7), Drone("SHIP-6"));

        await RunAsync();

        var move = _activeGoals["SHIP-5"].Should().BeOfType<MoveToWaypointGoal>().Subject;
        (move.TargetWaypointSymbol, move.Drifting).Should().Be((B7, true));
        _log.Kept.Should().Contain(message => message.Contains("SHIP-5") && message.Contains(B7) && message.Contains("D54"));
    }

    [Fact]
    public async Task AShipThatCanOnlySurvey_StaysWhereAsManyDronesMine()
    {
        // D54: a tie keeps it where it is; it surveys there.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-5", FleetRole.Survey), ("SHIP-3", FleetRole.Mine), ("SHIP-4", FleetRole.Mine));
        _activeGoals["SHIP-3"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        _activeGoals["SHIP-4"] = new MineAndSellGoal { TradeSymbol = "GOLD_ORE", SourceWaypointSymbol = B14, SellWaypointSymbol = B7 };
        Fleet(SurveyShip(), Drone("SHIP-3"), Drone("SHIP-4", B7));

        await RunAsync();

        _activeGoals["SHIP-5"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetWaypointSymbol.Should().Be(XB5C);
    }

    [Fact]
    public async Task ASecondSurveyShip_IsBought_ForAnAreaWithDronesThatHasNone_AfterTheCoverageDrones()
    {
        // D55, asked on 2026-10-03: "Can we add that extra surveyor drones are bought to try and cover all areas with surveys?
        // The second surveyor is lower priority than the first on the buy order": after the drones per scarce mineral and
        // area (D53), before the cargo ships. Drones mine in the middle and for B7; the one survey ship works at B7.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-5", FleetRole.Survey), ("SHIP-3", FleetRole.Mine), ("SHIP-4", FleetRole.Mine));
        _activeGoals["SHIP-3"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        _activeGoals["SHIP-4"] = new MineAndSellGoal { TradeSymbol = "GOLD_ORE", SourceWaypointSymbol = B14, SellWaypointSymbol = B7 };
        Fleet(SurveyShip(waypoint: B7), Drone("SHIP-3"), Drone("SHIP-4", B7));

        await RunAsync();

        _order.Of(AutomationPlan.Survey).Should().Be(new PurchaseNeed(PurchaseTier.SurveyorPerArea, "SHIP_SURVEYOR", H52, 33_905));
        await _purchases.Received(1).TryPurchaseAsync("SHIP_SURVEYOR", H52, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithASurveyShipInEachAreaWithDrones_NoneIsBought()
    {
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-5", FleetRole.Survey), ("SHIP-7", FleetRole.Survey), ("SHIP-3", FleetRole.Mine), ("SHIP-4", FleetRole.Mine));
        _activeGoals["SHIP-3"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        _activeGoals["SHIP-4"] = new MineAndSellGoal { TradeSymbol = "GOLD_ORE", SourceWaypointSymbol = B14, SellWaypointSymbol = B7 };
        Fleet(SurveyShip(waypoint: B7), SurveyShip(symbol: "SHIP-7"), Drone("SHIP-3"), Drone("SHIP-4", B7));

        await RunAsync();

        _order.Of(AutomationPlan.Survey).Should().Be(PurchaseNeed.None);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task OfTwoSurveyShipsInOneArea_TheFreeOneDriftsToAnAreaWithDronesThatHasNone()
    {
        // D55: "When two survey ships share an area, one drifts to an area that has none", here though fewer drones mine there:
        // the survey ship just bought, at H52, joined the one in the middle.
        RoleBoardTestSupport.RolesAre(
            _settings,
            _plans,
            ("SHIP-5", FleetRole.Survey),
            ("SHIP-7", FleetRole.Survey),
            ("SHIP-3", FleetRole.Mine),
            ("SHIP-4", FleetRole.Mine),
            ("SHIP-6", FleetRole.Mine));
        _activeGoals["SHIP-7"] = new SurveyWaypointGoal { TargetWaypointSymbol = XB5C, TargetDepositSymbol = "COPPER_ORE" };
        _activeGoals["SHIP-3"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        _activeGoals["SHIP-6"] = new MineAndSellGoal { TradeSymbol = "SILICON_CRYSTALS", SourceWaypointSymbol = XB5C, SellWaypointSymbol = F49 };
        _activeGoals["SHIP-4"] = new MineAndSellGoal { TradeSymbol = "GOLD_ORE", SourceWaypointSymbol = B14, SellWaypointSymbol = B7 };
        Fleet(SurveyShip(waypoint: H52), SurveyShip(symbol: "SHIP-7"), Drone("SHIP-3"), Drone("SHIP-4", B7), Drone("SHIP-6"));

        await RunAsync();

        var move = _activeGoals["SHIP-5"].Should().BeOfType<MoveToWaypointGoal>().Subject;
        (move.TargetWaypointSymbol, move.Drifting).Should().Be((B7, true));
    }

    [Fact]
    public async Task TheState_ListsTheSurveyorsThatCanReachEachTarget()
    {
        // B55: the command ship mines (its 400-unit tank takes it to B14 for B7), the surveyor's 80-unit tank keeps it in the
        // middle. B14's targets are no work the plan could give the surveyor, and the ShipLeftIdle rule reads that here.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-1", FleetRole.Mine), ("SHIP-5", FleetRole.Survey));
        Fleet(CommandShip(), SurveyShip());

        await RunAsync();

        _state!.Targets.Where(target => target.WaypointSymbol == B14).Should().NotBeEmpty()
            .And.OnlyContain(target => target.CandidateShipSymbols.Count == 0);
        _state.Targets.Where(target => target.WaypointSymbol == XB5C).Should().NotBeEmpty()
            .And.OnlyContain(target => target.CandidateShipSymbols.SequenceEqual(new[] { "SHIP-5" }));
    }

    [Fact]
    public async Task ASurveyor_SurveysOnlyWhereItCanFlyOnFrom_ToAMarketThatSellsFuel()
    {
        // B58, seen on the cluster on 2026-10-03: the survey ship SPECTER-F reached B7 at 18:40:10Z (D54) and at once flew to
        // B37 for gold, 68 away with its 80-unit tank (the command ship's 400-unit tank makes B37 a target for B7). It got there
        // with 12 fuel and no market that sells fuel within reach, and the area rule drifted it back to B7, 32 minutes; at B7,
        // gold at B37 was again the best paid ore without a survey. B14, where the drones mine for B7, got none. Here, as
        // there: B14, 25 from B7, has copper; B37, 68 from B7, gold.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-5", FleetRole.Survey), ("SHIP-1", FleetRole.Trade), ("SHIP-4", FleetRole.Mine));
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(FarSideMap(), [], 129_357, Now));
        _activeGoals["SHIP-4"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = B14, SellWaypointSymbol = B7 };
        Fleet(SurveyShip(waypoint: B7), CommandShip(B7), Drone("SHIP-4", B7));

        await RunAsync();

        var survey = _activeGoals["SHIP-5"].Should().BeOfType<SurveyWaypointGoal>().Subject;
        (survey.TargetWaypointSymbol, survey.TargetDepositSymbol).Should().Be((B14, "COPPER_ORE"));
        _state!.Targets.Where(target => target.WaypointSymbol == B37).Should().NotBeEmpty()
            .And.OnlyContain(target => target.CandidateShipSymbols.Count == 0, "no surveyor gets away from B37");
    }

    [Fact]
    public async Task AtACollectionPointsAsteroid_ASurveyShipSurveys_ThoughItCantFlyOn_AsItStaysParkedThere()
    {
        // D83: a drone parked at B37 mines B7's gold for the shuttle there, with 12 fuel left: no miner's trip there and back
        // shows the target, and B58 would keep the survey ship from B37, 68 from B7. It parks there, as the drones do.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-5", FleetRole.Survey), ("SHIP-4", FleetRole.Mine));
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(FarSideMap(), [], 129_357, Now));
        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>()).Returns(new MiningAutomationPlanState
        {
            PlanId = Guid.NewGuid(),
            Opportunities = [],
            CollectionPoints = [new CollectionPointState { AsteroidWaypointSymbol = B37, SellWaypointSymbol = B7, Ores = ["GOLD_ORE"], ScarceOres = ["GOLD_ORE"] }],
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        _activeGoals["SHIP-4"] = new MineForShuttleGoal { TradeSymbol = "GOLD_ORE", AsteroidWaypointSymbol = B37, SellWaypointSymbol = B7 };
        Fleet(SurveyShip(waypoint: B7), Drone("SHIP-4", B37, "IN_ORBIT") with { FuelCurrent = 12 });

        await RunAsync();

        var survey = _activeGoals["SHIP-5"].Should().BeOfType<SurveyWaypointGoal>().Subject;
        (survey.TargetWaypointSymbol, survey.TargetDepositSymbol).Should().Be((B37, "GOLD_ORE"));
        _state!.Targets.Should().Contain(target => target.WaypointSymbol == B37 && target.CandidateShipSymbols.Contains("SHIP-5"));
    }

    [Fact]
    public async Task ASurveyShipAtACollectionPointsAsteroid_StaysThere_ThoughItCantCruiseToTheMarketsArea()
    {
        // B68, on 2026-10-05: SPECTER-2C surveyed B44 once, then, with 26 fuel, couldn't cruise the 53 to B7, where the parked
        // drones count (D83), so its own area had none, and the survey plan moved it to B7 (D54), a 25-minute drift. From B7 it
        // was sent to survey B44 again: one survey every 28 minutes from 13:49 on, and the drones extracted without one. Here the
        // survey ship is at B37 with 12 fuel, 68 from B7: it surveys B37 again.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-5", FleetRole.Survey), ("SHIP-4", FleetRole.Mine));
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new MiningContext(FarSideMap(), [], 129_357, Now));
        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>()).Returns(new MiningAutomationPlanState
        {
            PlanId = Guid.NewGuid(),
            Opportunities = [],
            CollectionPoints = [new CollectionPointState { AsteroidWaypointSymbol = B37, SellWaypointSymbol = B7, Ores = ["GOLD_ORE"], ScarceOres = ["GOLD_ORE"] }],
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        _activeGoals["SHIP-4"] = new MineForShuttleGoal { TradeSymbol = "GOLD_ORE", AsteroidWaypointSymbol = B37, SellWaypointSymbol = B7 };
        Fleet(SurveyShip(waypoint: B37) with { FuelCurrent = 12 }, Drone("SHIP-4", B37, "IN_ORBIT") with { FuelCurrent = 12 });

        await RunAsync();

        var survey = _activeGoals["SHIP-5"].Should().BeOfType<SurveyWaypointGoal>().Subject;
        (survey.TargetWaypointSymbol, survey.TargetDepositSymbol).Should().Be((B37, "GOLD_ORE"));
        _log.Entries.Should().NotContain(entry => entry.Message.Contains("moves to", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithTheRoleBoardOn_OnlyTheShipWithTheSurveyRole_Surveys()
    {
        // Slice 6.9 (D38): a ship that can only survey surveys, so the command ship, with the trade role, doesn't.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-1", FleetRole.Trade), ("SHIP-5", FleetRole.Survey));
        Fleet(CommandShip(), Drone(), SurveyShip());

        await RunAsync();

        _activeGoals.Keys.Should().Equal("SHIP-5");
        _activeGoals["SHIP-5"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetWaypointSymbol.Should().Be(XB5C);
    }

    [Fact]
    public async Task ASecondSurveyor_TakesTheNextTarget()
    {
        _activeGoals["SHIP-1"] = new SurveyWaypointGoal { TargetWaypointSymbol = XB5C, TargetDepositSymbol = "COPPER_ORE" };
        Fleet(CommandShip(), CommandShip(symbol: "SHIP-2"), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetDepositSymbol.Should().Be("COPPER_ORE");
        _activeGoals["SHIP-2"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetDepositSymbol.Should().Be("ALUMINUM_ORE");
    }

    [Fact]
    public async Task ShipsThatCantSurvey_AndBusySurveyors_GetNoSurvey()
    {
        _activeGoals["SHIP-1"] = new ScoutWaypointGoal { TargetWaypointSymbol = H51 };
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals.Should().ContainSingle().Which.Value.Should().BeOfType<ScoutWaypointGoal>();
    }

    [Fact]
    public async Task OnceTheContractsOreHasItsStock_TheCommandShipSurveysForTheNextOre()
    {
        // D27 (2026-10-02): "I'd expect him to make 1 copper ore survey and then move to the next ore
        // type". The stock is two usable surveys per ore, unless the setting says otherwise.
        ContractMines("COPPER_ORE", XB5C, H51);
        SurveysAre(Survey("S-1", XB5C, "COPPER_ORE"), Survey("S-2", XB5C, "COPPER_ORE"));
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetDepositSymbol.Should().Be("ALUMINUM_ORE");
    }

    [Fact]
    public async Task TheStock_IsASetting()
    {
        _settings.GetAsync<int>(SurveyPlanService.StockPerOreSetting, Arg.Any<CancellationToken>()).Returns(1);
        ContractMines("COPPER_ORE", XB5C, H51);
        SurveysAre(Survey("S-1", XB5C, "COPPER_ORE"));
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetDepositSymbol.Should().Be("ALUMINUM_ORE");
    }

    [Fact]
    public async Task WithEveryOreStocked_TheSurveyorWaits()
    {
        // D27. The command ship can do more than survey: it waits, or trades and mines in its spare time (D34); a ship that
        // can only survey surveys on (D52).
        ContractMines("COPPER_ORE", XB5C, H51);
        SurveysAre(
            Survey("S-1", XB5C, "COPPER_ORE", "ALUMINUM_ORE", "IRON_ORE"),
            Survey("S-2", XB5C, "SILICON_CRYSTALS", "QUARTZ_SAND", "COPPER_ORE"),
            Survey("S-3", XB5C, "ALUMINUM_ORE", "IRON_ORE", "SILICON_CRYSTALS", "QUARTZ_SAND"));
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _state!.Targets.Should().HaveCount(6).And.OnlyContain(target => !target.NeedsSurvey && target.UsableSurveys == 2);
    }

    [Fact]
    public async Task EachPass_EndsTheSurveysThatExpired()
    {
        Fleet(Drone());

        await RunAsync();

        await _surveyKeeper.Received(1).ExpireAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheState_ListsTheTargets_AndIsWrittenOnlyWhenItChanges()
    {
        ContractMines("COPPER_ORE", XB5C, H51);
        _activeGoals["SHIP-1"] = new SurveyWaypointGoal { TargetWaypointSymbol = XB5C, TargetDepositSymbol = "COPPER_ORE" };
        Fleet(CommandShip(), Drone());

        await RunAsync();
        await RunAsync();

        await _plans.Received(1).UpsertAsync(PlanTypes.Survey, Arg.Any<SurveyPlanState>(), Arg.Any<CancellationToken>());
        var first = _state!.Targets[0];
        first.ForContract.Should().BeTrue();
        first.SurveyorShipSymbols.Should().Equal("SHIP-1");
        _state.Targets.Should().HaveCount(6, "the contract's copper, then five sellable ores at XB5C");
    }

    [Fact]
    public async Task ASurveyThatNeedsTaking_TakesTheCommandShipOffItsSpareTimeTrip_WithItsHoldAboard()
    {
        // D37 (2026-10-02): "It surveys on the spot with the hold aboard, then carries on filling, and sells once full."
        var gathering = CommandShip() with { CargoCurrent = 12, CargoInventory = [new CargoItemModel("COPPER_ORE", 12)] };
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C };
        _activeGoals["SHIP-1"] = trip;
        Stored(gathering);
        Fleet(gathering, Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetWaypointSymbol.Should().Be(XB5C);
        var interrupted = _log.Journal.Should().ContainSingle().Subject;
        interrupted.EventKind.Should().Be("GatheringInterrupted");
        interrupted.Properties["Reason"].Should().Be("survey");
        interrupted.Properties["Units"].Should().Be(12);
        interrupted.Properties["WaypointSymbol"].Should().Be(XB5C);
    }

    [Fact]
    public async Task WithEveryOreStocked_TheCommandShipGathersOn()
    {
        SurveysAre(
            Survey("S-1", XB5C, "COPPER_ORE", "ALUMINUM_ORE", "IRON_ORE"),
            Survey("S-2", XB5C, "SILICON_CRYSTALS", "QUARTZ_SAND", "COPPER_ORE"),
            Survey("S-3", XB5C, "ALUMINUM_ORE", "IRON_ORE", "SILICON_CRYSTALS", "QUARTZ_SAND"));
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C };
        _activeGoals["SHIP-1"] = trip;
        Stored(CommandShip());
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeSameAs(trip);
        _log.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task ASpareTimeTripThatSells_IsNotInterrupted()
    {
        // It is nearly done; the plans choose again after its sales.
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C, Selling = true };
        _activeGoals["SHIP-1"] = trip;
        Stored(CommandShip(H51, "DOCKED"));
        Fleet(CommandShip(H51, "DOCKED"), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeSameAs(trip);
    }

    [Fact]
    public async Task AShipWhoseArrivalIsNotRecordedYet_IsNotTakenOffItsTrip()
    {
        // B17: the arrival matches the trip's goal. The fleet as the plans read it reckons the ship arrived; as
        // stored, it is still in flight until the arrival is recorded.
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C };
        _activeGoals["SHIP-1"] = trip;
        Stored(CommandShip(H51, "IN_TRANSIT") with { DestWaypointSymbol = XB5C, ArrivesAt = DateTimeOffset.UtcNow.AddSeconds(-1) });
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeSameAs(trip);
        _log.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task AShipWhoseGoalStepRuns_IsTakenOffItsTripOnALaterTick()
    {
        // B46: a step that turns the trip to selling would write it back over the survey.
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C };
        _activeGoals["SHIP-1"] = trip;
        Stored(CommandShip());
        Fleet(CommandShip(), Drone());
        _stepGuard.TryEnter("SHIP-1").Should().BeTrue();

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeSameAs(trip);

        _stepGuard.Exit("SHIP-1");
        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>();
    }

    /// <summary>
    /// The far side of X1-DC53 as it was on 2026-10-03 (B58): B14, 25 from B7, has the common metals, copper among them;
    /// B37, 68 from B7 and further from any other market, the precious ones, gold among them.
    /// </summary>
    private static TradeMarketMap FarSideMap()
        => new(
            [
                .. Waypoints.Where(waypoint => waypoint.Symbol is not B13 and not B14),
                new WaypointCacheModel(B14, SystemSymbol, "ASTEROID", 23, 348, false, false, DateTimeOffset.UnixEpoch, TraitsJson: """[{"symbol":"COMMON_METAL_DEPOSITS"}]"""),
                new WaypointCacheModel(B37, SystemSymbol, "ASTEROID", -9, 382, false, false, DateTimeOffset.UnixEpoch, TraitsJson: """[{"symbol":"PRECIOUS_METAL_DEPOSITS"}]"""),
            ],
            Markets(),
            new Dictionary<string, IReadOnlyList<string>>());

    private void Stored(ShipModel ship) => _ships.FindAsync(ship.Symbol, Arg.Any<CancellationToken>()).Returns(ship);

    private void ContractMines(string ore, string asteroid, string destination)
        => _contractPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-1",
            ShipSymbol = "SHIP-3",
            TradeSymbol = ore,
            SourceWaypoint = asteroid,
            DestinationWaypoint = destination,
            UnitsRequired = 145,
            UnitsFulfilled = 45,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        });

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private void SurveysAre(params SurveyModel[] surveys)
        => _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(surveys));

    private Task RunAsync()
        => new SurveyPlanService(
                _ships,
                _goals,
                _assignments,
                _contractPlans,
                _contexts,
                _surveyKeeper,
                _plans,
                _settings,
                new SpareTimeInterruption(_ships, _goals, _stepGuard, Substitute.For<ITripBook>(), _log.For<SpareTimeInterruption>()),
                _shipyards,
                _purchases,
                _order,
                _log.For<SurveyPlanService>())
            .EnsureBootstrappedAsync();
}
