using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class AssignShipAfterSaleHandlerTests
{
    [Fact]
    public async Task Handle_ShipBecameIdle_AssignsPlannerResult_WhenShipExists()
    {
        var ships = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipRepository>();
        var assignments = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipAssignmentRepository>();
        var planner = Substitute.For<IShipAssignmentPlanner>();
        var bus = Substitute.For<IMessageBus>();
        var ship = new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100, null);
        var assignment = new AssignShipCommand("SHIP-1", "Scout", OriginWaypoint: "X1-AB-002");

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship);
        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipAssignmentDto)null!);
        planner.PlanAsync(ship, Arg.Any<CancellationToken>()).Returns(assignment);

        var handler = new ShipAssignmentOrchestratorHandler(ships, assignments, planner, bus, NullLogger<ShipAssignmentOrchestratorHandler>.Instance);

        await handler.Handle(new ShipBecameIdleEvent("SHIP-1", "Completed Trade assignment"), CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipCommand>(c => c.ShipSymbol == "SHIP-1" && c.AssignmentType == "Scout" && c.OriginWaypoint == "X1-AB-002"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_ShipBecameIdle_AssignsIdle_WhenShipMissing()
    {
        var ships = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipRepository>();
        var assignments = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipAssignmentRepository>();
        var planner = Substitute.For<IShipAssignmentPlanner>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipModel)null!);

        var handler = new ShipAssignmentOrchestratorHandler(ships, assignments, planner, bus, NullLogger<ShipAssignmentOrchestratorHandler>.Instance);

        await handler.Handle(new ShipBecameIdleEvent("SHIP-1", "Startup recovery"), CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipCommand>(c => c.ShipSymbol == "SHIP-1" && c.AssignmentType == "Idle"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_ShipArrivedAtWaypoint_PublishesIdle_WhenNoActiveAssignment()
    {
        var ships = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipRepository>();
        var assignments = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipAssignmentRepository>();
        var planner = Substitute.For<IShipAssignmentPlanner>();
        var bus = Substitute.For<IMessageBus>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipAssignmentDto)null!);

        var handler = new ShipAssignmentOrchestratorHandler(ships, assignments, planner, bus, NullLogger<ShipAssignmentOrchestratorHandler>.Instance);

        await handler.Handle(new ShipArrivedAtWaypointEvent("SHIP-1", new WaypointSymbol("X1-AB-002")), CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<object>(m => m is ShipBecameIdleEvent && ((ShipBecameIdleEvent)m).ShipSymbol == "SHIP-1" && ((ShipBecameIdleEvent)m).Reason.Contains("X1-AB-002", StringComparison.Ordinal)),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_ShipArrivedAtWaypoint_DoesNothing_WhenActiveAssignmentExists()
    {
        var ships = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipRepository>();
        var assignments = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipAssignmentRepository>();
        var planner = Substitute.For<IShipAssignmentPlanner>();
        var bus = Substitute.For<IMessageBus>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(
            new ShipAssignmentDto("SHIP-1", "Trade", "X1-AB-001", "X1-AB-002", "IRON_ORE", null, 3, DateTimeOffset.UtcNow, null));

        var handler = new ShipAssignmentOrchestratorHandler(ships, assignments, planner, bus, NullLogger<ShipAssignmentOrchestratorHandler>.Instance);

        await handler.Handle(new ShipArrivedAtWaypointEvent("SHIP-1", new WaypointSymbol("X1-AB-002")), CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(
            Arg.Is<object>(m => m is ShipBecameIdleEvent),
            Arg.Any<DeliveryOptions>());
    }
}
