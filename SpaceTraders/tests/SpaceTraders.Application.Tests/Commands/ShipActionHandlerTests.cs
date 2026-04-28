using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class ShipActionHandlerTests
{
    [Fact]
    public async Task DockShip_WhenNotInOrbit_PublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipRepository>();
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
        var ships = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));

        var handler = new OrbitShipHandler(port, ships, bus, NullLogger<OrbitShipHandler>.Instance);
        await handler.Handle(new OrbitShipCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<SpaceTraders.Domain.Events.Ships.ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }
}
