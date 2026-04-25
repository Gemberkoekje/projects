using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class InitialAssignmentHandlerTests
{
    [Fact]
    public async Task Handle_AssignsPlannerResult_WhenShipExists()
    {
        var ships = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipRepository>();
        var planner = Substitute.For<IShipAssignmentPlanner>();
        var bus = Substitute.For<IMessageBus>();
        var ship = new ShipModel("SHIP-2", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100, null);
        var assignment = new AssignShipCommand("SHIP-2", "Trade", "X1-AB-001", "X1-AB-002", "IRON_ORE");

        ships.FindAsync("SHIP-2", Arg.Any<CancellationToken>()).Returns(ship);
        planner.PlanAsync(ship, Arg.Any<CancellationToken>()).Returns(assignment);

        var handler = new InitialAssignmentHandler(ships, planner, bus, NullLogger<InitialAssignmentHandler>.Instance);

        await handler.Handle(
            new NewShipPurchasedEvent("SHIP-2", ShipType.ShipMiningDrone, 50_000),
            CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipCommand>(c =>
                c.ShipSymbol == "SHIP-2" &&
                c.AssignmentType == "Trade" &&
                c.CargoSymbol == "IRON_ORE"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_AssignsIdle_WhenShipMissing()
    {
        var ships = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipRepository>();
        var planner = Substitute.For<IShipAssignmentPlanner>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-2", Arg.Any<CancellationToken>()).Returns((ShipModel)null!);

        var handler = new InitialAssignmentHandler(ships, planner, bus, NullLogger<InitialAssignmentHandler>.Instance);

        await handler.Handle(
            new NewShipPurchasedEvent("SHIP-2", ShipType.ShipMiningDrone, 50_000),
            CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipCommand>(c => c.ShipSymbol == "SHIP-2" && c.AssignmentType == "Idle"),
            Arg.Any<DeliveryOptions>());
    }
}
