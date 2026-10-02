using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>Slice 6.3: one flight of a probe per goal, in CRUISE; the arrival fetches the market.</summary>
public sealed class DeployProbeGoalExecutorTests
{
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    [Theory]
    [InlineData("DOCKED")]
    [InlineData("IN_ORBIT")]
    public async Task AtItsMarket_TheFlightIsOver(string status)
    {
        // The arrival already fetched the market and docked; the probe plan chooses the next market.
        var result = await StepAsync(Probe("X1-AB-MKT", status));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _goals.Received(1).ClearActiveGoalAsync("PROBE-1", Arg.Any<CancellationToken>());
        _bus.ReceivedCalls().Should().BeEmpty("the old plan set DRIFT here, which made the next flight ten times slower");
    }

    [Fact]
    public async Task Elsewhere_ItFliesThere()
    {
        var result = await StepAsync(Probe("X1-AB-HQ", "DOCKED"));

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.ShipSymbol == "PROBE-1" && c.DestinationWaypoint == "X1-AB-MKT"),
            Arg.Any<CancellationToken>());
        await _goals.DidNotReceiveWithAnyArgs().ClearActiveGoalAsync(default!, default);
    }

    [Fact]
    public async Task AProbeInDrift_SwitchesToCruise_BeforeItFlies()
    {
        // A probe has no tank, so CRUISE costs it nothing; the old plan parked probes in DRIFT (B47).
        var result = await StepAsync(Probe("X1-AB-HQ", "DOCKED") with { FlightMode = "DRIFT" });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<PatchShipNavCommand>(c => c.ShipSymbol == "PROBE-1" && c.FlightMode == "CRUISE"),
            Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InFlight_ItWaits()
    {
        var result = await StepAsync(Probe("X1-AB-MKT", "IN_TRANSIT") with { ArrivesAt = DateTimeOffset.MaxValue });

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        _bus.ReceivedCalls().Should().BeEmpty();
        await _goals.DidNotReceiveWithAnyArgs().ClearActiveGoalAsync(default!, default);
    }

    private static ShipModel Probe(string waypoint, string status) =>
        new("PROBE-1", "X1-AB", waypoint, status, "CRUISE", 0, 0, ShipType: "SATELLITE");

    private Task<GoalExecutionResult> StepAsync(ShipModel probe)
        => new DeployProbeGoalExecutor(_goals, _bus, NullLogger<DeployProbeGoalExecutor>.Instance)
            .ExecuteStepAsync(probe, new DeployProbeGoal { TargetWaypointSymbol = "X1-AB-MKT" }, new ShipGoalContext(), CancellationToken.None);
}
