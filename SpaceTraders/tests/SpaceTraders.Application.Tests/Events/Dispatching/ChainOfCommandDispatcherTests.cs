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
    public async Task DispatchAsync_UsesRegistrationOrder_AndStopsAtFirstHandled()
    {
        var bus = Substitute.For<IMessageBus>();
        var tracker = new CallTracker();
        var services = new ServiceCollection();

        services.AddSingleton(tracker);
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, SkipInOrbitHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, HandleInOrbitHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, LateInOrbitHandler>();

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

        result.HandlerName.Should().Be(nameof(HandleInOrbitHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().BeEmpty();
        result.IsScheduled.Should().BeFalse();
        tracker.Calls.Should().ContainInOrder(nameof(SkipInOrbitHandler), nameof(HandleInOrbitHandler));
        tracker.Calls.Should().NotContain(nameof(LateInOrbitHandler));
        await bus.DidNotReceive().PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
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
        result.NextEventType.Should().Be(nameof(ShipInOrbitEvent));
        result.IsScheduled.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_Throws_WhenNoHandlersRegistered()
    {
        var bus = Substitute.For<IMessageBus>();
        var services = new ServiceCollection();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipInOrbitEvent(
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

    private sealed class SkipInOrbitHandler(CallTracker tracker) : IChainOfCommandEventHandler<ShipInOrbitEvent>
    {

        public Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
        {
            tracker.Calls.Add(nameof(SkipInOrbitHandler));
            return Task.FromResult(ChainOfCommandHandlerResult.Skipped());
        }
    }

    private sealed class HandleInOrbitHandler(CallTracker tracker) : IChainOfCommandEventHandler<ShipInOrbitEvent>
    {

        public Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
        {
            tracker.Calls.Add(nameof(HandleInOrbitHandler));
            return Task.FromResult(ChainOfCommandHandlerResult.Handled());
        }
    }

    private sealed class LateInOrbitHandler(CallTracker tracker) : IChainOfCommandEventHandler<ShipInOrbitEvent>
    {

        public Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
        {
            tracker.Calls.Add(nameof(LateInOrbitHandler));
            return Task.FromResult(ChainOfCommandHandlerResult.Skipped());
        }
    }

    private sealed class HandleWithoutNextDockedHandler : IChainOfCommandEventHandler<ShipDockedEvent>
    {

        public Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
            => Task.FromResult(ChainOfCommandHandlerResult.Handled());
    }

    private sealed class ScheduleArrivalHandler : IChainOfCommandEventHandler<ShipInTransitEvent>
    {

        public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInTransitEvent @event, CancellationToken cancellationToken)
        {
            var orbitEvent = new ShipInOrbitEvent(
                @event.ShipSymbol,
                "X1-AB",
                @event.DestinationWaypointSymbol,
                @event.CorrelationId,
                @event.EventId,
                DateTimeOffset.UtcNow);

#pragma warning disable CS0618
            return await Task.FromResult(ChainOfCommandHandlerResult.Scheduled(orbitEvent, @event.ArrivalTime));
#pragma warning restore CS0618
        }
    }
}
