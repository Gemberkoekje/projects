using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipStateMismatchEventHandlerTests
{
    [Fact]
    public async Task DispatchAsync_DocksShip_WhenShipIsInOrbit()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = Substitute.For<IShipRepository>();
        var inOrbitCommands = Substitute.For<IInOrbitCommandAcceptor>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var services = new ServiceCollection();
        services.AddSingleton(bus);
        services.AddSingleton(ships);
        services.AddSingleton(inOrbitCommands);
        services.AddSingleton<IChainOfCommandEventHandler<ShipStateMismatchEvent>, ShipStateMismatchEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipStateMismatchEvent(
            "SHIP-1",
            "NavigateShipCommand",
            "IN_ORBIT",
            "DOCKED",
            "Recovery needed",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipStateMismatchEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().BeNullOrEmpty();

        await inOrbitCommands.Received(1).DockAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_EmitsShipIdleEvent_WhenShipIsNotInOrbit()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = Substitute.For<IShipRepository>();
        var inOrbitCommands = Substitute.For<IInOrbitCommandAcceptor>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100));

        var services = new ServiceCollection();
        services.AddSingleton(bus);
        services.AddSingleton(ships);
        services.AddSingleton(inOrbitCommands);
        services.AddSingleton<IChainOfCommandEventHandler<ShipStateMismatchEvent>, ShipStateMismatchEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipStateMismatchEvent(
            "SHIP-1",
            "NavigateShipCommand",
            "DOCKED",
            "IN_ORBIT",
            "Recovery needed",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipStateMismatchEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().BeNullOrEmpty();
    }
}
