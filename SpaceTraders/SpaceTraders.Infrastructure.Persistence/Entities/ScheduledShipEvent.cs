namespace SpaceTraders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistent record for a scheduled future ship event stored in the <c>scheduled_ship_events</c> table.
/// </summary>
/// <remarks>Phase 14a: introduced as part of the goal-driven architecture migration.</remarks>
public sealed class ScheduledShipEvent
{
    public string ShipSymbol { get; init; } = string.Empty;

    public Guid GoalId { get; init; }

    /// <summary>Either <c>Arrival</c> or <c>CooldownExpiry</c>.</summary>
    public string EventKind { get; init; } = string.Empty;

    public DateTimeOffset TriggerAt { get; set; }
}
