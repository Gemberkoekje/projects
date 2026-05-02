namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Schedules future ship events (arrival and cooldown expiry) to be fired at a specific time,
/// replacing the polling tick loop for in-transit and cooling-down ships.
/// </summary>
/// <remarks>Phase 14a: introduced as part of the goal-driven architecture migration.</remarks>
public interface IShipEventScheduler
{
    /// <summary>
    /// Schedules a <c>ShipArrivedEvent</c> to be published at <paramref name="arrivalTime"/>.
    /// </summary>
    Task ScheduleArrivalAsync(string shipSymbol, Guid goalId, DateTimeOffset arrivalTime, CancellationToken ct);

    /// <summary>
    /// Schedules a <c>ShipCooldownExpiredEvent</c> to be published at <paramref name="expiresAt"/>.
    /// </summary>
    Task ScheduleCooldownExpiryAsync(string shipSymbol, Guid goalId, DateTimeOffset expiresAt, CancellationToken ct);

    /// <summary>
    /// Cancels any pending scheduled event for the given ship and goal, if one exists.
    /// No-op if no matching entry is found.
    /// </summary>
    Task CancelScheduledAsync(string shipSymbol, Guid goalId, CancellationToken ct);
}
