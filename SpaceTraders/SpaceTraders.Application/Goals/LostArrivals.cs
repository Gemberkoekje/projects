using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Goals;

/// <summary>
/// Arrivals that were never handled (B70). Only the arrival's dock takes a ship out of transit in the cache, and an arrival
/// whose handling fails for good is gone: the scheduler deleted its timer when it fired, and Wolverine drops a message after
/// its last retry. On 2026-10-06 SPECTER-5's arrival at X1-FJ91-C45 fell in a 35-second outage of the game's API; the dock
/// failed four times, and the ship stayed stored in transit for hours, every step of its goal waiting for an arrival that
/// never came, until a restart's sync stored where the API said it was. Once a ship stored in transit is more than
/// <see cref="Margin"/> past its arrival, its arrival is scheduled again: the whole arrival runs again, the dock included.
/// </summary>
/// <remarks>A singleton; thread-safe. A restart forgets it: the startup sync stores every ship as the API has it.</remarks>
public sealed class LostArrivals(IShipEventScheduler scheduler)
{
    /// <summary>
    /// How long past its arrival time a ship stored in transit counts as lost, and how long until it is scheduled again if it
    /// stays so; well within <c>ShipStuck</c>'s 30 minutes. A normal arrival is handled within seconds of its time; under the
    /// 429 back-off (B13) each of its calls can wait up to a minute. An arrival run twice costs a market fetch and a dock,
    /// which the API answers alike for a docked ship.
    /// </summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _scheduledAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Schedules the ship's arrival again when it is stored in transit more than <see cref="Margin"/> past its arrival time,
    /// at most once a margin.
    /// </summary>
    /// <param name="ship">The ship as stored, without dead-reckoning.</param>
    /// <param name="goalId">Its active goal, which the arrival resumes (B17).</param>
    /// <param name="now">The time.</param>
    /// <param name="cancellationToken">Stops the scheduling.</param>
    /// <returns>True when its arrival was scheduled again.</returns>
    public Task<bool> RescheduleIfLostAsync(ShipModel ship, Guid goalId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ship);
        if (ship.LocalStatus != ShipLocalStatus.InTransit || ship.ArrivesAt is not { } arrivesAt || now - arrivesAt < Margin)
        {
            return Task.FromResult(false);
        }

        lock (_gate)
        {
            if (_scheduledAt.TryGetValue(ship.Symbol, out var last) && now - last < Margin)
            {
                return Task.FromResult(false);
            }

            _scheduledAt[ship.Symbol] = now;
        }

        return ScheduleAsync(ship.Symbol, goalId, now, cancellationToken);
    }

    private async Task<bool> ScheduleAsync(string shipSymbol, Guid goalId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await scheduler.ScheduleArrivalAsync(shipSymbol, goalId, now, cancellationToken);
        return true;
    }
}
