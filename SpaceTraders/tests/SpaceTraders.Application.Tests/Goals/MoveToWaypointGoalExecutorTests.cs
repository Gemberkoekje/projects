using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Phase 10: unit tests for <see cref="MoveToWaypointGoalExecutor"/>.
/// </summary>
public sealed class MoveToWaypointGoalExecutorTests
{
    private readonly IInOrbitCommandAcceptor _inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
    private readonly IDockedCommandAcceptor _docked = Substitute.For<IDockedCommandAcceptor>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private MoveToWaypointGoalExecutor CreateExecutor() => new(_inOrbit, _docked, _bus);

    private static ShipModel Ship(string waypoint, string status = "IN_ORBIT", string flightMode = "CRUISE", int fuel = 80, int fuelCap = 100) =>
        new("SHIP-1", "X1-AB", waypoint, status, flightMode, fuel, fuelCap);

    private static MoveToWaypointGoal Goal(string target = "X1-AB-WP2") =>
        new() { TargetWaypointSymbol = target };

    private static ShipGoalContext Ctx(string recommended = "") => new() { RecommendedFlightMode = recommended };

    [Fact]
    public void CanExecute_ReturnsTrue_ForMoveToWaypointGoal()
    {
        CreateExecutor().CanExecute(Goal()).Should().BeTrue();
    }

    [Fact]
    public void CanExecute_ReturnsFalse_ForOtherGoal()
    {
        CreateExecutor().CanExecute(new IdleGoal()).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteStepAsync_InTransit_ReturnsWaitingForArrival()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1", "IN_TRANSIT"), Goal(), Ctx(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_Orbits()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1", "DOCKED"), Goal(), Ctx(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_AlreadyAtTarget_Completes()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP2"), Goal("X1-AB-WP2"), Ctx(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
    }

    [Fact]
    public async Task ExecuteStepAsync_InOrbit_NotAtTarget_Navigates()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1"), Goal("X1-AB-WP2"), Ctx(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-WP2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_NoFuelTank_PatchesDriftAndNavigates()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1", "IN_ORBIT", "CRUISE", 0, 0), Goal(), Ctx(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(Arg.Is<PatchShipNavCommand>(c => c.FlightMode == "DRIFT"), Arg.Any<CancellationToken>());
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_WrongFlightMode_PatchesAndNavigates()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1", "IN_ORBIT", "CRUISE"), Goal(), Ctx(recommended: "BURN"), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(Arg.Is<PatchShipNavCommand>(c => c.FlightMode == "BURN"), Arg.Any<CancellationToken>());
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
