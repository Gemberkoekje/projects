using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class NavigateShipHandlerTests
{
    [Fact]
    public async Task NavigateShip_WhenDocked_DoesNotNavigateAndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100));

        var handler = new NavigateShipHandler(port, ships, bus, NullLogger<NavigateShipHandler>.Instance);
        await handler.Handle(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        await port.DidNotReceive().NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task NavigateShip_PublishesShipInTransitEvent()
    {
        var arrival = DateTimeOffset.UtcNow.AddMinutes(3);
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        port.NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>())
            .Returns(new NavigateActionResult(
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-001", "CRUISE", "X1-AB-002", arrival),
                new FuelModel(70, 100)));

        var handler = new NavigateShipHandler(port, ships, bus, NullLogger<NavigateShipHandler>.Instance);
        await handler.Handle(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipInTransitEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.OriginWaypointSymbol == "X1-AB-001" &&
                e.DestinationWaypointSymbol == "X1-AB-002" &&
                e.ArrivalTime == arrival),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task NavigateShip_WhenApiRejectsNotInOrbit_PublishesMismatchAndDoesNotThrow()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        port.NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>())
            .Returns<Task<NavigateActionResult>>(_ => throw new InvalidOperationException("Ship action failed. Ship is not currently in orbit at X1-AB-001."));

        var handler = new NavigateShipHandler(port, ships, bus, NullLogger<NavigateShipHandler>.Instance);

        var act = async () => await handler.Handle(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        await bus.Received(1).PublishAsync(
            Arg.Is<ShipStateMismatchEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.RequiredState == "IN_ORBIT" &&
                e.ActualState == "DOCKED"),
            Arg.Any<DeliveryOptions>());
    }
}
