namespace SpaceTraders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistent record for a fleet-level goal stored in the <c>fleet_goals</c> table.
/// </summary>
/// <remarks>Phase 11e: introduced as part of the goal-driven architecture migration.</remarks>
public sealed class FleetGoalRecord
{
    public Guid Id { get; init; }

    public string AgentToken { get; init; } = string.Empty;

    required public string Kind { get; set; }

    public int Priority { get; set; }

    required public string Description { get; set; }

    required public string PayloadJson { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; set; }
}
