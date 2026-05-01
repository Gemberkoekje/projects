using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipInTransitEventHandlerTests
{
    [Fact]
    public async Task Handle_SchedulesArrivalTick_WhenArrivalIsInFuture()
    {
        var bus = Substitute.For<IMessageBus>();
        var handler = new ShipInTransitEventHandler();

        var now = DateTimeOffset.UtcNow;
        var @event = new ShipInTransitEvent(
            "SHIP-1",
            "X1-AB-001",
            "X1-AB-002",
            now.AddMinutes(2),
            Guid.NewGuid(),
            Guid.Empty,
            now);

        await handler.Handle(@event, bus, CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e => e.ShipSymbol == @event.ShipSymbol && e.Reason == "Arrived"),
            Arg.Is<DeliveryOptions>(o => o != null && o.ScheduledTime != null));
    }

    [Fact]
    public async Task Handle_PublishesArrivalTickImmediately_WhenArrivalIsDue()
    {
        var bus = Substitute.For<IMessageBus>();
        var handler = new ShipInTransitEventHandler();

        var now = DateTimeOffset.UtcNow;
        var @event = new ShipInTransitEvent(
            "SHIP-1",
            "X1-AB-001",
            "X1-AB-002",
            now.AddSeconds(-1),
            Guid.NewGuid(),
            Guid.Empty,
            now.AddMinutes(-2));

        await handler.Handle(@event, bus, CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e =>
                e.ShipSymbol == @event.ShipSymbol &&
                e.Reason == "Arrived" &&
                e.CorrelationId == @event.CorrelationId &&
                e.CausationId == @event.EventId),
            Arg.Any<DeliveryOptions>());
    }
}
