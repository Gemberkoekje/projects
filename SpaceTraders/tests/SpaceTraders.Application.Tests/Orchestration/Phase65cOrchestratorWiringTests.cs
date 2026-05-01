using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Orchestration;

/// <summary>
/// Phase 6.5c integration-style tests.
/// Verifies that:
///   1. <see cref="ShipDockedIdleEventHandler"/> delegates to <see cref="IFleetOrchestrator"/>
///      rather than using a local assignment planner.
///   2. <see cref="FleetOrchestrator"/> emits <see cref="AssignShipCommand"/> and never issues
///      low-level Dock/Orbit/Navigate commands directly.
///   3. After the orchestrator assigns a ship, a subsequent <see cref="ShipAutomationTickEvent"/>
///      causes the planner to execute exactly one command.
///   4. Fleet expansion is treated as an advisory goal only; purchasing ships is out of scope
///      for the orchestrator.
/// </summary>
public sealed class Phase65cOrchestratorWiringTests
{
    // ──────────────────────────────────────────────────────────────────────────────
    // ShipDockedIdleEventHandler — delegates to IFleetOrchestrator
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShipDockedIdleEventHandler_WhenShipIsIdle_CallsOrchestratorInsteadOfLocalPlanner()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var orchestrator = Substitute.For<IFleetOrchestrator>();

        assignments.FindAsync("IDLE-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto(
                ShipSymbol: "IDLE-1",
                AssignmentType: "Idle",
                OriginWaypoint: null,
                DestWaypoint: null,
                CargoSymbol: null,
                ContractId: null,
                StepIndex: 0,
                AssignedAt: DateTimeOffset.UtcNow,
                CompletedAt: null));

        var handler = new ShipDockedIdleEventHandler(
            assignments,
            orchestrator,
            NullLogger<ShipDockedIdleEventHandler>.Instance);

        var result = await handler.HandleAsync(
            new ShipDockedEvent("IDLE-1", "X1-AB", "X1-AB-WP", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow),
            CancellationToken.None);

        result.Should().BeSameAs(ChainOfCommandHandlerResult.Handled(),
            "an idle ship should trigger the orchestrator and return Handled");
        await orchestrator.Received(1).EvaluateAndAssignAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShipDockedIdleEventHandler_WhenShipIsNotIdle_SkipsOrchestratorCall()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var orchestrator = Substitute.For<IFleetOrchestrator>();

        assignments.FindAsync("BUSY-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto(
                ShipSymbol: "BUSY-1",
                AssignmentType: "Trade",
                OriginWaypoint: "X1-A",
                DestWaypoint: "X1-B",
                CargoSymbol: "IRON_ORE",
                ContractId: null,
                StepIndex: 0,
                AssignedAt: DateTimeOffset.UtcNow,
                CompletedAt: null));

        var handler = new ShipDockedIdleEventHandler(
            assignments,
            orchestrator,
            NullLogger<ShipDockedIdleEventHandler>.Instance);

        var result = await handler.HandleAsync(
            new ShipDockedEvent("BUSY-1", "X1-AB", "X1-AB-WP", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow),
            CancellationToken.None);

        result.Should().BeSameAs(ChainOfCommandHandlerResult.Skipped(),
            "a non-idle ship should not trigger the orchestrator");
        await orchestrator.DidNotReceive().EvaluateAndAssignAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShipDockedIdleEventHandler_WhenNoAssignment_SkipsOrchestratorCall()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var orchestrator = Substitute.For<IFleetOrchestrator>();

        assignments.FindAsync("UNASSIGNED-1", Arg.Any<CancellationToken>())
            .Returns((ShipAssignmentDto?)null);

        var handler = new ShipDockedIdleEventHandler(
            assignments,
            orchestrator,
            NullLogger<ShipDockedIdleEventHandler>.Instance);

        var result = await handler.HandleAsync(
            new ShipDockedEvent("UNASSIGNED-1", "X1-AB", "X1-AB-WP", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow),
            CancellationToken.None);

        result.Should().BeSameAs(ChainOfCommandHandlerResult.Skipped());
        await orchestrator.DidNotReceive().EvaluateAndAssignAsync(Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // FleetOrchestrator — emits AssignShipCommand, never navigation commands
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Orchestrator_WhenIdleShipAndContractGoal_EmitsAssignShipCommandOnly()
    {
        var bus = Substitute.For<IMessageBus>();

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([new ShipModel("HAULER-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 60)]);

        var assignments = Substitute.For<IShipAssignmentRepository>();
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        var goal = new FleetGoal(
            FleetGoalKind.Contract,
            "Deliver ore",
            Priority: 100,
            ContractId: "c1",
            TradeSymbol: "IRON_ORE",
            DestinationWaypoint: "X1-DEST",
            RemainingUnits: 10);

        var evaluator = Substitute.For<IFleetGoalEvaluator>();
        evaluator.EvaluateAsync(Arg.Any<CancellationToken>()).Returns([goal]);

        var orchestrator = new FleetOrchestrator(
            [evaluator],
            ships,
            assignments,
            bus,
            NullLogger<FleetOrchestrator>.Instance);

        await orchestrator.EvaluateAndAssignAsync(CancellationToken.None);

        // Orchestrator must emit AssignShipCommand — never low-level navigation commands.
        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipCommand>(c =>
                c.ShipSymbol == "HAULER-1" &&
                c.AssignmentType == "Contract" &&
                c.ContractId == "c1"),
            Arg.Any<DeliveryOptions>());

        // Verify no Dock / Orbit / Navigate commands were issued by the orchestrator.
        await bus.DidNotReceive().SendAsync(Arg.Is<DockShipCommand>(c => c != null), Arg.Any<DeliveryOptions>());
        await bus.DidNotReceive().SendAsync(Arg.Is<OrbitShipCommand>(c => c != null), Arg.Any<DeliveryOptions>());
        await bus.DidNotReceive().SendAsync(Arg.Is<NavigateShipCommand>(c => c != null), Arg.Any<DeliveryOptions>());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Fleet expansion — advisory goal only; no purchase-ship command issued
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Orchestrator_FleetExpansionGoal_IsAdvisoryOnly_NoPurchaseOrNavigationCommand()
    {
        var bus = Substitute.For<IMessageBus>();

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([new ShipModel("S1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 30)]);

        var assignments = Substitute.For<IShipAssignmentRepository>();
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        var goal = new FleetGoal(
            FleetGoalKind.FleetExpansion,
            "Expand fleet",
            Priority: 30,
            EstimatedCost: 500_000);

        var evaluator = Substitute.For<IFleetGoalEvaluator>();
        evaluator.EvaluateAsync(Arg.Any<CancellationToken>()).Returns([goal]);

        var orchestrator = new FleetOrchestrator(
            [evaluator],
            ships,
            assignments,
            bus,
            NullLogger<FleetOrchestrator>.Instance);

        await orchestrator.EvaluateAndAssignAsync(CancellationToken.None);

        // Fleet expansion is advisory: no command of any kind should be sent.
        await bus.DidNotReceive().SendAsync(Arg.Any<AssignShipCommand>(), Arg.Any<DeliveryOptions>());
        await bus.DidNotReceive().SendAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // End-to-end: orchestrator assigns → ShipAutomationTickEvent → planner executes
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterOrchestratorAssigns_ShipAutomationTickEvent_ExecutesExactlyOnePlannerCommand()
    {
        // Arrange: planner service that records calls
        var planner = Substitute.For<IShipPlannerService>();
        planner.PlanAndExecuteAsync("HAULER-1", Arg.Any<CancellationToken>())
            .Returns(ShipPlannerDecision.Orbit("HAULER-1", "ready to proceed"));

        var tickHandler = new ShipAutomationTickEventHandler(
            planner,
            NullLogger<ShipAutomationTickEventHandler>.Instance);

        // Act: simulate what happens after the orchestrator sends AssignShipCommand and
        // the next automation tick fires for the ship.
        var tick = new ShipAutomationTickEvent(
            "HAULER-1",
            "Assigned",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.Empty);

        await tickHandler.Handle(tick, CancellationToken.None);

        // Assert: planner was invoked exactly once; no other commands were issued here
        // (the planner service owns execution of those).
        await planner.Received(1).PlanAndExecuteAsync("HAULER-1", Arg.Any<CancellationToken>());
    }
}
