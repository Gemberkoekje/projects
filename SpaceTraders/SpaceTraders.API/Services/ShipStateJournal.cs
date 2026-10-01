using SpaceTraders.Application;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.API.Services;

/// <summary>
/// Writes the journal's <c>ShipIdle</c> lines from the ship states the metrics sampler reads every
/// 10 seconds: a ship is idle when it has no goal and no assignment. It says so once per spell:
/// for every idle ship at the first sample after a start (<c>idle_at_start</c>), for a ship that
/// appears idle later (<c>new_ship</c>), and when a ship's goal or assignment ends
/// (<c>goal_ended</c>, with the goal as <c>PreviousGoal</c>). <c>ShipBlocked</c> comes from the
/// circuit breaker itself, the moment it blocks a goal.
/// </summary>
/// <remarks>Thread-safe; it keeps each ship's last goal in memory.</remarks>
public sealed class ShipStateJournal(ILogger<ShipStateJournal> logger)
{
    private const string NoGoal = "None";

    private readonly Dictionary<string, string> _goals = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private bool _observedBefore;

    /// <summary>Compares <paramref name="ships"/> with the previous sample and logs the ships that turned idle.</summary>
    public void Observe(IReadOnlyCollection<ShipMetricsSample> ships)
    {
        lock (_lock)
        {
            foreach (var ship in ships)
            {
                var known = _goals.TryGetValue(ship.Ship, out var previousGoal);
                _goals[ship.Ship] = ship.Goal;
                if (ship.Goal != NoGoal)
                {
                    continue;
                }

                if (!known)
                {
                    logger.LogInformation(
                        "{EventKind}: ship {ShipSymbol} is idle ({Reason}): no goal and no assignment.",
                        JournalEvents.ShipIdle,
                        ship.Ship,
                        _observedBefore ? "new_ship" : "idle_at_start");
                }
                else if (previousGoal != NoGoal)
                {
                    logger.LogInformation(
                        "{EventKind}: ship {ShipSymbol} is idle ({Reason}): its {PreviousGoal} ended.",
                        JournalEvents.ShipIdle,
                        ship.Ship,
                        "goal_ended",
                        previousGoal);
                }
            }

            var current = ships.Select(s => s.Ship).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in _goals.Keys.Where(symbol => !current.Contains(symbol)).ToList())
            {
                _goals.Remove(gone);
            }

            _observedBefore = true;
        }
    }
}
