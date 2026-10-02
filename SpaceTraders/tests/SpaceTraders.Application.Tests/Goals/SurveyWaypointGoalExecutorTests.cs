using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Goals;
using Wolverine;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Slice 6.4: a survey goal is one survey. The ship flies to the asteroid, orbits, waits for its cooldown,
/// surveys and stores what it found; then the goal ends and the survey plan gives the next one (B16: survey
/// goals never ended).
/// </summary>
public sealed class SurveyWaypointGoalExecutorTests
{
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly ISurveyKeeper _surveyKeeper = Substitute.For<ISurveyKeeper>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly LogRecorder _log = new();

    private static readonly SurveyWaypointGoal Goal = new() { TargetWaypointSymbol = XB5C, TargetDepositSymbol = "COPPER_ORE" };

    public SurveyWaypointGoalExecutorTests()
    {
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new TradeContext(Map(), 129_357, 200));
    }

    [Fact]
    public async Task DockedAtTheAsteroid_ItEntersOrbit()
    {
        var result = await StepAsync(CommandShip(status: "DOCKED"));

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _orbit.Received(1).ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().SurveyAsync(default!, default);
    }

    [Fact]
    public async Task Elsewhere_ItFliesToTheAsteroid()
    {
        var result = await StepAsync(CommandShip(waypoint: H51, status: "DOCKED"));

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.ShipSymbol == "SHIP-1" && command.DestinationWaypoint == XB5C),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnCooldown_ItWaits()
    {
        var result = await StepAsync(CommandShip() with { CooldownExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30) });

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
        await _port.DidNotReceiveWithAnyArgs().SurveyAsync(default!, default);
    }

    [Fact]
    public async Task InOrbit_ItSurveys_KeepsWhatItFound_AndTheGoalEnds()
    {
        var found = Survey("S-1", XB5C, "COPPER_ORE", "IRON_ORE");
        var cooldown = DateTimeOffset.UtcNow.AddSeconds(70);
        _port.SurveyAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(new SurveyActionResult([found], 70, cooldown));

        var result = await StepAsync(CommandShip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _surveyKeeper.Received(1).TakenAsync("SHIP-1", "COPPER_ORE", Arg.Is<IReadOnlyList<SurveyModel>>(s => s.Single() == found), Arg.Any<CancellationToken>());
        await _ships.Received(1).UpdateCooldownAsync("SHIP-1", cooldown, Arg.Any<CancellationToken>());
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedSurvey_EndsTheGoal_SoThePlanCanGiveItAgain()
    {
        _port.SurveyAsync("SHIP-1", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("4000 cooldown"));

        var result = await StepAsync(CommandShip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _surveyKeeper.DidNotReceiveWithAnyArgs().TakenAsync(default!, default!, default!, default);
    }

    private Task<GoalExecutionResult> StepAsync(ShipModel ship)
        => new SurveyWaypointGoalExecutor(
                _port,
                _ships,
                _goals,
                _surveyKeeper,
                _tradeContexts,
                _orbit,
                _dock,
                _bus,
                _log.For<SurveyWaypointGoalExecutor>())
            .ExecuteStepAsync(ship, Goal, new ShipGoalContext(), CancellationToken.None);
}
