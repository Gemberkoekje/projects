using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

/// <summary>
/// Unit tests for <see cref="ShipArrivedEventHandler"/> and
/// <see cref="ShipCooldownExpiredEventHandler"/>.
/// </summary>
public sealed class ShipArrivedEventHandlerTests
{
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipGoalExecutorService _goalExecutor = Substitute.For<IShipGoalExecutorService>();

    private ShipArrivedEventHandler CreateHandler() =>
        new(_goals, NullLogger<ShipArrivedEventHandler>.Instance);

    [Fact]
    public async Task Handle_WhenGoalIdMatches_CallsGoalExecutor()
    {
        var goalId = Guid.NewGuid();
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new IdleGoal { GoalId = goalId });

        var @event = new ShipArrivedEvent("SHIP-1", goalId, DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _goalExecutor, CancellationToken.None);

        await _goalExecutor.Received(1).ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenGoalIdDoesNotMatch_IgnoresStaleWakeUp()
    {
        var activeGoalId = Guid.NewGuid();
        var staleGoalId = Guid.NewGuid();
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new IdleGoal { GoalId = activeGoalId });

        var @event = new ShipArrivedEvent("SHIP-1", staleGoalId, DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _goalExecutor, CancellationToken.None);

        await _goalExecutor.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNoActiveGoal_IgnoresStaleWakeUp()
    {
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        var @event = new ShipArrivedEvent("SHIP-1", Guid.NewGuid(), DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _goalExecutor, CancellationToken.None);

        await _goalExecutor.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

public sealed class ShipCooldownExpiredEventHandlerTests
{
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalExecutorService _goalExecutor = Substitute.For<IShipGoalExecutorService>();

    private ShipCooldownExpiredEventHandler CreateHandler() =>
        new(_goals, _ships, NullLogger<ShipCooldownExpiredEventHandler>.Instance);

    [Fact]
    public async Task Handle_WhenGoalIdMatches_ClearsCooldownAndCallsGoalExecutor()
    {
        var goalId = Guid.NewGuid();
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new IdleGoal { GoalId = goalId });

        var @event = new ShipCooldownExpiredEvent("SHIP-1", goalId, DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _goalExecutor, CancellationToken.None);

        await _ships.Received(1).UpdateCooldownAsync("SHIP-1", null, Arg.Any<CancellationToken>());
        await _goalExecutor.Received(1).ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenGoalIdDoesNotMatch_IgnoresStaleWakeUp()
    {
        var activeGoalId = Guid.NewGuid();
        var staleGoalId = Guid.NewGuid();
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new IdleGoal { GoalId = activeGoalId });

        var @event = new ShipCooldownExpiredEvent("SHIP-1", staleGoalId, DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _goalExecutor, CancellationToken.None);

        await _ships.DidNotReceive().UpdateCooldownAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _goalExecutor.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNoActiveGoal_IgnoresStaleWakeUp()
    {
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        var @event = new ShipCooldownExpiredEvent("SHIP-1", Guid.NewGuid(), DateTimeOffset.UtcNow);

        await CreateHandler().Handle(@event, _goalExecutor, CancellationToken.None);

        await _ships.DidNotReceive().UpdateCooldownAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _goalExecutor.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
