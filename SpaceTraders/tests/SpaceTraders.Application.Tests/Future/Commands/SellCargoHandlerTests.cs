using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class SellCargoHandlerTests
{
    [Fact]
    public async Task SellCargo_WhenNotDocked_PublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var handler = new SellCargoHandler(port, ships, agents, waypoints, assignments, bus, NullLogger<SellCargoHandler>.Instance);
        await handler.Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task SellCargo_PublishesShipCargoSoldEvent_WhenSuccessful()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100, CargoCurrent: 10, CargoCapacity: 60));
        waypoints.FindAsync("X1-AB-001", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-001", "X1-AB", "ORBITAL_STATION", 0, 0, true, false, DateTimeOffset.UtcNow));
        port.SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT-1", 55_000, new CargoModel(0, 60), 5_000));
        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Trade", null, null, null, null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var handler = new SellCargoHandler(port, ships, agents, waypoints, assignments, bus, NullLogger<SellCargoHandler>.Instance);
        await handler.Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        await bus.Received().PublishAsync(Arg.Any<ShipCargoSoldEvent>(), Arg.Any<DeliveryOptions>());
    }
}
