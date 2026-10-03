using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.DTOs;
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
/// B45, found on the cluster on 2026-10-02: the scout plan logged "all 26 waypoints visited" while
/// its last stop, X1-DC53-J58, never was. The ship arrived at stop 25, and in the same second the
/// tick ran its resume check and the ship's goal step next to the arrival's goal step. The resume
/// check sent the ship back to the stop it had just visited, and a second completion of that visit
/// moved the plan past the next stop. The real goal executor service, scout executor and scout plan
/// service run here, with the plan, the goal and the assignment kept in memory, and the tick's step
/// and the arrival's step interleaved the way they were on the cluster.
/// </summary>
public sealed class ScoutStopSkippedTests
{
    private const string ShipSymbol = "SHIP-1";
    private const string VisitedStop = "X1-AB-002";
    private const string LastStop = "X1-AB-003";

    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IScoutPlanRepository _scoutPlans = Substitute.For<IScoutPlanRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly IWaypointVisitService _visits = Substitute.For<IWaypointVisitService>();
    private ShipGoal? _goal = new ScoutWaypointGoal { TargetWaypointSymbol = VisitedStop };
    private ShipAssignmentDto? _assignment = ShipAssignmentDto.CreateScout(ShipSymbol, VisitedStop, 1, DateTimeOffset.UtcNow.AddMinutes(-2));
    private ScoutAllMarketplacesPlanState _plan = new()
    {
        PlanId = Guid.NewGuid(),
        ShipSymbol = ShipSymbol,
        StartWaypointSymbol = "X1-AB-001",
        RouteWaypointSymbols = ["X1-AB-001", VisitedStop, LastStop],
        CurrentRouteIndex = 1,
        Status = ScoutPlanStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
    };

    // Runs once, in the middle of the tick's step: the arrival's goal step, start to end.
    private Func<Task>? _arrivalDuringVisit;
    private Func<Task>? _arrivalDuringAssignmentRead;

    public ScoutStopSkippedTests()
    {
        _goals.GetActiveGoalAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_ => _goal);
        _goals.When(g => g.SetActiveGoalAsync(ShipSymbol, Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _goal = call.Arg<ShipGoal>());
        _goals.When(g => g.ClearActiveGoalAsync(ShipSymbol, Arg.Any<CancellationToken>()))
            .Do(_ => _goal = null);
        _scoutPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(_ => _plan);
        _scoutPlans.When(p => p.UpsertAsync(Arg.Any<ScoutAllMarketplacesPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _plan = call.Arg<ScoutAllMarketplacesPlanState>());
        _assignments.FindAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_ => ReadAssignmentAsync());
        _assignments.When(a => a.UpsertAsync(Arg.Any<ShipAssignmentDto>(), Arg.Any<CancellationToken>()))
            .Do(call => _assignment = call.Arg<ShipAssignmentDto>());
        _visits.MarkVisitedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => VisitAsync());
    }

    [Fact]
    public async Task TheTickAndAnArrivalCompletingTheSameVisit_DoNotSkipTheNextStop()
    {
        // The tick's step has read the ship's goal (the stop it is docked at) when the arrival's step
        // completes that visit and moves the plan on; then the tick's step completes it too.
        _arrivalDuringVisit = () => GoalStepAsync();

        await GoalStepAsync();

        ThePlanWaitsForTheLastStop();
    }

    [Fact]
    public async Task TheTicksResumeCheckDuringAnArrival_DoesNotSendTheShipBack()
    {
        // The tick reads the plan, the arrival's step moves it on, and then the tick's resume check
        // reads the ship's assignment. The tick's goal step follows.
        _arrivalDuringAssignmentRead = () => GoalStepAsync();

        await CreateScoutPlan().EnsureBootstrappedAsync(CancellationToken.None);
        await GoalStepAsync();

        ThePlanWaitsForTheLastStop();
    }

    private void ThePlanWaitsForTheLastStop()
    {
        _plan.Status.Should().Be(ScoutPlanStatus.Active, "the ship hasn't been to {0} yet", LastStop);
        _plan.CurrentRouteIndex.Should().Be(2);
        _goal.Should().BeOfType<ScoutWaypointGoal>()
            .Which.TargetWaypointSymbol.Should().Be(LastStop);
        _assignment.Should().NotBeNull();
        _assignment.StepIndex.Should().Be(2);
        _assignment.DestWaypoint.Should().Be(LastStop);
        _assignment.CompletedAt.Should().BeNull();
        _visits.DidNotReceive().MarkVisitedAsync(LastStop, Arg.Any<CancellationToken>());
    }

    private async Task<ShipAssignmentDto?> ReadAssignmentAsync()
    {
        var arrival = _arrivalDuringAssignmentRead;
        _arrivalDuringAssignmentRead = null;
        if (arrival is not null)
        {
            await arrival();
        }

        return _assignment;
    }

    private async Task VisitAsync()
    {
        var arrival = _arrivalDuringVisit;
        _arrivalDuringVisit = null;
        if (arrival is not null)
        {
            await arrival();
        }
    }

    private ScoutAllMarketplacesPlanService CreateScoutPlan() =>
        new(
            _scoutPlans,
            Substitute.For<IScoutShipSelectionService>(),
            Substitute.For<IScoutMarketplaceDiscoveryService>(),
            Substitute.For<IMarketplaceRoutePlanner>(),
            _assignments,
            _goals,
            NullLogger<ScoutAllMarketplacesPlanService>.Instance);

    // One goal step, as the tick or an arrival runs it, each in its own scope.
    private async Task GoalStepAsync()
    {
        var ships = Substitute.For<IShipRepository>();
        ships.FindAsync(ShipSymbol, Arg.Any<CancellationToken>())
            .Returns(new ShipModel(ShipSymbol, "X1-AB", VisitedStop, "DOCKED", "CRUISE", 400, 400));
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var sut = new ShipGoalExecutorService(
            [new ScoutWaypointGoalExecutor(_visits, Substitute.For<IDockSubCommand>(), Substitute.For<IMessageBus>())],
            _goals,
            ships,
            CreateScoutPlan(),
            settings,
            Substitute.For<IGoalStepCircuitBreaker>(),
            new ShipGoalStepGuard(), // Its own per step, so the interleaved steps both run: this tests B45's fix, not B46's guard.
            Substitute.For<IAutomationMetrics>(),
            Substitute.For<ITripBook>(),
            NullLogger<ShipGoalExecutorService>.Instance);

        await sut.ExecuteAsync(ShipSymbol, CancellationToken.None);
    }
}
