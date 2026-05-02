using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class ShipCommandResultTests
{
    [Fact]
    public async Task DockShip_WhenInOrbit_ReturnsAcceptedDockedResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100, CargoCurrent: 10, CargoCapacity: 40));

        port.DockShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new NavModel("DOCKED", "X1-AB", "X1-AB-001", "CRUISE", null, null));

        var handler = new DockShipHandler(port, ships, bus, NullLogger<DockShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new DockShipCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.Docked);
        result.WaypointSymbol.Should().Be("X1-AB-001");
        result.FuelCurrent.Should().Be(80);
        result.CargoCurrent.Should().Be(10);
    }

    [Fact]
    public async Task DockShip_WhenDocked_ReturnsRejectedResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));

        var handler = new DockShipHandler(port, ships, bus, NullLogger<DockShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new DockShipCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Status.Should().Be(ShipLocalStatus.Docked);
        await port.DidNotReceive().DockShipAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrbitShip_WhenDocked_ReturnsAcceptedInOrbitResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));

        port.OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new NavModel("IN_ORBIT", "X1-AB", "X1-AB-001", "CRUISE", null, null));

        var handler = new OrbitShipHandler(port, ships, bus, NullLogger<OrbitShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new OrbitShipCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.InOrbit);
    }

    [Fact]
    public async Task OrbitShip_WhenInOrbit_ReturnsRejectedResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));

        var handler = new OrbitShipHandler(port, ships, bus, NullLogger<OrbitShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new OrbitShipCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Status.Should().Be(ShipLocalStatus.InOrbit);
        await port.DidNotReceive().OrbitShipAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NavigateShip_WhenInOrbit_ReturnsAcceptedInTransitResult()
    {
        var arrival = DateTimeOffset.UtcNow.AddMinutes(3);
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var goals = Substitute.For<IShipGoalRepository>();
        var scheduler = Substitute.For<IShipEventScheduler>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        port.NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>())
            .Returns(new NavigateActionResult(
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-001", "CRUISE", "X1-AB-002", arrival),
                new FuelModel(70, 100)));

        var handler = new NavigateShipHandler(port, ships, goals, scheduler, bus, NullLogger<NavigateShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.InTransit);
        result.ArrivesAt.Should().Be(arrival);
        result.FuelCurrent.Should().Be(70);
    }

    [Fact]
    public async Task NavigateShip_WhenDocked_ReturnsRejectedResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var goals = Substitute.For<IShipGoalRepository>();
        var scheduler = Substitute.For<IShipEventScheduler>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100));

        var handler = new NavigateShipHandler(port, ships, goals, scheduler, bus, NullLogger<NavigateShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Status.Should().Be(ShipLocalStatus.Docked);
        await port.DidNotReceive().NavigateShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task DockShip_WhenAccepted_PublishesShipAutomationTickEvent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));

        port.DockShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new NavModel("DOCKED", "X1-AB", "X1-AB-001", "CRUISE", null, null));

        var handler = new DockShipHandler(port, ships, bus, NullLogger<DockShipHandler>.Instance);
        await handler.ExecuteAsync(new DockShipCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e => e.ShipSymbol == "SHIP-1" && e.Reason == "Docked"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task OrbitShip_WhenAccepted_PublishesShipAutomationTickEvent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));

        port.OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new NavModel("IN_ORBIT", "X1-AB", "X1-AB-001", "CRUISE", null, null));

        var handler = new OrbitShipHandler(port, ships, bus, NullLogger<OrbitShipHandler>.Instance);
        await handler.ExecuteAsync(new OrbitShipCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e => e.ShipSymbol == "SHIP-1" && e.Reason == "Undocked"),
            Arg.Any<DeliveryOptions>());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Phase 13c: SuppressContinuationTick — goal executors suppress the tick when they
    // invoke Dock/Orbit inline so that ShipGoalExecutorService publishes its own GoalStep tick.
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DockShip_WhenAccepted_AndSuppressContinuationTickIsTrue_DoesNotPublishTick()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));
        port.DockShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new NavModel("DOCKED", "X1-AB", "X1-AB-001", "CRUISE", null, null));

        var handler = new DockShipHandler(port, ships, bus, NullLogger<DockShipHandler>.Instance);
        await handler.ExecuteAsync(new DockShipCommand("SHIP-1") { SuppressContinuationTick = true }, CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(Arg.Any<ShipAutomationTickEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task OrbitShip_WhenAccepted_AndSuppressContinuationTickIsTrue_DoesNotPublishTick()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));
        port.OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new NavModel("IN_ORBIT", "X1-AB", "X1-AB-001", "CRUISE", null, null));

        var handler = new OrbitShipHandler(port, ships, bus, NullLogger<OrbitShipHandler>.Instance);
        await handler.ExecuteAsync(new OrbitShipCommand("SHIP-1") { SuppressContinuationTick = true }, CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(Arg.Any<ShipAutomationTickEvent>(), Arg.Any<DeliveryOptions>());
    }
}
