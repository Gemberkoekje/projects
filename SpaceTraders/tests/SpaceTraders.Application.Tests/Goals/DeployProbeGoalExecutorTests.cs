using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

public sealed class DeployProbeGoalExecutorTests
{
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IProbeDeploymentPlanService _probeDeploymentPlan = Substitute.For<IProbeDeploymentPlanService>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private DeployProbeGoalExecutor CreateExecutor() =>
        new(
            _goals,
            _probeDeploymentPlan,
            _dock,
            _bus,
            NullLogger<DeployProbeGoalExecutor>.Instance);

    private static DeployProbeGoal Goal() => new() { TargetWaypointSymbol = "X1-AB-MKT" };

    private static ShipModel Probe(string waypoint, string status) =>
        new("PROBE-1", "X1-AB", waypoint, status, "CRUISE", 0, 0);

    [Fact]
    public async Task ExecuteStepAsync_SetsDriftAdvancesPlanAndClearsGoal_WhenDockedAtTarget()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Probe("X1-AB-MKT", "DOCKED"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<PatchShipNavCommand>(c => c.ShipSymbol == "PROBE-1" && c.FlightMode == "DRIFT"),
            Arg.Any<CancellationToken>());
        await _probeDeploymentPlan.Received(1).AdvanceAsync("X1-AB-MKT", Arg.Any<CancellationToken>());
        await _goals.Received(1).ClearActiveGoalAsync("PROBE-1", Arg.Any<CancellationToken>());
        await _dock.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docks_WhenInOrbitAtTarget()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Probe("X1-AB-MKT", "IN_ORBIT"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _dock.Received(1).ExecuteAsync("PROBE-1", Arg.Any<CancellationToken>());
        await _probeDeploymentPlan.DidNotReceive().AdvanceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _goals.DidNotReceive().ClearActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_NavigatesToTarget_WhenElsewhere()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Probe("X1-AB-HQ", "DOCKED"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.ShipSymbol == "PROBE-1" && c.DestinationWaypoint == "X1-AB-MKT"),
            Arg.Any<CancellationToken>());
        await _dock.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _probeDeploymentPlan.DidNotReceive().AdvanceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
