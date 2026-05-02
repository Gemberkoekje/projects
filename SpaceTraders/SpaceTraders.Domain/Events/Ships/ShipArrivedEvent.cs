namespace SpaceTraders.Domain.Events.Ships;

/// <summary>
/// Published by <c>ShipEventScheduler</c> when a ship's scheduled arrival time has been reached.
/// Handlers should verify the <see cref="GoalId"/> still matches the ship's active goal before acting.
/// </summary>
/// <remarks>Phase 14b: introduced as part of the goal-driven architecture migration.</remarks>
public sealed record ShipArrivedEvent
{
    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public string ShipSymbol { get; init; }

    /// <summary>The goal that scheduled this wake-up; used for stale-wake-up detection.</summary>
    public Guid GoalId { get; init; }

    public ShipArrivedEvent(string shipSymbol, Guid goalId, DateTimeOffset occurredAt)
    {
        EventId = Guid.NewGuid();
        OccurredAt = occurredAt;
        ShipSymbol = shipSymbol;
        GoalId = goalId;
    }
}
