using FluentAssertions;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Domain.Tests.Events;

public sealed class GoalEventsTests
{
    [Fact]
    public void GoalCompletedEvent_PreservesAllProvidedFields()
    {
        var shipSymbol = "SHIP-1";
        var goalId = Guid.NewGuid();
        var goalKind = ShipGoalKind.MineResource;
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var @event = new GoalCompletedEvent(shipSymbol, goalId, goalKind, correlationId, causationId, occurredAt);

        @event.ShipSymbol.Should().Be(shipSymbol);
        @event.GoalId.Should().Be(goalId);
        @event.GoalKind.Should().Be(goalKind);
        @event.CorrelationId.Should().Be(correlationId);
        @event.CausationId.Should().Be(causationId);
        @event.OccurredAt.Should().Be(occurredAt);
        @event.EventId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void GoalCompletedEvent_EmptyCorrelationId_UsesEventId()
    {
        var @event = new GoalCompletedEvent("SHIP-1", Guid.NewGuid(), ShipGoalKind.Idle, Guid.Empty, Guid.NewGuid(), DateTimeOffset.UtcNow);

        @event.CorrelationId.Should().Be(@event.EventId);
    }

    [Fact]
    public void GoalBlockedEvent_PreservesAllProvidedFields()
    {
        var shipSymbol = "SHIP-2";
        var goalId = Guid.NewGuid();
        var goalKind = ShipGoalKind.SiphonResource;
        var reason = "no siphon mount installed";
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var @event = new GoalBlockedEvent(shipSymbol, goalId, goalKind, reason, correlationId, causationId, occurredAt);

        @event.ShipSymbol.Should().Be(shipSymbol);
        @event.GoalId.Should().Be(goalId);
        @event.GoalKind.Should().Be(goalKind);
        @event.Reason.Should().Be(reason);
        @event.CorrelationId.Should().Be(correlationId);
        @event.CausationId.Should().Be(causationId);
        @event.OccurredAt.Should().Be(occurredAt);
        @event.EventId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void GoalBlockedEvent_EmptyCorrelationId_UsesEventId()
    {
        var @event = new GoalBlockedEvent("SHIP-2", Guid.NewGuid(), ShipGoalKind.MineResource, "blocked", Guid.Empty, Guid.NewGuid(), DateTimeOffset.UtcNow);

        @event.CorrelationId.Should().Be(@event.EventId);
    }
}
