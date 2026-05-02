using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Domain.Events.Ships;

/// <summary>
/// Published when a ship successfully completes its active goal.
/// The orchestrator reacts to this event to re-evaluate fleet assignments.
/// </summary>
/// <remarks>Phase 8: goal lifecycle events introduced as part of goal-driven architecture migration.</remarks>
public sealed record GoalCompletedEvent
{
    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid CausationId { get; init; }

    public string ShipSymbol { get; init; }

    public Guid GoalId { get; init; }

    public ShipGoalKind GoalKind { get; init; }

    public GoalCompletedEvent(
        string shipSymbol,
        Guid goalId,
        ShipGoalKind goalKind,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
    {
        EventId = Guid.NewGuid();
        OccurredAt = occurredAt;
        CorrelationId = correlationId == Guid.Empty ? EventId : correlationId;
        CausationId = causationId;
        ShipSymbol = shipSymbol;
        GoalId = goalId;
        GoalKind = goalKind;
    }
}
