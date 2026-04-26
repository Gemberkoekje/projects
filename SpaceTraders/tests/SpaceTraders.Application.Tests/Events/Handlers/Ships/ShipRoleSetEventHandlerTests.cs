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

public sealed class ShipRoleSetEventHandlerTests
{
    [Fact]
    public async Task DispatchAsync_EmitsShipUndockedEvent_WhenShipFound()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = Substitute.For<IShipRepository>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var services = new ServiceCollection();
        services.AddSingleton(bus);
        services.AddSingleton(ships);
        services.AddSingleton<IChainOfCommandEventHandler<ShipRoleSetEvent>, ShipRoleSetEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var correlationId = Guid.NewGuid();
        var @event = new ShipRoleSetEvent(
            "SHIP-1",
            SpaceTraders.Domain.Enums.ShipRole.Explorer,
            correlationId,
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipRoleSetEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipUndockedEvent));
        result.IsScheduled.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_EmitsShipUndockedEvent_PropagatesCorrelationId()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = Substitute.For<IShipRepository>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var services = new ServiceCollection();
        services.AddSingleton(bus);
        services.AddSingleton(ships);
        services.AddSingleton<IChainOfCommandEventHandler<ShipRoleSetEvent>, ShipRoleSetEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var correlationId = Guid.NewGuid();
        var @event = new ShipRoleSetEvent(
            "SHIP-1",
            SpaceTraders.Domain.Enums.ShipRole.Explorer,
            correlationId,
            Guid.Empty,
            DateTimeOffset.UtcNow);

        await dispatcher.DispatchAsync(@event, CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipUndockedEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.SystemSymbol == "X1-AB" &&
                e.WaypointSymbol == "X1-AB-001" &&
                e.CorrelationId == correlationId),
            Arg.Any<Wolverine.DeliveryOptions>());
    }

    [Fact]
    public async Task DispatchAsync_EmitsShipIdleEvent_WhenShipNotFound()
    {
        var bus = Substitute.For<IMessageBus>();
        var ships = Substitute.For<IShipRepository>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns((ShipModel)null);

        var services = new ServiceCollection();
        services.AddSingleton(bus);
        services.AddSingleton(ships);
        services.AddSingleton<IChainOfCommandEventHandler<ShipRoleSetEvent>, ShipRoleSetEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipRoleSetEvent(
            "SHIP-1",
            SpaceTraders.Domain.Enums.ShipRole.Explorer,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.NextEventType.Should().Be(nameof(ShipIdleEvent));
    }
}
