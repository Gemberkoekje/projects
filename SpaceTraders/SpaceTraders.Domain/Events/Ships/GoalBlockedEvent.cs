using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Domain.Events.Ships;

/// <summary>
/// Published when a ship cannot make progress toward its active goal.
/// The orchestrator reacts by reassigning the ship or purchasing missing equipment.
/// </summary>
/// <remarks>Phase 8: goal lifecycle events introduced as part of goal-driven architecture migration.</remarks>
public sealed record GoalBlockedEvent
{
    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid CausationId { get; init; }

    public string ShipSymbol { get; init; }

    public Guid GoalId { get; init; }

    public ShipGoalKind GoalKind { get; init; }

    public string Reason { get; init; }

    public GoalBlockedEvent(
        string shipSymbol,
        Guid goalId,
        ShipGoalKind goalKind,
        string reason,
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
        Reason = reason;
    }
}
