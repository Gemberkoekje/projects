using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Tests.Roles;
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
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private TradingAutomationPlanState? _state;

    public TradingAutomationServiceTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(Map()));
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
        // Scouting done (B10): no goal and no assignment, docked where the scout plan ended.
        Fleet(CommandShip());

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        trip.TradeSymbol.Should().Be("EQUIPMENT");
        trip.BuyWaypointSymbol.Should().Be(K85);
        trip.SellWaypointSymbol.Should().Be(D41);
        trip.Units.Should().Be(20);
        trip.ExpectedProfit.Should().Be((233 * 20) - (2 * 76));
        trip.FeedsTradeSymbol.Should().Be("SHIP_PARTS");
        trip.CargoBought.Should().BeFalse();

        var started = _log.Journal.Should().ContainSingle().Subject;
        started.EventKind.Should().Be("TradeStarted");
        started.Properties["ShipSymbol"].Should().Be("SHIP-1");
        started.Properties["FeedsTradeSymbol"].Should().Be("SHIP_PARTS");
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
    public async Task TwoTraders_NeverShareARoute()
    {
        Fleet(CommandShip(symbol: "SHIP-1"), CommandShip(symbol: "SHIP-4"));

        await RunAsync();

        var trips = _activeGoals.Values.Cast<TradeBetweenMarketsGoal>().ToList();
        trips.Should().HaveCount(2);
        trips.Select(trip => (trip.TradeSymbol, trip.BuyWaypointSymbol, trip.SellWaypointSymbol)).Should().OnlyHaveUniqueItems();
        trips.Should().Contain(trip => trip.SellWaypointSymbol == D41 && trip.TradeSymbol == "EQUIPMENT");
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
        held.SellWaypointSymbol.Should().Be(D41);
        held.FeedsTradeSymbol.Should().Be("SHIP_PARTS");

        // Open for the ShipLeftIdle rule (D13): what SHIP-1 could have done instead, now that it is busy.
        _state.Opportunities.Where(o => o.Status == MarketAutomationOpportunityStatus.Pending)
            .Should().HaveCount(2)
            .And.OnlyContain(o => o.CandidateShipSymbols.SequenceEqual(new[] { "SHIP-1" }));
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
        (trip.TradeSymbol, trip.BuyWaypointSymbol, trip.SellWaypointSymbol, trip.CargoBought).Should().Be(("EQUIPMENT", K85, D41, false));
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
        // the command ship takes MEDICINE, which is left.
        SurveyPlanOn();
        SpareTimePlanOn();
        CreditsAre(300_000);
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
    public async Task AfterTheShuttle_TheNextCargoShipsAreLightHaulers_UpToTwo()
    {
        SurveyPlanOn();
        CreditsAre(1_000_000);
        var shuttle = Shuttle("SHIP-5") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) };
        var hauler = Shuttle("SHIP-6") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) };
        Fleet(CommandShip(), shuttle, hauler);

        await RunAsync();

        await _purchases.Received(1).TryPurchaseAsync("SHIP_LIGHT_HAULER", A1, Arg.Any<CancellationToken>());

        _purchases.ClearReceivedCalls();
        Fleet(CommandShip(), shuttle, hauler, hauler with { Symbol = "SHIP-7" });

        await RunAsync();

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

    private void SpareTimePlanOn()
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.SpareTime), Arg.Any<CancellationToken>()).Returns(true);

    /// <summary>The ship as stored, which the spare-time interruption reads again (B17).</summary>
    private void Stored(ShipModel ship) => _ships.FindAsync(ship.Symbol, Arg.Any<CancellationToken>()).Returns(ship);

    private void CreditsAre(long credits)
        => _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(Map(), credits));

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
                new SpareTimeInterruption(_ships, _goals, _stepGuard, _log.For<SpareTimeInterruption>()),
                _contractPlans,
                _jettison,
                _log.For<TradingAutomationService>())
            .EnsureBootstrappedAsync();
}
