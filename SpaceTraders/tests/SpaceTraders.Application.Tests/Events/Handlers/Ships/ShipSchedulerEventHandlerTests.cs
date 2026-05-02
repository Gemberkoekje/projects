using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

/// <summary>
/// Phase 14e: unit tests for <see cref="ShipArrivedEventHandler"/> and
/// <see cref="ShipCooldownExpiredEventHandler"/>.
/// </summary>
public sealed class ShipArrivedEventHandlerTests
{
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private ShipArrivedEventHandler CreateHandler() =>
        new(_goals, NullLogger<ShipArrivedEventHandler>.Instance);

    [Fact]
    public async Task Handle_WhenGoalIdMatches_PublishesAutomationTick()
    {
        var goalId = Guid.NewGuid();
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new IdleGoal { GoalId = goalId });

        var @event = new ShipArrivedEvent("SHIP-1", goalId, DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _bus, CancellationToken.None);

        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.Reason == "Arrived"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_WhenGoalIdDoesNotMatch_IgnoresStaleWakeUp()
    {
        var activeGoalId = Guid.NewGuid();
        var staleGoalId = Guid.NewGuid();
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new IdleGoal { GoalId = activeGoalId });

        var @event = new ShipArrivedEvent("SHIP-1", staleGoalId, DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _bus, CancellationToken.None);

        await _bus.DidNotReceive().PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_WhenNoActiveGoal_IgnoresStaleWakeUp()
    {
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        var @event = new ShipArrivedEvent("SHIP-1", Guid.NewGuid(), DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _bus, CancellationToken.None);

        await _bus.DidNotReceive().PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }
}

public sealed class ShipCooldownExpiredEventHandlerTests
{
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private ShipCooldownExpiredEventHandler CreateHandler() =>
        new(_goals, _ships, NullLogger<ShipCooldownExpiredEventHandler>.Instance);

    [Fact]
    public async Task Handle_WhenGoalIdMatches_ClearsCooldownAndPublishesTick()
    {
        var goalId = Guid.NewGuid();
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new IdleGoal { GoalId = goalId });

        var @event = new ShipCooldownExpiredEvent("SHIP-1", goalId, DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _bus, CancellationToken.None);

        await _ships.Received(1).UpdateCooldownAsync("SHIP-1", null, Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.Reason == "CooldownExpired"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_WhenGoalIdDoesNotMatch_IgnoresStaleWakeUp()
    {
        var activeGoalId = Guid.NewGuid();
        var staleGoalId = Guid.NewGuid();
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new IdleGoal { GoalId = activeGoalId });

        var @event = new ShipCooldownExpiredEvent("SHIP-1", staleGoalId, DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _bus, CancellationToken.None);

        await _ships.DidNotReceive().UpdateCooldownAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_WhenNoActiveGoal_IgnoresStaleWakeUp()
    {
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        var @event = new ShipCooldownExpiredEvent("SHIP-1", Guid.NewGuid(), DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _bus, CancellationToken.None);

        await _ships.DidNotReceive().UpdateCooldownAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }
}
