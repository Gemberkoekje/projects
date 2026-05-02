using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Phase 13e: unit tests for <see cref="ShipGoalExecutorService"/>.
/// Verifies that the service communicates with the orchestrator exclusively via
/// <see cref="GoalCompletedEvent"/> and <see cref="GoalBlockedEvent"/>, and that
/// continuation ticks are published or scheduled correctly for non-terminal outcomes.
/// </summary>
public sealed class ShipGoalExecutorServiceTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly ISurveyRepository _surveys = Substitute.For<ISurveyRepository>();
    private readonly INavigationPlanningService _navigationPlanning = Substitute.For<INavigationPlanningService>();
    private readonly IConstructionRepository _constructions = Substitute.For<IConstructionRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IContractRepository _contracts = Substitute.For<IContractRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IAssignmentResolver _assignmentResolver = Substitute.For<IAssignmentResolver>();
    private readonly IShipCapabilityRegistry _capabilityRegistry = Substitute.For<IShipCapabilityRegistry>();
    private readonly IShipEventScheduler _scheduler = Substitute.For<IShipEventScheduler>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IShipGoalExecutor _executor = Substitute.For<IShipGoalExecutor>();

    private static readonly ShipModel Ship = new("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100);
    private static readonly IdleGoal IdleGoal = new();

    public ShipGoalExecutorServiceTests()
    {
        // Executor always accepts IdleGoal.
        _executor.CanExecute(Arg.Any<ShipGoal>()).Returns(true);

        // Capability registry: no survey capability, so survey repository is never called.
        _capabilityRegistry.GetCapabilities(Arg.Any<ShipModel>())
            .Returns(new ShipCapabilities(false, false, false, true, true, false));

        // Waypoints: return empty list so fuel context returns early without further DB hits.
        _waypoints.GetBySystemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        // Markets: no snapshots.
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private ShipGoalExecutorService CreateService() =>
        new(
            [_executor],
            _goals,
            _ships,
            _assignments,
            _surveys,
            _navigationPlanning,
            _constructions,
            _waypoints,
            _markets,
            _contracts,
            _settings,
            _assignmentResolver,
            _capabilityRegistry,
            _scheduler,
            _bus,
            NullLogger<ShipGoalExecutorService>.Instance);

    private void SetupShipAndGoal()
    {
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(Ship);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(IdleGoal);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Terminal outcomes — orchestrator communication
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenGoalCompleted_PublishesGoalCompletedEvent()
    {
        SetupShipAndGoal();
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.Completed("Done"));

        var svc = CreateService();
        var result = await svc.ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Outcome.Should().Be(GoalExecutionOutcome.Completed);

        await _bus.Received(1).PublishAsync(
            Arg.Is<GoalCompletedEvent>(e => e.ShipSymbol == "SHIP-1"),
            Arg.Any<DeliveryOptions>());
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenGoalCompleted_DoesNotPublishGoalBlockedEvent()
    {
        SetupShipAndGoal();
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.Completed("Done"));

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        await _bus.DidNotReceive().PublishAsync(
            Arg.Any<GoalBlockedEvent>(),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenGoalBlocked_PublishesGoalBlockedEvent()
    {
        SetupShipAndGoal();
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.Blocked("No route available"));

        var svc = CreateService();
        var result = await svc.ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Outcome.Should().Be(GoalExecutionOutcome.Blocked);

        await _bus.Received(1).PublishAsync(
            Arg.Is<GoalBlockedEvent>(e => e.ShipSymbol == "SHIP-1" && e.Reason == "No route available"),
            Arg.Any<DeliveryOptions>());
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenGoalBlocked_DoesNotPublishGoalCompletedEvent()
    {
        SetupShipAndGoal();
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.Blocked("No route available"));

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        await _bus.DidNotReceive().PublishAsync(
            Arg.Any<GoalCompletedEvent>(),
            Arg.Any<DeliveryOptions>());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Non-terminal outcomes — continuation ticks
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenProgressing_PublishesImmediateAutomationTickEvent()
    {
        SetupShipAndGoal();
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.Progressing("Navigating"));

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e => e.ShipSymbol == "SHIP-1" && e.Reason == "GoalStep"),
            Arg.Any<DeliveryOptions>());

        // No orchestrator events should be published for a progressing step.
        await _bus.DidNotReceive().PublishAsync(Arg.Any<GoalCompletedEvent>(), Arg.Any<DeliveryOptions>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<GoalBlockedEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenWaitingForCooldownWithExpiry_SchedulesCooldownViaScheduler()
    {
        SetupShipAndGoal();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(1);
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.WaitingForCooldown("On cooldown", expiresAt));

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        // Phase 14c: scheduler is used instead of bus.ScheduleAsync.
        await _scheduler.Received(1).ScheduleCooldownExpiryAsync(
            "SHIP-1",
            Arg.Any<Guid>(),
            expiresAt,
            Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e => e.Reason == "CooldownExpired"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenWaitingForCooldownWithoutExpiry_SchedulesFallbackViaScheduler()
    {
        SetupShipAndGoal();
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.WaitingForCooldown("On cooldown, expiry unknown"));

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        // Phase 14c: scheduler is used with a fallback time (no bus.ScheduleAsync).
        await _scheduler.Received(1).ScheduleCooldownExpiryAsync(
            "SHIP-1",
            Arg.Any<Guid>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e => e.Reason == "CooldownRetry"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenWaitingForArrival_SchedulesArrivalViaScheduler()
    {
        var arrival = DateTimeOffset.UtcNow.AddMinutes(3);
        var ship = Ship with { ArrivesAt = arrival };
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(IdleGoal);
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.WaitingForArrival("In transit"));

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        // Phase 14c: scheduler is used for arrival (no bus.PublishAsync for ticks).
        await _scheduler.Received(1).ScheduleArrivalAsync(
            "SHIP-1",
            Arg.Any<Guid>(),
            arrival,
            Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<ShipAutomationTickEvent>(), Arg.Any<DeliveryOptions>());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Edge cases
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenShipNotFound_ReturnsNull()
    {
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipModel?)null);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().BeNull();
        await _bus.DidNotReceive().PublishAsync(Arg.Any<GoalCompletedEvent>(), Arg.Any<DeliveryOptions>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<GoalBlockedEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoActiveGoal_ReturnsNull()
    {
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(Ship);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipGoal?)null);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().BeNull();
        await _bus.DidNotReceive().PublishAsync(Arg.Any<GoalCompletedEvent>(), Arg.Any<DeliveryOptions>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<GoalBlockedEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenGoalCompleted_PreservesGoalIdInEvent()
    {
        SetupShipAndGoal();
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.Completed("Done"));

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        await _bus.Received(1).PublishAsync(
            Arg.Is<GoalCompletedEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.GoalId == IdleGoal.GoalId &&
                e.GoalKind == IdleGoal.Kind),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenGoalBlocked_PreservesGoalIdInEvent()
    {
        SetupShipAndGoal();
        const string reason = "No equipment";
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.Blocked(reason));

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        await _bus.Received(1).PublishAsync(
            Arg.Is<GoalBlockedEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.GoalId == IdleGoal.GoalId &&
                e.GoalKind == IdleGoal.Kind &&
                e.Reason == reason),
            Arg.Any<DeliveryOptions>());
    }
}
