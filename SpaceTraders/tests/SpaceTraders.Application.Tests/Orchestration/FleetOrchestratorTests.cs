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
        // Goals with lower priority numbers are assigned first (ascending order).
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("S1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 30));
        var assignments = EmptyAssignments();

        var contract = new FleetGoal(FleetGoalKind.Contract, "C", FleetGoalPriority.Contract, ContractId: "c1", TradeSymbol: "ORE", RemainingUnits: 1);
        var market = new FleetGoal(FleetGoalKind.MarketCoverage, "M", FleetGoalPriority.MarketCoverage, OriginWaypoint: "X1-MKT");

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
    public async Task EvaluateAndAssignAsync_AssignsHigherPriorityGoalFirst_WhenMultipleIdleShipsAvailable()
    {
        // With two idle ships and two goals of different priorities, each ship
        // should be assigned to the goal with the lower priority number first.
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("S1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 30),
            new ShipModel("S2", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 30));
        var assignments = EmptyAssignments();

        var highPriority = new FleetGoal(FleetGoalKind.Contract, "High", FleetGoalPriority.Contract, ContractId: "c1", TradeSymbol: "ORE", RemainingUnits: 1);
        var lowPriority = new FleetGoal(FleetGoalKind.MarketCoverage, "Low", FleetGoalPriority.MarketCoverage, OriginWaypoint: "X1-MKT");

        var orchestrator = new FleetOrchestrator(
            [EvaluatorReturning(lowPriority, highPriority)],
            ships,
            assignments,
            EmptyFleetGoalRepository(),
            bus,
            NullLogger<FleetOrchestrator>.Instance);

        await orchestrator.EvaluateAndAssignAsync(CancellationToken.None);

        // Both goals should be assigned (two ships available).
        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c => c.Goal.Kind == FleetGoalKind.Contract),
            Arg.Any<DeliveryOptions>());
        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c => c.Goal.Kind == FleetGoalKind.MarketCoverage),
            Arg.Any<DeliveryOptions>());

        // Verify the high-priority goal (Contract, lower number) was assigned before the low-priority one.
        var calls = bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IMessageBus.SendAsync))
            .Select(c => c.GetArguments()[0])
            .OfType<AssignShipToGoalCommand>()
            .ToList();
        calls[0].Goal.Kind.Should().Be(FleetGoalKind.Contract);
        calls[1].Goal.Kind.Should().Be(FleetGoalKind.MarketCoverage);
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

    [Fact]
    public async Task EvaluateAndAssignAsync_MarketScoutingGoal_AssignsIdleShip()
    {
        // MarketScouting goal produced for a waypoint with uncached market data →
        // orchestrator assigns the idle ship.
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("PROBE-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 0));
        var assignments = EmptyAssignments();
        var goal = new FleetGoal(
            FleetGoalKind.MarketScouting,
            "Scout X1-MKT",
            FleetGoalPriority.MarketScouting,
            OriginWaypoint: "X1-MKT");

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
                c.Goal.Kind == FleetGoalKind.MarketScouting &&
                c.Goal.OriginWaypoint == "X1-MKT"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task EvaluateAndAssignAsync_MarketScoutingGoal_NoIdleShip_NothingDispatched()
    {
        // MarketScouting goal, but the only ship is busy → orchestrator does not dispatch.
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("PROBE-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 0));
        var assignments = Substitute.For<IShipAssignmentRepository>();
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipAssignmentDto("PROBE-1", "Scout", null, null, null, null, 0, DateTimeOffset.UtcNow, null),
        ]);
        var goal = new FleetGoal(
            FleetGoalKind.MarketScouting,
            "Scout X1-MKT",
            FleetGoalPriority.MarketScouting,
            OriginWaypoint: "X1-MKT");

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
    public async Task EvaluateAndAssignAsync_ContractGoal_AllShipsBusy_FleetExpansionStillDispatched()
    {
        // All ships are busy (contract goal cannot be assigned), but a fleet expansion goal
        // is also present. Fleet expansion is dispatched regardless of idle ship availability.
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("HAULER-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 60));
        var assignments = Substitute.For<IShipAssignmentRepository>();
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipAssignmentDto("HAULER-1", "Contract", null, null, null, null, 0, DateTimeOffset.UtcNow, null),
        ]);

        var contractGoal = new FleetGoal(
            FleetGoalKind.Contract,
            "Deliver ore",
            FleetGoalPriority.Contract,
            ContractId: "c1",
            TradeSymbol: "IRON_ORE",
            RemainingUnits: 50);
        var expansionGoal = new FleetGoal(
            FleetGoalKind.FleetExpansion,
            "Expand fleet",
            FleetGoalPriority.FleetExpansion,
            EstimatedCost: 200_000,
            ExpansionShipType: "SHIP_MINING_DRONE",
            ExpansionShipyardWaypoint: "X1-AB-SHIPYARD");

        var orchestrator = new FleetOrchestrator(
            [EvaluatorReturning(contractGoal, expansionGoal)],
            ships,
            assignments,
            EmptyFleetGoalRepository(),
            bus,
            NullLogger<FleetOrchestrator>.Instance);

        await orchestrator.EvaluateAndAssignAsync(CancellationToken.None);

        // Fleet expansion is dispatched even though no idle ships are available.
        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c => c.Goal.Kind == FleetGoalKind.FleetExpansion),
            Arg.Any<DeliveryOptions>());

        // Contract goal is NOT assigned because no idle ships are available.
        await bus.DidNotReceive().SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c => c.Goal.Kind == FleetGoalKind.Contract),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task EvaluateAndAssignAsync_ConstructionGoal_AssignsIdleMiner()
    {
        // Construction resource need with an idle ship that has cargo → idle ship assigned.
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("MINER-1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 40));
        var assignments = EmptyAssignments();
        var goal = new FleetGoal(
            FleetGoalKind.Construction,
            "Supply FAB_MATS to X1-GATE",
            FleetGoalPriority.Construction,
            TradeSymbol: "FAB_MATS",
            DestinationWaypoint: "X1-GATE",
            RemainingUnits: 500);

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
                c.ShipSymbol == "MINER-1" &&
                c.Goal.Kind == FleetGoalKind.Construction &&
                c.Goal.DestinationWaypoint == "X1-GATE"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task EvaluateAndAssignAsync_TwoGoalsDifferentPriorities_OneIdleShip_HigherPriorityAssigned()
    {
        // Two goals with different priorities, one idle ship → ship assigned to higher-priority goal
        // (lower priority number = higher priority in ascending ordering).
        var bus = Substitute.For<IMessageBus>();
        var ships = ShipsWith(
            new ShipModel("S1", "X1", "X1-A", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 30));
        var assignments = EmptyAssignments();

        var highPriority = new FleetGoal(
            FleetGoalKind.MarketScouting,
            "Scout high priority",
            FleetGoalPriority.MarketScouting,
            OriginWaypoint: "X1-MKT-1");
        var lowPriority = new FleetGoal(
            FleetGoalKind.MarketCoverage,
            "Market coverage low priority",
            FleetGoalPriority.MarketCoverage,
            OriginWaypoint: "X1-MKT-2");

        var orchestrator = new FleetOrchestrator(
            [EvaluatorReturning(lowPriority, highPriority)],
            ships,
            assignments,
            EmptyFleetGoalRepository(),
            bus,
            NullLogger<FleetOrchestrator>.Instance);

        await orchestrator.EvaluateAndAssignAsync(CancellationToken.None);

        // Only one ship; only the higher-priority goal (MarketScouting, priority 10) is assigned.
        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c => c.Goal.Kind == FleetGoalKind.MarketScouting),
            Arg.Any<DeliveryOptions>());
        await bus.DidNotReceive().SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c => c.Goal.Kind == FleetGoalKind.MarketCoverage),
            Arg.Any<DeliveryOptions>());
    }
}
