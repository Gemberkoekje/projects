using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Dispatching;

public sealed class ChainOfCommandDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_UsesPriorityOrder_AndStopsAtFirstHandled()
    {
        var bus = Substitute.For<IMessageBus>();
        var tracker = new CallTracker();
        var services = new ServiceCollection();

        services.AddSingleton(tracker);
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, SkipUndockedHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, HandleUndockedHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, LateUndockedHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipUndockedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(HandleUndockedHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipAssignmentTypeSetEvent));
        result.IsScheduled.Should().BeFalse();
        tracker.Calls.Should().ContainInOrder(nameof(SkipUndockedHandler), nameof(HandleUndockedHandler));
        tracker.Calls.Should().NotContain(nameof(LateUndockedHandler));
        await bus.Received(1).PublishAsync(Arg.Any<ShipAssignmentTypeSetEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task DispatchAsync_DoesNotPublish_WhenHandlerReturnsHandledWithoutNextEvent()
    {
        var bus = Substitute.For<IMessageBus>();
        var services = new ServiceCollection();

        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipDockedEvent>, HandleWithoutNextDockedHandler>();

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

        result.HandlerName.Should().Be(nameof(HandleWithoutNextDockedHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().BeEmpty();
        result.IsScheduled.Should().BeFalse();
        await bus.DidNotReceive().PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task DispatchAsync_SchedulesEvent_WhenHandlerReturnsScheduled()
    {
        var bus = Substitute.For<IMessageBus>();
        var services = new ServiceCollection();
        var inTransit = Substitute.For<IInTransitCommandAcceptor>();

        services.AddSingleton(inTransit);
        services.AddSingleton<IChainOfCommandEventHandler<ShipInTransitEvent>, ScheduleArrivalHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var now = DateTimeOffset.UtcNow;
        var @event = new ShipInTransitEvent(
            "SHIP-1",
            "X1-AB-001",
            "X1-AB-002",
            now.AddMinutes(3),
            Guid.NewGuid(),
            Guid.Empty,
            now);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ScheduleArrivalHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipArrivedEvent));
        result.IsScheduled.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_Throws_WhenNoHandlersRegistered()
    {
        var bus = Substitute.For<IMessageBus>();
        var services = new ServiceCollection();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipUndockedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var action = async () => await dispatcher.DispatchAsync(@event, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class CallTracker
    {
        public List<string> Calls { get; } = [];
    }

    private sealed class SkipUndockedHandler(CallTracker tracker) : IChainOfCommandEventHandler<ShipUndockedEvent>
    {
        public int Priority => 100;

        public Task<ChainOfCommandHandlerResult> HandleAsync(ShipUndockedEvent @event, CancellationToken cancellationToken)
        {
            tracker.Calls.Add(nameof(SkipUndockedHandler));
            return Task.FromResult(ChainOfCommandHandlerResult.Skipped());
        }
    }

    private sealed class HandleUndockedHandler(CallTracker tracker) : IChainOfCommandEventHandler<ShipUndockedEvent>
    {
        public int Priority => 200;

        public Task<ChainOfCommandHandlerResult> HandleAsync(ShipUndockedEvent @event, CancellationToken cancellationToken)
        {
            tracker.Calls.Add(nameof(HandleUndockedHandler));
            var nextEvent = new ShipAssignmentTypeSetEvent(@event.ShipSymbol, @event.SystemSymbol, @event.WaypointSymbol, "Idle", @event.CorrelationId, @event.EventId, DateTimeOffset.UtcNow);
            return Task.FromResult(ChainOfCommandHandlerResult.Handled(nextEvent));
        }
    }

    private sealed class LateUndockedHandler(CallTracker tracker) : IChainOfCommandEventHandler<ShipUndockedEvent>
    {
        public int Priority => 300;

        public Task<ChainOfCommandHandlerResult> HandleAsync(ShipUndockedEvent @event, CancellationToken cancellationToken)
        {
            tracker.Calls.Add(nameof(LateUndockedHandler));
            return Task.FromResult(ChainOfCommandHandlerResult.Skipped());
        }
    }

    private sealed class HandleWithoutNextDockedHandler : IChainOfCommandEventHandler<ShipDockedEvent>
    {
        public int Priority => 100;

        public Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
            => Task.FromResult(ChainOfCommandHandlerResult.Handled());
    }

    private sealed class ScheduleArrivalHandler(IInTransitCommandAcceptor inTransit) : IChainOfCommandEventHandler<ShipInTransitEvent>
    {
        public int Priority => 100;

        public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInTransitEvent @event, CancellationToken cancellationToken)
        {
            var arrivalEvent = new ShipArrivedEvent(
                @event.ShipSymbol,
                "X1-AB",
                @event.DestinationWaypointSymbol,
                @event.ArrivalTime,
                @event.CorrelationId,
                @event.EventId,
                DateTimeOffset.UtcNow);

            await inTransit.ScheduleArrivalAsync(arrivalEvent, @event.ArrivalTime, cancellationToken);
            return ChainOfCommandHandlerResult.Scheduled(arrivalEvent, @event.ArrivalTime);
        }
    }
}
