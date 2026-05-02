using NSubstitute;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipInTransitEventHandlerTests
{
    /// <summary>
    /// Phase 14c: ShipInTransitEventHandler is now a no-op; it no longer schedules ticks.
    /// The ShipEventScheduler handles arrival scheduling via ShipArrivedEvent instead.
    /// </summary>
    [Fact]
    public async Task Handle_DoesNotScheduleAnyTick_WhenArrivalIsInFuture()
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

        await handler.Handle(@event, CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_DoesNotScheduleAnyTick_WhenArrivalIsDue()
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

        await handler.Handle(@event, CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }
}
