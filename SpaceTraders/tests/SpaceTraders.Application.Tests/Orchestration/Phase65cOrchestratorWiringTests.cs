using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Orchestration;

/// <summary>
/// Phase 6.5c integration-style tests.
/// Phase 7b: ShipDockedIdleEventHandler tests removed (handler deleted).
/// Phase 11b: orchestrator now emits <see cref="AssignShipToGoalCommand"/> instead of
///   <see cref="AssignShipCommand"/>; fleet expansion is dispatched via the same command.
/// Remaining tests verify that:
///   1. <see cref="FleetOrchestrator"/> emits <see cref="AssignShipToGoalCommand"/> and never issues
///      low-level Dock/Orbit/Navigate commands directly.
///   2. After the orchestrator assigns a ship, a subsequent <see cref="ShipAutomationTickEvent"/>
///      causes the planner to execute exactly one command.
///   3. Fleet expansion is dispatched via <see cref="AssignShipToGoalCommand"/>; the handler
///      handles purchasing.
/// </summary>
public sealed class Phase65cOrchestratorWiringTests
{

    [Fact]
    public async Task Orchestrator_WhenIdleShipAndContractGoal_EmitsAssignShipToGoalCommandOnly()
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

        // Orchestrator must emit AssignShipToGoalCommand — never low-level navigation commands.
        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c =>
                c.ShipSymbol == "HAULER-1" &&
                c.Goal.Kind == FleetGoalKind.Contract &&
                c.Goal.ContractId == "c1"),
            Arg.Any<DeliveryOptions>());

        // Verify no Dock / Orbit / Navigate commands were issued by the orchestrator.
        await bus.DidNotReceive().SendAsync(Arg.Is<DockShipCommand>(c => c != null), Arg.Any<DeliveryOptions>());
        await bus.DidNotReceive().SendAsync(Arg.Is<OrbitShipCommand>(c => c != null), Arg.Any<DeliveryOptions>());
        await bus.DidNotReceive().SendAsync(Arg.Is<NavigateShipCommand>(c => c != null), Arg.Any<DeliveryOptions>());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Fleet expansion — dispatched via AssignShipToGoalCommand; handler does purchasing
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Orchestrator_FleetExpansionGoal_EmitsAssignShipToGoalCommand_NoPurchaseOrNavigationCommand()
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

        // Fleet expansion is dispatched via AssignShipToGoalCommand; the handler handles purchasing.
        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c => c.Goal.Kind == FleetGoalKind.FleetExpansion),
            Arg.Any<DeliveryOptions>());

        // Verify no low-level navigation commands were issued by the orchestrator.
        await bus.DidNotReceive().SendAsync(Arg.Is<DockShipCommand>(c => c != null), Arg.Any<DeliveryOptions>());
        await bus.DidNotReceive().SendAsync(Arg.Is<OrbitShipCommand>(c => c != null), Arg.Any<DeliveryOptions>());
        await bus.DidNotReceive().SendAsync(Arg.Is<NavigateShipCommand>(c => c != null), Arg.Any<DeliveryOptions>());
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
