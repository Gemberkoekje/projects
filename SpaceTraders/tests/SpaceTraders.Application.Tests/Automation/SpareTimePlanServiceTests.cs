using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.SpareTime.SpareTimeFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.8, asked on 2026-10-02: "I'd like my command ship not to be idle." With nothing to survey and no trade
/// (D34), the command ship mines or siphons at the nearest place it can (D35), keeping whatever sells, and sells it
/// (D36). The plan goes last, so it only gets a ship every other plan left free.
/// </summary>
public sealed class SpareTimePlanServiceTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly ITradeContextReader _contexts = Substitute.For<ITradeContextReader>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private SpareTimePlanState? _state;

    public SpareTimePlanServiceTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _goals.When(goals => goals.SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _activeGoals[call.ArgAt<string>(0)] = call.ArgAt<ShipGoal>(1));
        _plans.GetAsync<SpareTimePlanState>(PlanTypes.SpareTime, Arg.Any<CancellationToken>()).Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(PlanTypes.SpareTime, Arg.Any<SpareTimePlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.ArgAt<SpareTimePlanState>(1));
        SurveyPlanIs(on: true);
    }

    [Fact]
    public async Task TheCommandShip_WithNothingToSurveyOrTrade_MinesAtTheNearestAsteroid()
    {
        Fleet(CommandShip(XB5C));

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<GatherAndSellGoal>().Subject;
        (trip.SourceWaypointSymbol, trip.Siphoning, trip.Selling).Should().Be((XB5C, false, false));

        var started = _log.Journal.Should().ContainSingle().Subject;
        started.EventKind.Should().Be("GatheringStarted");
        started.Properties["WaypointSymbol"].Should().Be(XB5C);
        started.Properties["Method"].Should().Be("mines");

        _state!.Ships.Should().Equal(new SpareTimeShipState { ShipSymbol = "SHIP-1", Activity = SpareTimeActivity.Gathering, SourceWaypointSymbol = XB5C });
    }

    [Fact]
    public async Task AtTheGasStation_ItSiphonsAtTheGasGiant()
    {
        // D35: the nearest place it can work, whichever it is; C38 is next to C39, XB5C 169 away.
        Fleet(CommandShip(C39, "DOCKED"));

        await RunAsync();

        var trip = _activeGoals["SHIP-1"].Should().BeOfType<GatherAndSellGoal>().Subject;
        (trip.SourceWaypointSymbol, trip.Siphoning).Should().Be((C38, true));
        _log.Journal.Should().ContainSingle().Which.Properties["Method"].Should().Be("siphons");
    }

    [Fact]
    public async Task WithTheSurveyPlanOff_TheCommandShipIsAMiner_AndGetsNoSpareTimeTrip()
    {
        // The mining plan gives it work then (6.4).
        SurveyPlanIs(on: false);
        Fleet(CommandShip(XB5C));

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _state!.Ships.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyAShipThatSurveys_HasSpareTime()
    {
        // Drones, siphon drones and cargo ships have plans of their own.
        var drone = new ShipModel("SHIP-3", SystemSymbol, XB5C, "IN_ORBIT", "CRUISE", 80, 80, CargoCapacity: 15, ShipType: "SHIP_MINING_DRONE", MountSymbols: ["MOUNT_MINING_LASER_I"]);
        var siphonDrone = new ShipModel("SHIP-5", SystemSymbol, C38, "IN_ORBIT", "CRUISE", 80, 80, CargoCapacity: 15, ShipType: "SHIP_SIPHON_DRONE", MountSymbols: ["MOUNT_GAS_SIPHON_I"]);
        var shuttle = new ShipModel("SHIP-6", SystemSymbol, H51, "DOCKED", "CRUISE", 300, 300, CargoCapacity: 40, ShipType: "SHIP_LIGHT_SHUTTLE", MountSymbols: ["MOUNT_TURRET_I"]);
        Fleet(drone, siphonDrone, shuttle);

        await RunAsync();

        _activeGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task AShipWorkingForAnotherPlan_IsLeftAlone()
    {
        _activeGoals["SHIP-1"] = new SurveyWaypointGoal { TargetWaypointSymbol = XB5C, TargetDepositSymbol = "COPPER_ORE" };
        _activeGoals["SHIP-2"] = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = H51, SellWaypointSymbol = F49 };
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new ShipAssignmentDto("SHIP-4", "Contract", XB5C, H51, "COPPER_ORE", "C-1", 0, DateTimeOffset.UtcNow, null)]);
        var flying = CommandShip(H51, "IN_TRANSIT", symbol: "SHIP-7") with { DestWaypointSymbol = F49, ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(2) };
        Fleet(CommandShip(), CommandShip(symbol: "SHIP-2"), CommandShip(symbol: "SHIP-4"), flying);

        await RunAsync();

        _activeGoals.Should().HaveCount(2).And.NotContainKey("SHIP-4").And.NotContainKey("SHIP-7");
        _state!.Ships.Should().OnlyContain(ship => ship.Activity == SpareTimeActivity.Busy && ship.SourceWaypointSymbol.Length == 0);
        _log.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task AShipOnATrip_KeepsIt()
    {
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C, Selling = true };
        _activeGoals["SHIP-1"] = trip;
        Fleet(CommandShip(H51, "DOCKED"));

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeSameAs(trip);
        _state!.Ships.Should().Equal(new SpareTimeShipState { ShipSymbol = "SHIP-1", Activity = SpareTimeActivity.Selling, SourceWaypointSymbol = XB5C });
    }

    [Fact]
    public async Task AShipWhoseTripWasBlocked_GetsANewOne()
    {
        _activeGoals["SHIP-1"] = new GatherAndSellGoal { SourceWaypointSymbol = XB5C, Status = GoalStatus.Blocked, StatusReason = "runaway" };
        Fleet(CommandShip(XB5C));

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<GatherAndSellGoal>().Which.Status.Should().Be(GoalStatus.Assigned);
    }

    [Fact]
    public async Task NowhereItCanGatherAndSell_TheShipWaits()
    {
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(Market(XB5C, Good("FUEL", "EXCHANGE", 97, 82, 180))));
        Fleet(CommandShip(XB5C));

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _state!.Ships.Should().Equal(new SpareTimeShipState { ShipSymbol = "SHIP-1", Activity = SpareTimeActivity.Waiting });
    }

    [Fact]
    public async Task AFullHoldThatNoMarketBuys_GetsNoTrip()
    {
        // A trip would turn to selling at once, find nothing to sell and end, on every tick.
        Fleet(CommandShip(XB5C, cargo: [new CargoItemModel("EXOTIC_MATTER", 40)]));

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _log.Journal.Should().BeEmpty();
        _state!.Ships.Should().ContainSingle().Which.Activity.Should().Be(SpareTimeActivity.Waiting);
    }

    [Fact]
    public async Task TheState_IsWrittenOnlyWhenItChanges()
    {
        // The tick runs every 5 seconds; while nothing happens the plan writes nothing.
        _activeGoals["SHIP-1"] = new GatherAndSellGoal { SourceWaypointSymbol = XB5C };
        Fleet(CommandShip(XB5C));

        await RunAsync();
        await RunAsync();
        await RunAsync();

        await _plans.Received(1).UpsertAsync(PlanTypes.SpareTime, Arg.Any<SpareTimePlanState>(), Arg.Any<CancellationToken>());
    }

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private void SurveyPlanIs(bool on)
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Survey), Arg.Any<CancellationToken>()).Returns(on);

    private Task RunAsync()
        => new SpareTimePlanService(
                _ships,
                _goals,
                _assignments,
                _contexts,
                _plans,
                _settings,
                _log.For<SpareTimePlanService>())
            .EnsureBootstrappedAsync();
}
