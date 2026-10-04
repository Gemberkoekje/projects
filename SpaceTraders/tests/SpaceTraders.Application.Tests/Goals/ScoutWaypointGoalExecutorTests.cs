using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Goals;
using Wolverine;
using static SpaceTraders.Application.Tests.Commands.FuelStopFixture;

namespace SpaceTraders.Application.Tests.Goals;

public sealed class ScoutWaypointGoalExecutorTests
{
    private readonly IWaypointVisitService _waypointVisit = Substitute.For<IWaypointVisitService>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    public ScoutWaypointGoalExecutorTests()
    {
        _tradeContexts.ReadAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns(new TradeContext(new TradeMarketMap([], [], new Dictionary<string, IReadOnlyList<string>>()), 0, 0));
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
    }

    private ScoutWaypointGoalExecutor CreateExecutor() => new(_waypointVisit, _tradeContexts, _dock, _bus);

    private static ScoutWaypointGoal Goal() => new() { TargetWaypointSymbol = "X1-AB-MKT" };

    private static ShipModel Ship(string waypoint, string status) =>
        new("SCOUT-1", "X1-AB", waypoint, status, "CRUISE", 100, 100);

    [Fact]
    public async Task ExecuteStepAsync_MarksVisitedAndCompletes_WhenDockedAtTarget()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-MKT", "DOCKED"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _waypointVisit.Received(1).MarkVisitedAsync("X1-AB-MKT", Arg.Any<CancellationToken>());
        await _dock.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docks_WhenInOrbitAtTarget()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-MKT", "IN_ORBIT"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _dock.Received(1).ExecuteAsync("SCOUT-1", Arg.Any<CancellationToken>());
        await _waypointVisit.DidNotReceive().MarkVisitedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_NavigatesToTarget_WhenElsewhere()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-HQ", "DOCKED"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.ShipSymbol == "SCOUT-1" && c.DestinationWaypoint == "X1-AB-MKT"),
            Arg.Any<CancellationToken>());
        await _dock.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_ToATargetBeyondOneTank_FliesInCruise_ToTheFirstFuelStop()
    {
        // B47: the scout plan's flights asked for no flight mode and made no refuelling stops, so a target beyond one tank
        // was left to the navigation's fallback, which drifts. From J67, EF5D is 747 away; J66 is on the way.
        var goal = new ScoutWaypointGoal { TargetWaypointSymbol = EF5D };

        var result = await CreateExecutor().ExecuteStepAsync(CommandShip(), goal, new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.DestinationWaypoint == J66 && c.FlightMode == "CRUISE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_AShipLeftInDrift_FliesToTheTargetInCruise()
    {
        // B47: a ship the fallback left in DRIFT flies on in DRIFT, ten times slower, unless its flight asks for CRUISE.
        var goal = new ScoutWaypointGoal { TargetWaypointSymbol = EF5D };

        await CreateExecutor().ExecuteStepAsync(CommandShip(H60, flightMode: "DRIFT"), goal, new ShipGoalContext(), CancellationToken.None);

        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.DestinationWaypoint == EF5D && c.FlightMode == "CRUISE"),
            Arg.Any<CancellationToken>());
    }
}
