using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class ShipActionHandlerTests
{
    [Fact]
    public async Task DockShip_WhenNotInOrbit_PublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));

        var handler = new DockShipHandler(port, ships, bus, NullLogger<DockShipHandler>.Instance);
        await handler.Handle(new DockShipCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<SpaceTraders.Domain.Events.Ships.ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task OrbitShip_WhenNotDocked_PublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));

        var handler = new OrbitShipHandler(port, ships, bus, NullLogger<OrbitShipHandler>.Instance);
        await handler.Handle(new OrbitShipCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<SpaceTraders.Domain.Events.Ships.ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task InstallMount_WhenNotDocked_PublishesMismatch_AndSkipsPortCall()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));

        var handler = new InstallMountHandler(port, ships, agents, waypoints, bus, NullLogger<InstallMountHandler>.Instance);
        await handler.Handle(new InstallMountCommand("SHIP-1", "MOUNT_SENSOR_ARRAY_II"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<SpaceTraders.Domain.Events.Ships.ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
        await port.DidNotReceive().InstallMountAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallMount_WhenDockedAtNonShipyard_PublishesMismatch_AndSkipsPortCall()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));
        waypoints.FindAsync("X1-AB-001", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-001", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow));

        var handler = new InstallMountHandler(port, ships, agents, waypoints, bus, NullLogger<InstallMountHandler>.Instance);
        await handler.Handle(new InstallMountCommand("SHIP-1", "MOUNT_SENSOR_ARRAY_II"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<SpaceTraders.Domain.Events.Ships.ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
        await port.DidNotReceive().InstallMountAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
