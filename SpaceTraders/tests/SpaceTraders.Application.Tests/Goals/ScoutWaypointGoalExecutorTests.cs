using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Phase 10: unit tests for <see cref="ScoutWaypointGoalExecutor"/>.
/// </summary>
public sealed class ScoutWaypointGoalExecutorTests
{
    private readonly IInOrbitCommandAcceptor _inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
    private readonly IDockedCommandAcceptor _docked = Substitute.For<IDockedCommandAcceptor>();
    private readonly IWaypointVisitService _waypointVisit = Substitute.For<IWaypointVisitService>();
    private readonly IMarketRefreshService _marketRefresh = Substitute.For<IMarketRefreshService>();
    private readonly IShipyardRefreshService _shipyardRefresh = Substitute.For<IShipyardRefreshService>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private ScoutWaypointGoalExecutor CreateExecutor() =>
        new(_inOrbit, _docked, _waypointVisit, _marketRefresh, _shipyardRefresh, _bus);

    private static ShipModel Ship(string waypoint = "X1-AB-WP1", string status = "IN_ORBIT", int fuelCap = 100) =>
        new("SHIP-1", "X1-AB", waypoint, status, "CRUISE", 80, fuelCap);

    private static ScoutWaypointGoal Goal(string target = "X1-AB-TARGET") =>
        new() { TargetWaypointSymbol = target };

    [Fact]
    public void CanExecute_ReturnsTrue_ForScoutWaypointGoal()
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
        var result = await CreateExecutor().ExecuteStepAsync(Ship(status: "IN_TRANSIT"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
    }

    [Fact]
    public async Task ExecuteStepAsync_InOrbit_NotAtTarget_Navigates()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1"), Goal("X1-AB-TARGET"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-TARGET", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_InOrbit_AtTarget_Docks()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-TARGET"), Goal("X1-AB-TARGET"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).DockAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_AtTarget_RefreshesDataAndCompletes()
    {
        var ship = Ship("X1-AB-TARGET", "DOCKED");
        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-TARGET"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _waypointVisit.Received(1).MarkVisitedAsync("X1-AB-TARGET", Arg.Any<CancellationToken>());
        await _marketRefresh.Received(1).RefreshIfApplicableAsync("X1-AB-TARGET", Arg.Any<CancellationToken>());
        await _shipyardRefresh.Received(1).RefreshIfApplicableAsync("X1-AB-TARGET", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_NotAtTarget_Orbits()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1", "DOCKED"), Goal("X1-AB-TARGET"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_NoFuelTank_SwitchesToDrift()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1", fuelCap: 0), Goal("X1-AB-TARGET"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        result.Reason.Should().Contain("DRIFT");
    }
}
