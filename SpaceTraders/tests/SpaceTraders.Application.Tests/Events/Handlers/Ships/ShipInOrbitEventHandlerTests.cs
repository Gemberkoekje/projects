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

public sealed class ShipInOrbitEventHandlerTests
{
    [Fact]
    public async Task DispatchAsync_DocksShip_WhenNoRoleAssignment()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = Substitute.For<IShipRepository>();
        var inOrbit = Substitute.For<IInOrbitCommandAcceptor>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton(ships);
        services.AddSingleton(inOrbit);
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipInOrbitEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipInOrbitEventHandler));
        result.Outcome.Should().Be("Handled");

        await inOrbit.Received(1).DockAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_HandlesWhenShipNotInOrbit()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = Substitute.For<IShipRepository>();
        var inOrbit = Substitute.For<IInOrbitCommandAcceptor>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton(ships);
        services.AddSingleton(inOrbit);
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipInOrbitEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().BeNullOrEmpty();
        await inOrbit.DidNotReceive().DockAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
