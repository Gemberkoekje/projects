using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipRefueledEventHandlerTests
{
    [Fact]
    public async Task DispatchAsync_EmitsShipIdleEvent()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipRefueledEvent>, ShipRefueledEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipRefueledEvent(
            "SHIP-1",
            100,
            100,
            2_000,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipRefueledEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipIdleEvent));
        result.IsScheduled.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_PublishesShipIdleEvent_WithRefueledReason()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipRefueledEvent>, ShipRefueledEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipRefueledEvent(
            "SHIP-1",
            100,
            100,
            2_000,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        await dispatcher.DispatchAsync(@event, CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipIdleEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.Reason.Contains("refuel", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<Wolverine.DeliveryOptions>());
    }
}
