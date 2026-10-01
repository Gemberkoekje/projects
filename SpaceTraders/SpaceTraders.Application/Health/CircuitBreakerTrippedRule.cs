using System.Globalization;
using SpaceTraders.Application.Goals;

namespace SpaceTraders.Application.Health;

/// <summary>
/// The circuit breaker hasn't tripped (PLAN.md 3.2, slice 1.2): no ship's goal is blocked as a
/// runaway, and no ship tripped the breaker in the last hour. The subject is the ship.
/// </summary>
/// <remarks>
/// A blocked goal stays blocked until a plan replaces it; the scout plan never does, so a tripped
/// scout ship stays an anomaly until someone looks at it. Mining and trading replace a blocked goal at
/// once, so the trip itself counts for <see cref="RecentTrip"/> too (in memory, since the process
/// started).
/// </remarks>
public sealed class CircuitBreakerTrippedRule(HealthFleet fleet, IGoalStepCircuitBreaker circuitBreaker) : IHealthRule
{
    /// <summary>How long a trip counts after a plan has replaced the blocked goal.</summary>
    internal static readonly TimeSpan RecentTrip = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public string Name => "CircuitBreakerTripped";

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var ships = await fleet.LoadAsync(cancellationToken);
        var trips = circuitBreaker.LastTrips;
        var violations = new List<HealthViolation>();
        foreach (var ship in ships)
        {
            if (ship.IsBlocked)
            {
                violations.Add(new HealthViolation(
                    ship.Symbol,
                    $"its {ship.Goal?.Kind} goal is blocked ({ship.Goal?.StatusReason}): it took more goal steps in a minute than Automation.CircuitBreaker.MaxGoalStepsPerMinute allows, and it stays blocked until a plan replaces it"));
            }
            else if (trips.TryGetValue(ship.Symbol, out var trippedAt) && context.Elapsed(trippedAt) <= RecentTrip)
            {
                violations.Add(new HealthViolation(
                    ship.Symbol,
                    string.Create(CultureInfo.InvariantCulture,
                        $"the circuit breaker blocked its goal {context.Elapsed(trippedAt).TotalMinutes:0} minutes ago ({trippedAt:u}) for taking more goal steps in a minute than Automation.CircuitBreaker.MaxGoalStepsPerMinute allows; its goal isn't blocked any more")));
            }
        }

        return violations;
    }
}
