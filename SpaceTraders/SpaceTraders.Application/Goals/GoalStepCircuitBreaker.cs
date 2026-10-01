using System.Collections.Concurrent;

namespace SpaceTraders.Application.Goals;

/// <summary>
/// Counts goal steps per ship over the last minute, so that a goal that loops is stopped
/// before it can flood the database or the API.
/// </summary>
public interface IGoalStepCircuitBreaker
{
    /// <summary>
    /// Records one goal step for <paramref name="shipSymbol"/> at <paramref name="now"/>. Returns
    /// <c>true</c> when the ship has now taken more than <paramref name="maxStepsPerMinute"/> steps
    /// within the last minute; its count then starts again from zero.
    /// </summary>
    bool RecordStep(string shipSymbol, int maxStepsPerMinute, DateTimeOffset now);

    /// <summary>
    /// When each ship last tripped the breaker, since the process started. A plan may replace the
    /// blocked goal at once (mining and trading do), so the trip outlives the blocked goal here.
    /// </summary>
    IReadOnlyDictionary<string, DateTimeOffset> LastTrips { get; }
}

/// <summary>Sliding one-minute window per ship. Registered as a singleton, so it spans all scopes.</summary>
public sealed class GoalStepCircuitBreaker : IGoalStepCircuitBreaker
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _stepsByShip = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastTrips = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, DateTimeOffset> LastTrips => _lastTrips;

    public bool RecordStep(string shipSymbol, int maxStepsPerMinute, DateTimeOffset now)
    {
        var steps = _stepsByShip.GetOrAdd(shipSymbol, _ => new Queue<DateTimeOffset>());

        lock (steps)
        {
            while (steps.Count > 0 && now - steps.Peek() >= Window)
            {
                steps.Dequeue();
            }

            steps.Enqueue(now);
            if (steps.Count <= maxStepsPerMinute)
            {
                return false;
            }

            steps.Clear();
            _lastTrips[shipSymbol] = now;
            return true;
        }
    }
}
