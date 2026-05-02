namespace SpaceTraders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistent record for a single completed or blocked ship goal in the <c>ship_goal_history</c> table.
/// </summary>
/// <remarks>Phase 17e: introduced as part of the fleet dashboard implementation.</remarks>
public sealed class ShipGoalHistoryRecord
{
    public Guid Id { get; init; }

    public string AgentToken { get; init; } = string.Empty;

    required public string ShipSymbol { get; init; }

    required public string GoalKind { get; init; }

    public Guid GoalId { get; init; }

    /// <summary>'Completed' or 'Blocked'.</summary>
    required public string Outcome { get; init; }

    /// <summary>Blocking reason when <see cref="Outcome"/> is 'Blocked'; <c>null</c> otherwise.</summary>
    public string? Reason { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset EndedAt { get; init; }
}
