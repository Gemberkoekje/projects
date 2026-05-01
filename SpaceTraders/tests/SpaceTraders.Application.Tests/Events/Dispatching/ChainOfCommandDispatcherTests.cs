using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Dispatching;

public sealed class ChainOfCommandDispatcherTests
{
    // Phase 7e: ship events (ShipInOrbitEvent, ShipDockedEvent, ShipInTransitEvent) no longer derive
    // from ChainOfCommandEvent. These local test-only types stand in for the dispatcher mechanism tests
    // until Phase 7f deletes the chain infrastructure entirely.
    private sealed record TestOrbitEvent : ChainOfCommandEvent
    {
        public string ShipSymbol { get; init; }

        public TestOrbitEvent(string shipSymbol, Guid correlationId, Guid causationId, DateTimeOffset occurredAt)
            : base(correlationId, causationId, occurredAt)
        {
            ShipSymbol = shipSymbol;
        }
    }

    private sealed record TestDockedEvent : ChainOfCommandEvent
    {
        public string ShipSymbol { get; init; }

        public TestDockedEvent(string shipSymbol, Guid correlationId, Guid causationId, DateTimeOffset occurredAt)
            : base(correlationId, causationId, occurredAt)
        {
            ShipSymbol = shipSymbol;
        }
    }

    private sealed record TestTransitEvent : ChainOfCommandEvent
    {
        public string ShipSymbol { get; init; }

        public DateTimeOffset ArrivalTime { get; init; }

        public string DestinationWaypointSymbol { get; init; }

        public TestTransitEvent(string shipSymbol, string destination, DateTimeOffset arrivalTime, Guid correlationId, Guid causationId, DateTimeOffset occurredAt)
            : base(correlationId, causationId, occurredAt)
        {
            ShipSymbol = shipSymbol;
            DestinationWaypointSymbol = destination;
            ArrivalTime = arrivalTime;
        }
    }

    [Fact]
    public async Task DispatchAsync_UsesRegistrationOrder_AndStopsAtFirstHandled()
    {
        var bus = Substitute.For<IMessageBus>();
        var tracker = new CallTracker();
        var services = new ServiceCollection();

        services.AddSingleton(tracker);
        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<TestOrbitEvent>, SkipOrbitHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<TestOrbitEvent>, HandleOrbitHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<TestOrbitEvent>, LateOrbitHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new TestOrbitEvent("SHIP-1", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(HandleOrbitHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().BeEmpty();
        result.IsScheduled.Should().BeFalse();
        tracker.Calls.Should().ContainInOrder(nameof(SkipOrbitHandler), nameof(HandleOrbitHandler));
        tracker.Calls.Should().NotContain(nameof(LateOrbitHandler));
        await bus.DidNotReceive().PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task DispatchAsync_DoesNotPublish_WhenHandlerReturnsHandledWithoutNextEvent()
    {
        var bus = Substitute.For<IMessageBus>();
        var services = new ServiceCollection();

        services.AddSingleton(bus);
        services.AddSingleton<IChainOfCommandEventHandler<TestDockedEvent>, HandleWithoutNextDockedHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new TestDockedEvent("SHIP-1", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow);

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

        services.AddSingleton<IChainOfCommandEventHandler<TestTransitEvent>, ScheduleArrivalHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var now = DateTimeOffset.UtcNow;
        var @event = new TestTransitEvent("SHIP-1", "X1-AB-002", now.AddMinutes(3), Guid.NewGuid(), Guid.Empty, now);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ScheduleArrivalHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(TestOrbitEvent));
        result.IsScheduled.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_Throws_WhenNoHandlersRegistered()
    {
        var bus = Substitute.For<IMessageBus>();
        var services = new ServiceCollection();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new TestOrbitEvent("SHIP-1", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow);

        var action = async () => await dispatcher.DispatchAsync(@event, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class CallTracker
    {
        public List<string> Calls { get; } = [];
    }

    private sealed class SkipOrbitHandler(CallTracker tracker) : IChainOfCommandEventHandler<TestOrbitEvent>
    {

        public Task<ChainOfCommandHandlerResult> HandleAsync(TestOrbitEvent @event, CancellationToken cancellationToken)
        {
            tracker.Calls.Add(nameof(SkipOrbitHandler));
            return Task.FromResult(ChainOfCommandHandlerResult.Skipped());
        }
    }

    private sealed class HandleOrbitHandler(CallTracker tracker) : IChainOfCommandEventHandler<TestOrbitEvent>
    {

        public Task<ChainOfCommandHandlerResult> HandleAsync(TestOrbitEvent @event, CancellationToken cancellationToken)
        {
            tracker.Calls.Add(nameof(HandleOrbitHandler));
            return Task.FromResult(ChainOfCommandHandlerResult.Handled());
        }
    }

    private sealed class LateOrbitHandler(CallTracker tracker) : IChainOfCommandEventHandler<TestOrbitEvent>
    {

        public Task<ChainOfCommandHandlerResult> HandleAsync(TestOrbitEvent @event, CancellationToken cancellationToken)
        {
            tracker.Calls.Add(nameof(LateOrbitHandler));
            return Task.FromResult(ChainOfCommandHandlerResult.Skipped());
        }
    }

    private sealed class HandleWithoutNextDockedHandler : IChainOfCommandEventHandler<TestDockedEvent>
    {

        public Task<ChainOfCommandHandlerResult> HandleAsync(TestDockedEvent @event, CancellationToken cancellationToken)
            => Task.FromResult(ChainOfCommandHandlerResult.Handled());
    }

    private sealed class ScheduleArrivalHandler : IChainOfCommandEventHandler<TestTransitEvent>
    {

        public async Task<ChainOfCommandHandlerResult> HandleAsync(TestTransitEvent @event, CancellationToken cancellationToken)
        {
            var orbitEvent = new TestOrbitEvent(
                @event.ShipSymbol,
                @event.CorrelationId,
                @event.EventId,
                DateTimeOffset.UtcNow);

#pragma warning disable CS0618
            return await Task.FromResult(ChainOfCommandHandlerResult.Scheduled(orbitEvent, @event.ArrivalTime));
#pragma warning restore CS0618
        }
    }
}
