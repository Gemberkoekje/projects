using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.5: every free ship with a cargo hold and a fuel tank trades; the command ship is the first,
/// once it has scouted. Two traders never share a route, and the plan buys no ships (D16).
/// </summary>
public sealed class TradingAutomationServiceTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
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
    public async Task CargoNoMarketBuys_StaysAboard_AndTheShipTradesWithTheRestOfItsHold()
    {
        // The drone after its contract, with a unit of ore no market here buys.
        Fleet(CommandShip(cargo: [new CargoItemModel("COPPER_ORE", 1)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<TradeBetweenMarketsGoal>().Subject;
        trip.TradeSymbol.Should().Be("EQUIPMENT");
        trip.CargoBought.Should().BeFalse();
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

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private Task RunAsync()
        => new TradingAutomationService(
                _ships,
                _goals,
                _assignments,
                _tradeContexts,
                _plans,
                _log.For<TradingAutomationService>())
            .EnsureBootstrappedAsync();
}
