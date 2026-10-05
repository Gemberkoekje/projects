using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Tests.Roles;
using SpaceTraders.Application.Tests.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.5: every free ship with a cargo hold and a fuel tank trades. Two traders never share a route.
/// Slice 6.4: the plan buys its own cargo ships (D21). Slice 6.8: a surveyor with nothing to survey trades
/// (D34); with the spare-time plan on, it trades only for a route that waits for it, selling its hold first, and
/// otherwise keeps gathering (D37).
/// </summary>
public sealed class TradingAutomationServiceTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly IShipPurchaseService _purchases = Substitute.For<IShipPurchaseService>();
    private readonly ShipGoalStepGuard _stepGuard = new();
    private readonly IContractMineralPlanRepository _contractPlans = Substitute.For<IContractMineralPlanRepository>();
    private readonly ICargoJettison _jettison = Substitute.For<ICargoJettison>();
    private readonly OpenPurchaseOrder _order = new();
    private readonly FullHoldSavings _savings = new();
    private readonly PassedOverShips _passedOver = new();
    private readonly IConstructionSites _constructionSites = Substitute.For<IConstructionSites>();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private TradingAutomationPlanState? _state;

    public TradingAutomationServiceTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(Map()));
        _constructionSites.CachedNeedingMaterialsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ConstructionSiteModel>());
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _goals.When(goals => goals.SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _activeGoals[call.ArgAt<string>(0)] = call.ArgAt<ShipGoal>(1));
        _plans.GetAsync<TradingAutomationPlanState>(PlanTypes.TradingAutomation, Arg.Any<CancellationToken>())
            .Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(PlanTypes.TradingAutomation, Arg.Any<TradingAutomationPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.ArgAt<TradingAutomationPlanState>(1));
        _settings.GetAsync<string>(TradingAutomationService.ShipPurchasesSetting, Arg.Any<CancellationToken>())
            .Returns("SHIP_LIGHT_SHUTTLE,SHIP_LIGHT_HAULER,SHIP_LIGHT_HAULER");
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = A1,
                SystemSymbol = SystemSymbol,
                ShipTypes = ["SHIP_LIGHT_SHUTTLE", "SHIP_LIGHT_HAULER"],
                Ships =
                [
                    new ShipyardShipDto { Type = "SHIP_LIGHT_SHUTTLE", PurchasePrice = 117_273, FuelCapacity = 300, CargoCapacity = 40 },
                    new ShipyardShipDto { Type = "SHIP_LIGHT_HAULER", PurchasePrice = 354_210, FuelCapacity = 600, CargoCapacity = 80 },
                ],
            },
        ]);
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = true });
        _jettison.JettisonAsync(Arg.Any<ShipModel>(), Arg.Any<CargoItemModel>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ship = call.Arg<ShipModel>();
                var gone = call.Arg<CargoItemModel>();
                List<CargoItemModel> left = [.. (ship.CargoInventory ?? []).Where(item => item.Symbol != gone.Symbol)];
                return new CargoModel(left.Sum(item => item.Units), ship.CargoCapacity, left);
            });
    }

    [Fact]
    public async Task TheCommandShip_AfterScouting_TakesTheBestRoute()
    {
        // Scouting done (B10): no goal and no assignment, docked where the scout plan ended. SHIP_PARTS are made from EQUIPMENT,
        // so its routes come first (D82), and A1 pays the most for it.
        Fleet(CommandShip());

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        trip.TradeSymbol.Should().Be("EQUIPMENT");
        trip.BuyWaypointSymbol.Should().Be(K85);
        trip.SellWaypointSymbol.Should().Be(A1);
        trip.Units.Should().Be(40);
        trip.ExpectedProfit.Should().Be((245 * 40) - (2 * 90));
        trip.FeedsTradeSymbol.Should().BeEmpty("A1 makes nothing from EQUIPMENT");
        trip.CargoBought.Should().BeFalse();

        var started = _log.Journal.Should().ContainSingle().Subject;
        started.EventKind.Should().Be("TradeStarted");
        started.Properties["ShipSymbol"].Should().Be("SHIP-1");
        started.Properties.Should().NotContainKey("FeedsTradeSymbol");
    }

    [Fact]
    public async Task ATripToAMarketThatMakesAPricierGoodFromItsCargo_SaysWhichInItsGoalAndTheJournal()
    {
        // D15: D41 makes SHIP_PARTS (7,721) from EQUIPMENT; paying 3,520 for it there, it is the best route.
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(Map(K85Market(), D41Market(equipmentPrice: 3_520), A1Market())));
        Fleet(CommandShip());

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        (trip.SellWaypointSymbol, trip.FeedsTradeSymbol).Should().Be((D41, "SHIP_PARTS"));
        _log.Journal.Should().ContainSingle().Which.Properties["FeedsTradeSymbol"].Should().Be("SHIP_PARTS");
        _state!.Opportunities.Should().ContainSingle(o => o.Status == MarketAutomationOpportunityStatus.Assigned)
            .Which.FeedsTradeSymbol.Should().Be("SHIP_PARTS");
    }

    [Fact]
    public async Task AShipWithAnOpenAssignment_IsNoTrader()
    {
        // The contract drone works through its assignment, without a goal.
        Fleet(CommandShip());
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new ShipAssignmentDto("SHIP-1", "Contract", Asteroid, A1, "COPPER_ORE", "CONTRACT-1", 0, DateTimeOffset.UtcNow, null)]);

        await RunAsync();

        _activeGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task AShipWithAnotherPlansGoal_OrInTransit_OrWithoutAHold_IsNoTrader()
    {
        _activeGoals["SHIP-1"] = new ScoutWaypointGoal { TargetWaypointSymbol = A1 };
        var probe = new ShipModel("SHIP-2", SystemSymbol, K85, "DOCKED", "DRIFT", 0, 0, ShipType: "SATELLITE");
        var flying = CommandShip(symbol: "SHIP-4") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) };
        Fleet(CommandShip(), probe, flying);

        await RunAsync();

        _activeGoals.Should().ContainSingle().Which.Value.Should().BeOfType<ScoutWaypointGoal>();
    }

    [Fact]
    public async Task AShipWhoseGoalIsBlocked_IsATrader()
    {
        _activeGoals["SHIP-1"] = new ScoutWaypointGoal { TargetWaypointSymbol = A1, Status = GoalStatus.Blocked, StatusReason = "runaway" };
        Fleet(CommandShip());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>();
    }

    [Fact]
    public async Task WithoutTheCreditsForAllTheUnits_ATraderTakesFewer_AndSavesUpForNoMore()
    {
        // D79: a trip carries what the credits pay for. From K85 the command ship's best route is 40 EQUIPMENT for A1 (D82),
        // 130,160 and 180 for fuel; with 100,000 for cargo it takes 30 of them. It noted the saving for the 40 (D56), and setting
        // off on that route ends it: the trip holds back what its 30 cost instead (D57).
        CreditsAre(100_000);
        Fleet(CommandShip());

        await RunAsync();

        var smaller = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        (smaller.TradeSymbol, smaller.SellWaypointSymbol, smaller.Units, smaller.ReservedCredits).Should().Be(("EQUIPMENT", A1, 30, 30 * 3_254L));
        _savings.TryGet("SHIP-1", out _).Should().BeFalse();
        _log.Kept.Should().ContainSingle(message => message.Contains("saves up for 40 EQUIPMENT", StringComparison.Ordinal) && message.Contains("D56", StringComparison.Ordinal));
        _activeGoals.Clear();

        // Once the credits pay for it, the trader takes it, and the saving becomes what the trip holds back until it buys
        // (D57): ships are still bought after the hold, and it isn't counted twice.
        CreditsAre(140_000);

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        (trip.TradeSymbol, trip.ReservedCredits).Should().Be(("EQUIPMENT", 130_160L));
        _savings.Largest().Should().Be(0);
    }

    [Fact]
    public async Task ATraderWhoseBestRouteItCanPayFor_SavesUpForNothing()
    {
        Fleet(CommandShip());
        _savings.SaveFor("SHIP-1", TradeRoutePlanner.RouteKey("MEDICINE", D41, A1), 194_922);
        _savings.SaveFor("SHIP-9", TradeRoutePlanner.RouteKey("MEDICINE", D41, A1), 194_922);

        await RunAsync();

        _savings.Largest().Should().Be(0, "SHIP-1 can pay for its best route, and SHIP-9 no longer trades");
    }

    [Fact]
    public async Task TwoTraders_NeverShareARoute()
    {
        // Full holds of EQUIPMENT and MEDICINE cost 325,000. The first trader takes EQUIPMENT for A1 (D82); EQUIPMENT for D41
        // comes next, but the first is on its way to buy EQUIPMENT at K85 (D80): the second takes MEDICINE.
        CreditsAre(500_000);
        Fleet(CommandShip(symbol: "SHIP-1"), CommandShip(symbol: "SHIP-4"));

        await RunAsync();

        var trips = _activeGoals.Values.Cast<TradeBetweenMarketsGoal>().ToList();
        trips.Should().HaveCount(2);
        trips.Select(trip => (trip.TradeSymbol, trip.BuyWaypointSymbol, trip.SellWaypointSymbol)).Should().OnlyHaveUniqueItems();
        trips.Should().Contain(trip => trip.SellWaypointSymbol == A1 && trip.TradeSymbol == "EQUIPMENT");
        trips.Should().Contain(trip => trip.TradeSymbol == "MEDICINE");
    }

    [Fact]
    public async Task ARouteATraderHolds_IsNotGivenToAnother()
    {
        _activeGoals["SHIP-4"] = new TradeBetweenMarketsGoal { TradeSymbol = "EQUIPMENT", BuyWaypointSymbol = K85, SellWaypointSymbol = D41 };
        Fleet(CommandShip(symbol: "SHIP-1"), CommandShip(symbol: "SHIP-4") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) });

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>()
            .Which.TradeSymbol.Should().Be("MEDICINE");
    }

    [Fact]
    public async Task TheCreditsATripHoldsBackOnItsWayToBuy_AreNotGivenToAnotherTrader()
    {
        // D57, asked on 2026-10-03: "Let's have these credits reserved as soon as a ship starts towards it, so that this cannot
        // happen (waste of time and fuel)." Seen at 19:29Z: SPECTER-8 set off to buy 15 EQUIPMENT (49,485) at K85; another
        // trader spent about 121,000 before it got there, leaving 54,596, and it dropped the trip with nothing bought. Here
        // SHIP-4 flies to K85 for 40 EQUIPMENT and holds back 130,160 of the 250,000, and EQUIPMENT at K85 with it (D80):
        // MEDICINE, SHIP-1's best route left, gets the 119,840 left, 24 units after its 242 for fuel (D79).
        _activeGoals["SHIP-4"] = new TradeBetweenMarketsGoal { TradeSymbol = "EQUIPMENT", BuyWaypointSymbol = K85, SellWaypointSymbol = D41, Units = 40, ReservedCredits = 130_160 };
        Fleet(CommandShip(symbol: "SHIP-1"), CommandShip(symbol: "SHIP-4") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) });

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        (trip.TradeSymbol, trip.Units, trip.ReservedCredits).Should().Be(("MEDICINE", 24, 24 * 4_867L));
    }

    [Fact]
    public async Task TheCreditsAConstructionTripHoldsBack_AreNotGivenToATrader()
    {
        // Slice 6.6 (D64): the jump gate's load holds back its cargo from the start, as a trade trip does (D57). Of the 250,000,
        // SHIP-6's 130,160 leave 119,840: 36 EQUIPMENT after the 152 for fuel, not 40 (D79).
        IReadOnlyDictionary<string, SupplyConstructionGoal> construction = new Dictionary<string, SupplyConstructionGoal>
        {
            ["SHIP-6"] = new() { TradeSymbol = "FAB_MATS", ConstructionSiteWaypointSymbol = "X1-AB-I55", BuyWaypointSymbol = K85, Units = 80, ReservedCredits = 130_160 },
        };
        _goals.GetActiveConstructionGoalsAsync(Arg.Any<CancellationToken>()).Returns(construction);
        Fleet(CommandShip(symbol: "SHIP-1"));

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        (trip.TradeSymbol, trip.Units).Should().Be(("EQUIPMENT", 36));
    }

    [Fact]
    public async Task MaterialsTheJumpGateStillNeeds_StayAboard_WhileTheConstructionPlanIsOn()
    {
        // Slice 6.6: no market here buys FAB_MATS, so D42 would jettison them; the construction plan has the ship supply them.
        _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Construction), Arg.Any<CancellationToken>()).Returns(true);
        _constructionSites.CachedNeedingMaterialsAsync(Arg.Any<CancellationToken>()).Returns([Construction.ConstructionFixture.Site()]);
        Fleet(CommandShip(cargo: [new CargoItemModel("FAB_MATS", 1)]));

        await RunAsync();

        await _jettison.DidNotReceiveWithAnyArgs().JettisonAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ATraderHoldingCargo_SellsItWhereItFetchesTheMostFirst()
    {
        Fleet(CommandShip(cargo: [new CargoItemModel("EQUIPMENT", 10)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        trip.TradeSymbol.Should().Be("EQUIPMENT");
        trip.SellWaypointSymbol.Should().Be(A1);
        trip.CargoBought.Should().BeTrue("there is nothing to buy");
        trip.PricePaidPerUnit.Should().Be(0);
        trip.Units.Should().Be(10);
    }

    [Fact]
    public async Task CargoNoMarketBuys_IsJettisoned_AndTheShipTradesWithItsWholeHold()
    {
        // D42 (it used to stay aboard for good): a unit of ore no market here buys, after a contract that is over.
        Fleet(CommandShip(cargo: [new CargoItemModel("COPPER_ORE", 1)]));

        await RunAsync();

        await _jettison.Received(1).JettisonAsync(
            Arg.Is<ShipModel>(ship => ship.Symbol == "SHIP-1"),
            new CargoItemModel("COPPER_ORE", 1),
            HeldCargo.NoBuyer,
            Arg.Any<CancellationToken>());
        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        trip.TradeSymbol.Should().Be("EQUIPMENT");
        trip.CargoBought.Should().BeFalse();
    }

    [Fact]
    public async Task TheContractsOre_StaysAboardAShipThatMinesForTheContract()
    {
        // D42's exception: ore the contract wants is earmarked for the ship's next delivery (D26).
        ContractWants("COPPER_ORE");
        Fleet(CommandShip(cargo: [new CargoItemModel("COPPER_ORE", 1)]));

        await RunAsync();

        await _jettison.DidNotReceiveWithAnyArgs().JettisonAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ASurveyorWithNothingToSurvey_AndNoSpareTimeTrip_SellsItsHoldWhereThatPays()
    {
        // D42: the copper the command ship mined for the contract (SPECTER-1's 7) was carried for good once it surveyed.
        SurveyPlanOn();
        Fleet(CommandShip(cargo: [new CargoItemModel("EQUIPMENT", 10)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        (trip.TradeSymbol, trip.SellWaypointSymbol, trip.CargoBought).Should().Be(("EQUIPMENT", A1, true));
    }

    [Fact]
    public async Task ASurveyorWithNothingToSurvey_JettisonsWhatNoMarketBuys_AndTakesNoRoute()
    {
        SurveyPlanOn();
        Fleet(CommandShip(cargo: [new CargoItemModel("COPPER_ORE", 7)]));

        await RunAsync();

        await _jettison.Received(1).JettisonAsync(Arg.Any<ShipModel>(), new CargoItemModel("COPPER_ORE", 7), HeldCargo.NoBuyer, Arg.Any<CancellationToken>());
        _activeGoals.Should().BeEmpty("a surveyor surveys, and doesn't trade (D20)");
    }

    [Fact]
    public async Task WithTheRoleBoardOn_TheCommandShipTrades_WhenItsRoleIsTrade_ThoughTheSurveyPlanIsOn()
    {
        // Slice 6.9: a ship that can only survey surveys, so the command ship takes what pays it most (D38).
        SurveyPlanOn();
        RolesAre(("SHIP-1", FleetRole.Trade));
        Fleet(CommandShip());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Which.TradeSymbol.Should().Be("EQUIPMENT");
    }

    [Fact]
    public async Task WithTheRoleBoardOn_AShipWithTheSurveyRole_OrNoRoleYet_DoesNotTrade()
    {
        SurveyPlanOn();
        RolesAre(("SHIP-1", FleetRole.Survey));
        Fleet(CommandShip(), Drone(symbol: "SHIP-3"));

        await RunAsync();

        _activeGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task AShipFreedAfterItsGatheringPlansPass_WaitsForThatPlan_InsteadOfTrading()
    {
        // B63, seen on the cluster on 2026-10-04: SPECTER-3, a mining drone with the mining role, sold its ore at H60 at
        // 19:50:06.749Z in its arrival's goal step, outside the tick, and tick 202's trading plan gave it a route 0.3 s
        // later. The mining plan had run earlier in that tick, while the drone was still on its trip, so it never passed
        // the drone over; at the next tick it would have given it a trip, as it did after the drone's other trips (D58:
        // drones gather first). A ship a gathering plan works with trades only when that plan passed it over.
        MiningPlanOn();
        RolesAre(("SHIP-1", FleetRole.Mine));
        Fleet(CommandShip());
        _passedOver.Record(AutomationPlan.Mining, own: ["SHIP-1"], passedOver: []);

        await RunAsync();

        _activeGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task AShipItsGatheringPlanPassedOver_Trades()
    {
        // D58: a ship with the mining role trades when the mining plan has no trip for it.
        MiningPlanOn();
        RolesAre(("SHIP-1", FleetRole.Mine));
        Fleet(CommandShip());
        _passedOver.Record(AutomationPlan.Mining, own: ["SHIP-1"], passedOver: ["SHIP-1"]);

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>();
    }

    [Fact]
    public async Task AGatheringPlanThatIsOff_HasNoSayInWhoTrades()
    {
        RolesAre(("SHIP-1", FleetRole.Mine));
        Fleet(CommandShip());
        _passedOver.Record(AutomationPlan.Mining, own: ["SHIP-1"], passedOver: []);

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>();
    }

    [Fact]
    public async Task AtAnAbundantSeller_ATraderTakesWhatBothMarketsTradeAtOnce_ThoughThatFillsNoHold()
    {
        // D74: D41 sells SHIP_PARTS 15 at a time, its supply ABUNDANT; the command ship's trip carries 15 in its 40-unit hold,
        // and holds back what those 15 cost (D57).
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(ShipPartsMap()));
        Fleet(CommandShip(D41));

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        trip.TradeSymbol.Should().Be("SHIP_PARTS");
        trip.Units.Should().Be(15);
        trip.ReservedCredits.Should().Be(15 * 7_721);
    }

    [Fact]
    public async Task TwoTraders_AreNeverSentForAGoodAtTheSameMarket_OneBuyerAtATime()
    {
        // D80, seen on 2026-10-05: at 05:33:20Z SPECTER-D and SPECTER-E, 80-unit haulers, were both sent for EQUIPMENT at K94,
        // one to sell at A4 and one at D52. D bought first, which raised K94's price and ended its ABUNDANT supply, and E dropped
        // its trip on arrival (`not_full_hold`), its fuel spent for nothing: 7 trips since the reset. Here D41 sells SHIP_PARTS,
        // and A1 and K85 both buy them: one trader gets them, the other waits.
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(ShipPartsSoldAtA1AndK85(), 500_000));
        Fleet(CommandShip(D41, symbol: "SHIP-1"), CommandShip(D41, symbol: "SHIP-4"));

        await RunAsync();

        _activeGoals.Values.Cast<TradeBetweenMarketsGoal>().Should().ContainSingle(trip => trip.TradeSymbol == "SHIP_PARTS" && trip.BuyWaypointSymbol == D41);
        _activeGoals.Should().HaveCount(1);
    }

    [Fact]
    public async Task ATripOnItsWayToBuy_HoldsItsGoodAtThatMarket_UntilItHasBought()
    {
        // D80: SHIP-4 flies to D41 for SHIP_PARTS to sell at A1. SHIP-1, there already, isn't sent for them to K85 meanwhile;
        // once SHIP-4 has bought, it is (D18 still keeps it off SHIP-4's route to A1).
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(ShipPartsSoldAtA1AndK85(), 500_000));
        var onItsWay = new TradeBetweenMarketsGoal { TradeSymbol = "SHIP_PARTS", BuyWaypointSymbol = D41, SellWaypointSymbol = A1, Units = 15, ReservedCredits = 15 * 7_721 };
        _activeGoals["SHIP-4"] = onItsWay;
        Fleet(CommandShip(D41, symbol: "SHIP-1"), CommandShip(symbol: "SHIP-4") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) });

        await RunAsync();

        _activeGoals.Should().NotContainKey("SHIP-1");

        _activeGoals["SHIP-4"] = onItsWay with { CargoBought = true, PricePaidPerUnit = 7_721 };

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Which.SellWaypointSymbol.Should().Be(K85);
    }

    [Fact]
    public async Task WithoutALucrativeRoute_TheTraderWaits()
    {
        Fleet(Drone());

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _log.Journal.Should().BeEmpty();
        _state!.Opportunities.Should().BeEmpty();
    }

    [Fact]
    public async Task TheState_ListsTheHeldRoutes_AndTheOpenOnesWithTheShipsThatCouldTakeThem()
    {
        Fleet(CommandShip());

        await RunAsync();

        var held = _state!.Opportunities.Should().ContainSingle(o => o.Status == MarketAutomationOpportunityStatus.Assigned).Subject;
        held.AssignedShipSymbol.Should().Be("SHIP-1");
        held.SellWaypointSymbol.Should().Be(A1);
        held.FeedsTradeSymbol.Should().BeEmpty();

        // Open for the ShipLeftIdle rule (D13): what SHIP-1 could have done instead, now that it is busy.
        _state.Opportunities.Where(o => o.Status == MarketAutomationOpportunityStatus.Pending)
            .Should().HaveCount(2)
            .And.OnlyContain(o => o.CandidateShipSymbols.SequenceEqual(new[] { "SHIP-1" }));
    }

    [Fact]
    public async Task TheState_SaysWhyEachGoodWithAPriceGap_IsNotTraded()
    {
        // Slice 2.18 (D76), asked on 2026-10-05: "Can the new list also add why the other goods are not considered for
        // trading?" SHIP-1 takes EQUIPMENT for A1, and EQUIPMENT for D41 and MEDICINE wait. FOOD and FUEL have a price gap, but
        // not even their first unit earns the 200 (D14, D79). SHIP_PARTS, which no market here buys, has none.
        Fleet(CommandShip());

        await RunAsync();

        _state!.NotTraded.Select(good => (good.SystemSymbol, good.TradeSymbol, good.Reason, good.ShipSymbol, good.BuyWaypointSymbol, good.SellWaypointSymbol))
            .Should().Equal(
                (SystemSymbol, "FOOD", "not_lucrative", "SHIP-1", K85, A1),
                (SystemSymbol, "FUEL", "not_lucrative", "SHIP-1", D41, K85));
        _state.NotTraded[0].Why.Should().Be("SHIP-1: a unit bought at 2,360 and sold at 2,492 earns 132 before fuel; each must earn 200 (D14, D79).");
        _state.NotTraded[0].JudgedAt.Should().Be(_state.UpdatedAt);
    }

    [Fact]
    public async Task TheState_SaysWhyNot_ForTheFreeTraderThatGotFurthest()
    {
        // SHIP-1, in orbit at K85 with 100 fuel aboard, can't reach D41; the shuttle there can buy SHIP_PARTS, but the first
        // unit earns 279, under the 300 a trip must earn a unit (D14, D79).
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(ShipPartsMap(supplyAtD41: "MODERATE"), 1_000_000, minProfitPerUnit: 300));
        Fleet(CommandShip(status: "IN_ORBIT", fuel: 100), Shuttle("SHIP-5") with { WaypointSymbol = D41 });

        await RunAsync();

        var parts = _state!.NotTraded.Should().ContainSingle(good => good.TradeSymbol == "SHIP_PARTS").Subject;
        (parts.Reason, parts.ShipSymbol).Should().Be(("not_lucrative", "SHIP-5"));
        parts.Why.Should().Be("SHIP-5: a unit bought at 7,721 and sold at 8,000 earns 279 before fuel; each must earn 300 (D14, D79).");
    }

    [Fact]
    public async Task TheState_KeepsTheReasonsAndTheirTime_WhileNoTraderIsFree()
    {
        // With every trader on a trip the plan checks no route: what its last pass with a free trader found stays.
        Fleet(CommandShip());
        await RunAsync();
        var reasons = _state!.NotTraded;
        reasons.Should().NotBeEmpty();

        Fleet(CommandShip() with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) });
        await RunAsync();

        _state.Opportunities.Should().ContainSingle("the routes that waited for SHIP-1 wait for no one now");
        _state.NotTraded.Should().Equal(reasons);
    }

    [Fact]
    public async Task AKeptReason_IsDropped_OnceItsGoodIsInTheList()
    {
        // A route of FOOD is held now, so FOOD is traded, though no free trader judged it again.
        Fleet(CommandShip());
        await RunAsync();
        _activeGoals["SHIP-1"] = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = K85, SellWaypointSymbol = A1, Units = 40 };
        Fleet(CommandShip() with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) });

        await RunAsync();

        _state!.NotTraded.Select(good => good.TradeSymbol).Should().Equal("FUEL");
    }

    [Fact]
    public async Task ALucrativeGood_WhoseRoutesAllRankBelowTheListedOnes_SaysSo()
    {
        // The state keeps the best 20 waiting routes. Here 22 goods are lucrative from K85 to D41, each 10 a unit dearer at D41
        // than the one before: SHIP-1 takes G22, and G01 ranks below the 20 that wait.
        var goods = Enumerable.Range(1, 22).Select(i => $"G{i:00}").ToList();
        var map = Map(
            Market(K85, [.. goods.Select(good => Good(good, "EXPORT", 100, 50, 40)), Good("FUEL", "EXCHANGE", 93, 79, 180)]),
            Market(D41, [.. goods.Select((good, i) => Good(good, "IMPORT", 3_000, 1_110 + (10 * i), 40)), Good("FUEL", "EXCHANGE", 76, 69, 180)]));
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(map));
        Fleet(CommandShip());

        await RunAsync();

        _state!.Opportunities.Select(route => route.TradeSymbol).Should().NotContain("G01").And.HaveCount(1 + TradingAutomationService.MaxPendingRoutes);
        var g01 = _state.NotTraded.Should().ContainSingle(good => good.TradeSymbol == "G01").Subject;
        g01.Reason.Should().Be("below_the_listed_routes");
        g01.Why.Should().Be("SHIP-1: lucrative, 40 units for 40,248 after fuel, 1,006 a unit. The 20 waiting routes listed rank higher.");
    }

    [Fact]
    public async Task WhileATraderWaitsForALucrativeRoute_TheReasonsAreWrittenOnce()
    {
        // The drone, with no lucrative route, is free at every pass, and the plan checks its routes again each time; the tick
        // runs every 5 seconds, and nothing changes.
        Fleet(Drone());

        await RunAsync();
        await RunAsync();
        await RunAsync();

        _state!.NotTraded.Should().NotBeEmpty();
        await _plans.Received(1).UpsertAsync(PlanTypes.TradingAutomation, Arg.Any<TradingAutomationPlanState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheState_IsWrittenOnlyWhenItChanges()
    {
        // The tick runs every 5 seconds; while nothing happens the plan writes nothing.
        _activeGoals["SHIP-1"] = new TradeBetweenMarketsGoal { TradeSymbol = "EQUIPMENT", BuyWaypointSymbol = K85, SellWaypointSymbol = D41, Units = 20 };
        Fleet(CommandShip() with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) });

        await RunAsync();
        await RunAsync();
        await RunAsync();

        await _plans.Received(1).UpsertAsync(PlanTypes.TradingAutomation, Arg.Any<TradingAutomationPlanState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AShipThatCanSurvey_DoesNotTrade_WhileTheSurveyPlanIsOn_AndTheSpareTimePlanOff()
    {
        // D20: the command ship surveys, and only that, until the spare-time plan is switched on.
        SurveyPlanOn();
        Fleet(CommandShip());

        await RunAsync();

        _activeGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task WithTheSpareTimePlanOn_AShipThatCanSurvey_TakesARouteThatWaitsForIt()
    {
        // D34 (2026-10-02): survey first, then trade, then mine or siphon. The survey plan goes first.
        SurveyPlanOn();
        SpareTimePlanOn();
        Fleet(CommandShip());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Which.TradeSymbol.Should().Be("EQUIPMENT");
    }

    [Fact]
    public async Task WithTheSpareTimePlanOn_AFreeSurveyorWithoutARoute_KeepsItsHold()
    {
        // D37: the spare-time plan fills the hold on and sells it once full; the trading plan takes the ship only
        // for a route that waits for it.
        SurveyPlanOn();
        SpareTimePlanOn();
        CreditsAre(1_000);
        Fleet(CommandShip(cargo: [new CargoItemModel("EQUIPMENT", 10)]));

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _log.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task ARouteThatWaitsForIt_TakesTheCommandShipOffItsSpareTimeTrip_AndItSellsItsHoldFirst()
    {
        // D34 (2026-10-02): "If a more important job comes up such as trading or surveying it should stop mining,
        // sell it's inventory and start on the new job." Sold at A1, its hold leaves it where EQUIPMENT from K85
        // to D41 and MEDICINE from D41 to A1 are lucrative.
        SurveyPlanOn();
        SpareTimePlanOn();
        var gathering = CommandShip(waypoint: Asteroid, status: "IN_ORBIT", cargo: [new CargoItemModel("EQUIPMENT", 10)]);
        _activeGoals["SHIP-1"] = new GatherAndSellGoal { SourceWaypointSymbol = Asteroid };
        Stored(gathering);
        Fleet(gathering);

        await RunAsync();

        var sale = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        (sale.TradeSymbol, sale.SellWaypointSymbol, sale.CargoBought).Should().Be(("EQUIPMENT", A1, true));
        _log.Journal.Select(entry => entry.EventKind).Should().Equal("GatheringInterrupted", "TradeStarted");
        _log.Journal[0].Properties["Reason"].Should().Be("trade");
    }

    [Fact]
    public async Task ARouteThatWaitsForIt_WithNothingAboard_TakesTheCommandShipOffItsTrip_ForTheRoute()
    {
        SurveyPlanOn();
        SpareTimePlanOn();
        var gathering = CommandShip(status: "IN_ORBIT");
        _activeGoals["SHIP-1"] = new GatherAndSellGoal { SourceWaypointSymbol = Asteroid };
        Stored(gathering);
        Fleet(gathering);

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        (trip.TradeSymbol, trip.BuyWaypointSymbol, trip.SellWaypointSymbol, trip.CargoBought).Should().Be(("EQUIPMENT", K85, A1, false));
    }

    [Fact]
    public async Task ASpareTimeTripThatSells_OrIsInFlight_IsNotInterruptedForATrade()
    {
        SurveyPlanOn();
        SpareTimePlanOn();
        var selling = new GatherAndSellGoal { SourceWaypointSymbol = Asteroid, Selling = true };
        var flying = new GatherAndSellGoal { SourceWaypointSymbol = Asteroid };
        _activeGoals["SHIP-1"] = selling;
        _activeGoals["SHIP-4"] = flying;
        Fleet(
            CommandShip(),
            CommandShip(symbol: "SHIP-4") with { Status = "IN_TRANSIT", DestWaypointSymbol = Asteroid, ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) });

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeSameAs(selling);
        _activeGoals["SHIP-4"].Should().BeSameAs(flying);
    }

    [Fact]
    public async Task WithTheSpareTimePlanOn_TheOtherTradersChooseFirst()
    {
        // From K85 the command ship would earn most with EQUIPMENT for D41; the shuttle, a trader only, takes it, and
        // the command ship takes MEDICINE, which is left. Full holds of both (D56) cost 325,000.
        SurveyPlanOn();
        SpareTimePlanOn();
        CreditsAre(500_000);
        Fleet(CommandShip(), Shuttle("SHIP-5"));

        await RunAsync();

        _activeGoals["SHIP-5"].Should().BeOfType<TradeBetweenMarketsGoal>().Which.TradeSymbol.Should().Be("EQUIPMENT");
        _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Which.TradeSymbol.Should().Be("MEDICINE");
    }

    [Fact]
    public async Task WithNoTraderFree_ItBuysALightShuttleFirst_WhenANewShipWouldHaveALucrativeRoute()
    {
        // D21: the command ship surveys; a shuttle bought at A1 could fly EQUIPMENT from K85 to D41.
        SurveyPlanOn();
        Fleet(CommandShip());
        CreditsAre(300_000);

        await RunAsync();

        await _purchases.Received(1).TryPurchaseAsync("SHIP_LIGHT_SHUTTLE", A1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhereOnlyTheExploringCommandShipIs_NoCargoShipIsBought()
    {
        // Asked on 2026-10-04: while the command ship explores, business stays home. X1-KR90 sells shuttles, and a shuttle
        // there would have a lucrative route; the command ship, exploring, is docked at its shipyard.
        const string Kr90 = "X1-KR90";
        SurveyPlanOn();
        Fleet(CommandShip(waypoint: A1) with { SystemSymbol = Kr90, Status = "DOCKED" });
        CreditsAre(300_000);
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new ShipAssignmentDto("SHIP-1", "Explore", A1, null, null, null, 0, DateTimeOffset.UtcNow, null)]);
        _tradeContexts.ReadAsync(Kr90, Arg.Any<CancellationToken>()).Returns(Context(Map()));
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = A1,
                SystemSymbol = Kr90,
                ShipTypes = ["SHIP_LIGHT_SHUTTLE"],
                Ships = [new ShipyardShipDto { Type = "SHIP_LIGHT_SHUTTLE", PurchasePrice = 117_273, FuelCapacity = 300, CargoCapacity = 40 }],
            },
        ]);

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task AfterTheShuttle_TheNextCargoShipsAreLightHaulers_AndThenOneMoreOfTheLastType_InTurnWithTheDrones()
    {
        // D21's list, then D43: "alternate drones and cargo ships", one more cargo ship of the list's last type at a time.
        // A light hauler's 80-unit hold needs markets that trade 80 at once (D56): K85 and D41 do EQUIPMENT here.
        SurveyPlanOn();
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(MapWhereEquipmentFillsAHauler(), 1_000_000));
        var shuttle = Shuttle("SHIP-5") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) };
        var hauler = Shuttle("SHIP-6") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) };
        Fleet(CommandShip(), shuttle, hauler);

        await RunAsync();

        _order.Of(AutomationPlan.Trading).Should().Be(new PurchaseNeed(PurchaseTier.CargoShips, "SHIP_LIGHT_HAULER", A1, 354_210));
        await _purchases.Received(1).TryPurchaseAsync("SHIP_LIGHT_HAULER", A1, Arg.Any<CancellationToken>());

        _purchases.ClearReceivedCalls();
        Fleet(CommandShip(), shuttle, hauler, hauler with { Symbol = "SHIP-7" });

        await RunAsync();

        _order.Of(AutomationPlan.Trading).Should().Be(new PurchaseNeed(PurchaseTier.Alternating, "SHIP_LIGHT_HAULER", A1, 354_210));
        await _purchases.Received(1).TryPurchaseAsync("SHIP_LIGHT_HAULER", A1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACargoShipOfTheList_IsSavedUpFor_ThoughATraderHasNoTripYet()
    {
        // D43: "then save up for cargo ships". The drone has no lucrative route, so nothing is bought now, but nothing after
        // the shuttle in the order is bought either.
        CreditsAre(300_000);
        Fleet(Drone());

        await RunAsync();

        _order.Of(AutomationPlan.Trading).Should().Be(new PurchaseNeed(PurchaseTier.CargoShips, "SHIP_LIGHT_SHUTTLE", A1, 117_273));
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task BeyondTheList_ACargoShipIsNoNeed_WhileATraderHasNoTrip_SoTheDronesTurnMayCome()
    {
        // D43: "a turn passes when the other kind has nothing to buy".
        Fleet(Drone(), Shuttle("SHIP-5"), Shuttle("SHIP-6"), Shuttle("SHIP-7"));
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(Map(), 1_000_000, minProfitPerUnit: 100_000));

        await RunAsync();

        _order.Of(AutomationPlan.Trading).Should().Be(PurchaseNeed.None);
    }

    [Fact]
    public async Task WhileSomethingComesFirstInTheOrder_NoCargoShipIsBought()
    {
        // D43: the contract's drone, a surveyor and a drone for each scarce mineral come before the cargo ships.
        SurveyPlanOn();
        Fleet(CommandShip());
        CreditsAre(300_000);
        _order.Allows = false;

        await RunAsync();

        _order.Of(AutomationPlan.Trading).Tier.Should().Be(PurchaseTier.CargoShips);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task WhileATraderHasNoTrip_NoShipIsBought()
    {
        // The drone finds no lucrative route: a new ship waits until every trader is busy.
        CreditsAre(300_000);
        Fleet(Drone());

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task NoShipIsBought_WhenANewOneWouldHaveNoLucrativeRoute()
    {
        // After the shuttle's price, 2,727 credits buy one unit of FOOD at most, at 132 a unit before fuel.
        SurveyPlanOn();
        Fleet(CommandShip());
        CreditsAre(120_000);

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private void RolesAre(params (string Ship, FleetRole Role)[] roles) => RoleBoardTestSupport.RolesAre(_settings, _plans, roles);

    private void ContractWants(string ore)
    {
        _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Contract), Arg.Any<CancellationToken>()).Returns(true);
        _contractPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-1",
            ShipSymbol = "SHIP-3",
            TradeSymbol = ore,
            SourceWaypoint = Asteroid,
            DestinationWaypoint = A1,
            UnitsRequired = 40,
            UnitsFulfilled = 10,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
    }

    private void SurveyPlanOn()
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Survey), Arg.Any<CancellationToken>()).Returns(true);

    private void MiningPlanOn()
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Mining), Arg.Any<CancellationToken>()).Returns(true);

    private void SpareTimePlanOn()
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.SpareTime), Arg.Any<CancellationToken>()).Returns(true);

    /// <summary>The ship as stored, which the spare-time interruption reads again (B17).</summary>
    private void Stored(ShipModel ship) => _ships.FindAsync(ship.Symbol, Arg.Any<CancellationToken>()).Returns(ship);

    private void CreditsAre(long credits)
        => _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(Map(), credits));

    /// <summary>The fixture, with K85 and D41 trading EQUIPMENT 80 at a time: a light hauler's hold (D56).</summary>
    private static TradeMarketMap MapWhereEquipmentFillsAHauler()
        => Map(
            Market(K85, Good("EQUIPMENT", "EXPORT", 3_254, 1_456, 80), Good("FOOD", "EXPORT", 2_360, 1_069, 60), Good("FUEL", "EXCHANGE", 93, 79, 180)),
            Market(D41, Good("EQUIPMENT", "IMPORT", 7_032, 3_487, 80), Good("MEDICINE", "EXPORT", 4_867, 2_227, 40), Good("FUEL", "EXCHANGE", 76, 69, 180)),
            A1Market());

    /// <summary>
    /// For D80: D41 sells SHIP_PARTS 15 at a time at 7,721; A1 pays 8,000 for them and K85 7,990, 40 at a time. Nothing else
    /// trades at a profit.
    /// </summary>
    private static TradeMarketMap ShipPartsSoldAtA1AndK85()
        => Map(
            Market(K85, Good("SHIP_PARTS", "IMPORT", 16_000, 7_990, 40), Good("FUEL", "EXCHANGE", 93, 79, 180)),
            Market(D41, Good("SHIP_PARTS", "EXPORT", 7_721, 3_478, 15, "ABUNDANT"), Good("FUEL", "EXCHANGE", 76, 69, 180)),
            Market(A1, Good("SHIP_PARTS", "IMPORT", 16_000, 8_000, 40), Good("FUEL", "EXCHANGE", 90, 76, 180)));

    /// <summary>A cargo ship, as the trading plan buys them: a hold and a tank, nothing to mine or survey with.</summary>
    private static ShipModel Shuttle(string symbol)
        => new(symbol, SystemSymbol, A1, "DOCKED", "CRUISE", 300, 300, CargoCapacity: 40, ShipType: "SHIP_LIGHT_SHUTTLE", MountSymbols: ["MOUNT_TURRET_I"], CargoInventory: []);

    private Task RunAsync()
        => new TradingAutomationService(
                _ships,
                _goals,
                _assignments,
                _tradeContexts,
                _plans,
                _settings,
                _shipyards,
                _purchases,
                new SpareTimeInterruption(_ships, _goals, _stepGuard, Substitute.For<ITripBook>(), _log.For<SpareTimeInterruption>()),
                _contractPlans,
                _jettison,
                _order,
                _savings,
                _constructionSites,
                _passedOver,
                _log.For<TradingAutomationService>())
            .EnsureBootstrappedAsync();
}
