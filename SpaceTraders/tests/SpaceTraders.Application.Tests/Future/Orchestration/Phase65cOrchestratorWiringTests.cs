using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Orchestration;

/// <summary>
/// Phase 6.5c integration-style tests.
/// Phase 7b: ShipDockedIdleEventHandler tests removed (handler deleted).
/// Phase 11b: orchestrator now emits <see cref="AssignShipToGoalCommand"/> instead of
///   <see cref="AssignShipCommand"/>; fleet expansion is dispatched via the same command.
/// Phase 12a: planner-based end-to-end test replaced with goal-executor variant.
/// Remaining tests verify that:
///   1. <see cref="FleetOrchestrator"/> emits <see cref="AssignShipToGoalCommand"/> and never issues
///      low-level Dock/Orbit/Navigate commands directly.
///   2. After the orchestrator assigns a ship, the goal executor service is invoked directly
///      (no synthetic tick event).
///   3. Fleet expansion is dispatched via <see cref="AssignShipToGoalCommand"/>; the handler
///      handles purchasing.
/// </summary>
public sealed class Phase65cOrchestratorWiringTests
{
    private static IFleetGoalRepository EmptyFleetGoalRepository()
    {
        var repo = Substitute.For<IFleetGoalRepository>();
        repo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        return repo;
    }

    private static IShipGoalRepository EmptyShipGoalRepository()
    {
        var repo = Substitute.For<IShipGoalRepository>();
        repo.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ShipGoal?)null);
        return repo;
    }

    [Fact]
    public async Task Orchestrator_WhenIdleShipAndContractGoal_EmitsAssignShipToGoalCommandOnly()
    {
        var bus = Substitute.For<IMessageBus>();

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([new ShipModel("HAULER-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 60)]);

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
            EmptyShipGoalRepository(),
            EmptyFleetGoalRepository(),
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
            EmptyShipGoalRepository(),
            EmptyFleetGoalRepository(),
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
    // End-to-end: orchestrator assigns → goal executor runs directly
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterOrchestratorAssigns_AssignShipToGoalCommandHandler_InvokesGoalExecutorServiceOnce()
    {
        // Arrange
        var goalExecutorService = Substitute.For<IShipGoalExecutorService>();
        goalExecutorService.ExecuteAsync("HAULER-1", Arg.Any<CancellationToken>())
            .Returns((GoalExecutionResult?)null);

        var ships = Substitute.For<IShipRepository>();
        ships.FindAsync("HAULER-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("HAULER-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 60));

        var goals = Substitute.For<IShipGoalRepository>();
        var resolver = Substitute.For<IAssignmentResolver>();
        var resolvedGoal = new MineResourceGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = "X1-A-AST" };
        resolver.ResolveAsync(Arg.Any<ShipModel>(), Arg.Any<ResourceProductionGoal>(), Arg.Any<CancellationToken>())
            .Returns(resolvedGoal);

        var settings = Substitute.For<ISettingsRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var bus = Substitute.For<IMessageBus>();

        var handler = new AssignShipToGoalCommandHandler(
            ships, goals, resolver, settings, shipyards, bus, goalExecutorService,
            NullLogger<AssignShipToGoalCommandHandler>.Instance);

        var command = new AssignShipToGoalCommand("HAULER-1", new FleetGoal(
            FleetGoalKind.Contract, "Deliver ore", 100,
            ContractId: "c1", TradeSymbol: "IRON_ORE", RemainingUnits: 10));

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert: goal executor service was invoked exactly once — no tick event.
        await goalExecutorService.Received(1).ExecuteAsync("HAULER-1", Arg.Any<CancellationToken>());
    }
}
