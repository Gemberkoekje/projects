using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Tests.Orchestration;

public sealed class FleetOrchestratorTests
{
    private static IFleetGoalRepository EmptyFleetGoalRepository()
    {
        var repo = Substitute.For<IFleetGoalRepository>();
        repo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        return repo;
    }

    private static IShipAssignmentRepository EmptyAssignments()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        return assignments;
    }

    private static IShipRepository ShipsWith(params ShipModel[] fleet)
    {
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);
        return ships;
    }

    private static IFleetGoalEvaluator EvaluatorReturning(params FleetGoal[] goals)
    {
        var evaluator = Substitute.For<IFleetGoalEvaluator>();
        evaluator.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(goals);
        return evaluator;
    }

    [Fact]
    public async Task EvaluateAndAssignAsync_AssignsContractGoalToIdleShip()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("HAULER-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 60));
        var assignments = EmptyAssignments();
        var contractGoal = new FleetGoal(
            FleetGoalKind.Contract,
            "Deliver iron",
            Priority: 100,
            ContractId: "c1",
            TradeSymbol: "IRON_ORE",
            RemainingUnits: 50);

        var orchestrator = new FleetOrchestrator(
            [EvaluatorReturning(contractGoal)],
            ships,
            assignments,
            EmptyFleetGoalRepository(),
            bus,
            NullLogger<FleetOrchestrator>.Instance);

        await orchestrator.EvaluateAndAssignAsync(CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c =>
                c.ShipSymbol == "HAULER-1" &&
                c.Goal.Kind == FleetGoalKind.Contract &&
                c.Goal.ContractId == "c1" &&
                c.Goal.TradeSymbol == "IRON_ORE" &&
                c.Goal.RemainingUnits == 50),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task EvaluateAndAssignAsync_PrefersHigherPriorityGoal()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("S1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 30));
        var assignments = EmptyAssignments();

        var contract = new FleetGoal(FleetGoalKind.Contract, "C", 100, ContractId: "c1", TradeSymbol: "ORE", RemainingUnits: 1);
        var market = new FleetGoal(FleetGoalKind.MarketCoverage, "M", 40, OriginWaypoint: "X1-MKT");

        var orchestrator = new FleetOrchestrator(
            [EvaluatorReturning(market), EvaluatorReturning(contract)],
            ships,
            assignments,
            EmptyFleetGoalRepository(),
            bus,
            NullLogger<FleetOrchestrator>.Instance);

        await orchestrator.EvaluateAndAssignAsync(CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c => c.Goal.Kind == FleetGoalKind.Contract),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task EvaluateAndAssignAsync_DoesNotAssignBusyShip()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("BUSY-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 60));
        var assignments = Substitute.For<IShipAssignmentRepository>();
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipAssignmentDto("BUSY-1", "Trade", null, null, null, null, 0, DateTimeOffset.UtcNow, null),
        ]);

        var goal = new FleetGoal(FleetGoalKind.Contract, "C", 100, ContractId: "c1", TradeSymbol: "ORE", RemainingUnits: 5);

        var orchestrator = new FleetOrchestrator(
            [EvaluatorReturning(goal)],
            ships,
            assignments,
            EmptyFleetGoalRepository(),
            bus,
            NullLogger<FleetOrchestrator>.Instance);

        await orchestrator.EvaluateAndAssignAsync(CancellationToken.None);

        await bus.DidNotReceive().SendAsync(Arg.Any<AssignShipToGoalCommand>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task EvaluateAndAssignAsync_AssignsMarketCoverage()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("PROBE-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100));
        var assignments = EmptyAssignments();
        var goal = new FleetGoal(FleetGoalKind.MarketCoverage, "M", 40, OriginWaypoint: "X1-MKT-1");

        var orchestrator = new FleetOrchestrator(
            [EvaluatorReturning(goal)],
            ships,
            assignments,
            EmptyFleetGoalRepository(),
            bus,
            NullLogger<FleetOrchestrator>.Instance);

        await orchestrator.EvaluateAndAssignAsync(CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c =>
                c.ShipSymbol == "PROBE-1" &&
                c.Goal.Kind == FleetGoalKind.MarketCoverage &&
                c.Goal.OriginWaypoint == "X1-MKT-1"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task EvaluateAndAssignAsync_FleetExpansionGoal_EmitsAssignShipToGoalCommand()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("S1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 30));
        var assignments = EmptyAssignments();
        var goal = new FleetGoal(FleetGoalKind.FleetExpansion, "Expand", 30, EstimatedCost: 100_000);

        var orchestrator = new FleetOrchestrator(
            [EvaluatorReturning(goal)],
            ships,
            assignments,
            EmptyFleetGoalRepository(),
            bus,
            NullLogger<FleetOrchestrator>.Instance);

        await orchestrator.EvaluateAndAssignAsync(CancellationToken.None);

        // FleetExpansion is dispatched via AssignShipToGoalCommand; the handler handles purchasing.
        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c => c.Goal.Kind == FleetGoalKind.FleetExpansion),
            Arg.Any<DeliveryOptions>());
    }
}
