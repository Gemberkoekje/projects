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
    public async Task DispatchAsync_EmitsShipIdleDockedEvent()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
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
        result.IsScheduled.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_PublishesShipIdleDockedEvent_WithDockedReason()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
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

        await dispatcher.DispatchAsync(@event, CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipIdleDockedEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.Reason.Contains("docked", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<Wolverine.DeliveryOptions>());
    }
}
