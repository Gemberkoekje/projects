using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Goals;
using Wolverine;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// D54: a move is one flight to a waypoint, and the goal ends there. The survey plan moves a ship that can only survey to
/// the area where most drones mine, out of its CRUISE reach: such a move drifts (D45).
/// </summary>
public sealed class MoveToWaypointGoalExecutorTests
{
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly LogRecorder _log = new();

    /// <summary>The survey ship's move to B7, beyond its 80-unit tank's CRUISE reach.</summary>
    private static readonly MoveToWaypointGoal Drift = new() { TargetWaypointSymbol = B7, Drifting = true };

    public MoveToWaypointGoalExecutorTests()
    {
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new TradeContext(Map(), 129_357, 200));
    }

    [Fact]
    public async Task AMoveOutOfReach_DriftsThere_AndJournalsIt()
    {
        var result = await StepAsync(SurveyShip(), Drift);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.ShipSymbol == "SHIP-5" && command.DestinationWaypoint == B7 && command.FlightMode == "DRIFT"),
            Arg.Any<CancellationToken>());
        var drift = _log.Journal.Should().ContainSingle().Subject;
        drift.EventKind.Should().Be("DriftStarted");
        drift.Message.Should().Contain("SHIP-5").And.Contain(XB5C).And.Contain(B7);
    }

    [Fact]
    public async Task AMoveInReach_FliesThereInCruise()
    {
        var result = await StepAsync(SurveyShip(), new MoveToWaypointGoal { TargetWaypointSymbol = H51 });

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == H51 && command.FlightMode == "CRUISE"),
            Arg.Any<CancellationToken>());
        _log.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task InTransit_ItWaits()
    {
        var result = await StepAsync(SurveyShip() with { Status = "IN_TRANSIT", DestWaypointSymbol = B7 }, Drift);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task There_TheGoalEnds()
    {
        // Its arrival docked it at B7; the survey plan gives it its next survey from there.
        var result = await StepAsync(SurveyShip(waypoint: B7) with { Status = "DOCKED", FlightMode = "DRIFT", FuelCurrent = 79 }, Drift);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-5", Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>());
    }

    private Task<GoalExecutionResult> StepAsync(ShipModel ship, MoveToWaypointGoal goal)
        => new MoveToWaypointGoalExecutor(_goals, _tradeContexts, _dock, _bus, _log.For<MoveToWaypointGoalExecutor>())
            .ExecuteStepAsync(ship, goal, new ShipGoalContext(), CancellationToken.None);
}
