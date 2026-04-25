using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
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
    public async Task Handle_AssignsPlannerResult_WhenShipExists()
    {
        var ships = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipRepository>();
        var planner = Substitute.For<IShipAssignmentPlanner>();
        var bus = Substitute.For<IMessageBus>();
        var ship = new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100, null);
        var assignment = new AssignShipCommand("SHIP-1", "Scout", OriginWaypoint: "X1-AB-002");

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship);
        planner.PlanAsync(ship, Arg.Any<CancellationToken>()).Returns(assignment);

        var handler = new AssignShipAfterSaleHandler(ships, planner, bus, NullLogger<AssignShipAfterSaleHandler>.Instance);

        await handler.Handle(new ShipCargoSoldEvent("SHIP-1", new TradeSymbol("IRON_ORE"), 10, 1000, 5000), CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipCommand>(c => c.ShipSymbol == "SHIP-1" && c.AssignmentType == "Scout" && c.OriginWaypoint == "X1-AB-002"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_AssignsIdle_WhenShipMissing()
    {
        var ships = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipRepository>();
        var planner = Substitute.For<IShipAssignmentPlanner>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipModel)null!);

        var handler = new AssignShipAfterSaleHandler(ships, planner, bus, NullLogger<AssignShipAfterSaleHandler>.Instance);

        await handler.Handle(new ShipCargoSoldEvent("SHIP-1", new TradeSymbol("IRON_ORE"), 10, 1000, 5000), CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipCommand>(c => c.ShipSymbol == "SHIP-1" && c.AssignmentType == "Idle"),
            Arg.Any<DeliveryOptions>());
    }
}
