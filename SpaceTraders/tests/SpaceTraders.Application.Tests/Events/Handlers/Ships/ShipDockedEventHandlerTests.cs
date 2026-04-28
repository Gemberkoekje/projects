using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipDockedEventHandlerTests
{
    [Fact]
    public async Task DispatchAsync_EmitsShipIdleDockedEvent_WhenHandled()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipDockedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipDockedEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipIdleDockedEvent));
    }

    [Fact]
    public async Task DispatchAsync_PublishesShipIdleDockedEvent()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipDockedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipIdleDockedEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.SystemSymbol == "X1-AB" &&
                e.WaypointSymbol == "X1-AB-001"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task DispatchAsync_HandlesShipRefueledEvent_EmitsShipIdleDockedEvent()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipRefueledEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            100,
            100,
            2_000,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event as ShipDockedEvent, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipDockedEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task DispatchAsync_HandlesShipRoleSetEvent_EmitsShipIdleDockedEvent_WhenShipDocked()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipRoleSetEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            SpaceTraders.Domain.Enums.ShipRole.Explorer,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event as ShipDockedEvent, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipDockedEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipIdleDockedEvent));
    }

    [Fact]
    public async Task DispatchAsync_HandlesShipRoleSetEvent_EmitsShipIdleDockedEvent_WhenShipInOrbit()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipRoleSetEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            SpaceTraders.Domain.Enums.ShipRole.Excavator,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event as ShipDockedEvent, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipDockedEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipIdleDockedEvent));
    }

    [Fact]
    public async Task DispatchAsync_HandlesShipRoleSetEvent_EmitsShipInTransitEvent_WhenShipInTransit()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipRoleSetEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            SpaceTraders.Domain.Enums.ShipRole.Transport,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event as ShipDockedEvent, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipDockedEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipIdleDockedEvent));
    }

    [Fact]
    public async Task DispatchAsync_HandlesShipRoleSetEvent_EmitsShipIdleEvent_WhenShipNotFound()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipRoleSetEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            SpaceTraders.Domain.Enums.ShipRole.Explorer,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event as ShipDockedEvent, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipDockedEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipIdleDockedEvent));
    }
}
