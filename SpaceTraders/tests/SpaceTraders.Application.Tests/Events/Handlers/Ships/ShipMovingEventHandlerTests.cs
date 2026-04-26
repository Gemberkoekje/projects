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

public sealed class ShipMovingEventHandlerTests
{
    [Fact]
    public async Task DispatchAsync_SchedulesArrival_WhenArrivalIsInFuture()
    {
        var bus = Substitute.For<IMessageBus>();
        var inTransit = Substitute.For<IInTransitCommandAcceptor>();
        var services = new ServiceCollection();
        services.AddSingleton(inTransit);
        services.AddSingleton<IChainOfCommandEventHandler<ShipMovingEvent>, ShipMovingEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var now = DateTimeOffset.UtcNow;
        var @event = new ShipMovingEvent(
            "SHIP-1",
            "X1-AB-001",
            "X1-AB-002",
            now,
            now.AddMinutes(2),
            10,
            Guid.NewGuid(),
            Guid.Empty,
            now);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipMovingEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipArrivedEvent));
        result.IsScheduled.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_PublishesArrivalImmediately_WhenArrivalIsDue()
    {
        var bus = Substitute.For<IMessageBus>();
        var inTransit = Substitute.For<IInTransitCommandAcceptor>();
        var services = new ServiceCollection();
        services.AddSingleton(inTransit);
        services.AddSingleton<IChainOfCommandEventHandler<ShipMovingEvent>, ShipMovingEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var now = DateTimeOffset.UtcNow;
        var @event = new ShipMovingEvent(
            "SHIP-1",
            "X1-AB-001",
            "X1-AB-002",
            now.AddMinutes(-2),
            now.AddSeconds(-1),
            10,
            Guid.NewGuid(),
            Guid.Empty,
            now.AddMinutes(-2));

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipMovingEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipArrivedEvent));
        result.IsScheduled.Should().BeFalse();

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipArrivedEvent>(e =>
                e.ShipSymbol == @event.ShipSymbol &&
                e.WaypointSymbol == @event.DestinationWaypointSymbol &&
                e.SystemSymbol == "X1-AB" &&
                e.CorrelationId == @event.CorrelationId &&
                e.CausationId == @event.EventId),
            Arg.Any<DeliveryOptions>());
    }
}
