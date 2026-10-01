using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// B10: after the last scout stop the command ship kept its scout goal, so every tick ran it again:
/// docked at the target, mark the waypoint visited (a database write), complete, and advance a plan
/// that was already done. The real goal executor service, scout executor and scout plan service run
/// here for a minute of ticks, with goals and plan state kept in memory.
/// </summary>
public sealed class CommandShipAfterScoutingTests
{
    private const string ShipSymbol = "SHIP-1";
    private const string LastStop = "X1-AB-002";
    private const int TicksPerMinute = 12;

    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IScoutPlanRepository _scoutPlans = Substitute.For<IScoutPlanRepository>();
    private readonly IWaypointVisitService _visits = Substitute.For<IWaypointVisitService>();
    private ShipGoal? _goal = new ScoutWaypointGoal { TargetWaypointSymbol = LastStop };
    private ScoutAllMarketplacesPlanState _plan = Plan(ScoutPlanStatus.Active);

    public CommandShipAfterScoutingTests()
    {
        _goals.GetActiveGoalAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_ => _goal);
        _goals.When(g => g.SetActiveGoalAsync(ShipSymbol, Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _goal = call.Arg<ShipGoal>());
        _goals.When(g => g.ClearActiveGoalAsync(ShipSymbol, Arg.Any<CancellationToken>()))
            .Do(_ => _goal = null);
        _scoutPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(_ => _plan);
        _scoutPlans.When(p => p.UpsertAsync(Arg.Any<ScoutAllMarketplacesPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _plan = call.Arg<ScoutAllMarketplacesPlanState>());
    }

    [Fact]
    public async Task ReachingTheLastStop_CompletesThePlan_AndTheShipIsNotSteppedAgain()
    {
        await RunAMinuteOfTicksAsync();

        _plan.Status.Should().Be(ScoutPlanStatus.Completed);
        _goal.Should().BeNull();
        await _visits.Received(1).MarkVisitedAsync(LastStop, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AShipThatKeptItsGoalAfterThePlanCompleted_IsReleasedOnItsNextStep()
    {
        // A database from before the fix: the plan is done, but the ship still holds its last goal.
        _plan = Plan(ScoutPlanStatus.Completed);

        await RunAMinuteOfTicksAsync();

        _goal.Should().BeNull();
        await _visits.Received(1).MarkVisitedAsync(LastStop, Arg.Any<CancellationToken>());
    }

    private static ScoutAllMarketplacesPlanState Plan(ScoutPlanStatus status) => new()
    {
        PlanId = Guid.NewGuid(),
        ShipSymbol = ShipSymbol,
        StartWaypointSymbol = "X1-AB-001",
        RouteWaypointSymbols = ["X1-AB-001", LastStop],
        CurrentRouteIndex = 1,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private async Task RunAMinuteOfTicksAsync()
    {
        var ships = Substitute.For<IShipRepository>();
        ships.FindAsync(ShipSymbol, Arg.Any<CancellationToken>())
            .Returns(new ShipModel(ShipSymbol, "X1-AB", LastStop, "DOCKED", "CRUISE", 400, 400));
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var scoutPlan = new ScoutAllMarketplacesPlanService(
            _scoutPlans,
            Substitute.For<IScoutShipSelectionService>(),
            Substitute.For<IScoutMarketplaceDiscoveryService>(),
            Substitute.For<IMarketplaceRoutePlanner>(),
            Substitute.For<IShipAssignmentRepository>(),
            _goals,
            NullLogger<ScoutAllMarketplacesPlanService>.Instance);
        var sut = new ShipGoalExecutorService(
            [new ScoutWaypointGoalExecutor(_visits, Substitute.For<IDockSubCommand>(), Substitute.For<IMessageBus>())],
            _goals,
            ships,
            scoutPlan,
            settings,
            Substitute.For<IGoalStepCircuitBreaker>(),
            Substitute.For<IAutomationMetrics>(),
            NullLogger<ShipGoalExecutorService>.Instance);

        for (var tick = 0; tick < TicksPerMinute; tick++)
        {
            await sut.ExecuteAsync(ShipSymbol, CancellationToken.None);
        }
    }
}
