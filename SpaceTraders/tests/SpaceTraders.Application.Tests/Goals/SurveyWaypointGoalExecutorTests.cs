using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

public sealed class SurveyWaypointGoalExecutorTests
{
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ISurveyRepository _surveys = Substitute.For<ISurveyRepository>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private SurveyWaypointGoalExecutor CreateExecutor() =>
        new(
            _port,
            _surveys,
            _orbit,
            _bus,
            NullLogger<SurveyWaypointGoalExecutor>.Instance);

    private static SurveyWaypointGoal Goal() =>
        new()
        {
            TargetWaypointSymbol = "X1-AB-AST",
            TargetDepositSymbol = "IRON_ORE",
        };

    private static ShipModel Ship(string waypoint, string status) =>
        new("SURVEYOR-1", "X1-AB", waypoint, status, "CRUISE", 100, 100, MountSymbols: ["MOUNT_SURVEYOR_I"]);

    [Fact]
    public async Task ExecuteStepAsync_Orbits_WhenDockedAtTarget()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-AST", "DOCKED"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _orbit.Received(1).ExecuteAsync("SURVEYOR-1", Arg.Any<CancellationToken>());
        await _port.DidNotReceive().SurveyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_SurveysAndStoresResults_WhenInOrbitAtTarget()
    {
        var survey = new SurveyModel(
            Signature: "SURVEY-1",
            WaypointSymbol: "X1-AB-AST",
            Deposits: [new SurveyDepositModel("IRON_ORE")],
            Expiration: TimeProvider.System.GetUtcNow().AddMinutes(10),
            Size: "SMALL");

        _port.SurveyAsync("SURVEYOR-1", Arg.Any<CancellationToken>())
            .Returns(new SurveyActionResult([survey], CooldownSeconds: 0));

        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-AST", "IN_ORBIT"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _surveys.Received(1).UpsertAsync(
            "SURVEYOR-1",
            Arg.Is<IReadOnlyList<SurveyModel>>(s => s.Count == 1 && s[0].Signature == "SURVEY-1"),
            Arg.Any<CancellationToken>());
        await _orbit.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_NavigatesToTarget_WhenElsewhere()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-HQ", "DOCKED"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.ShipSymbol == "SURVEYOR-1" && c.DestinationWaypoint == "X1-AB-AST"),
            Arg.Any<CancellationToken>());
        await _orbit.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _port.DidNotReceive().SurveyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
