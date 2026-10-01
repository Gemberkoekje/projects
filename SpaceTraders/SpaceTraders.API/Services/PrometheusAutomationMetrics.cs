using Prometheus;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.API.Services;

/// <summary>Exports <see cref="IAutomationMetrics"/> to Prometheus.</summary>
public sealed class PrometheusAutomationMetrics : IAutomationMetrics
{
    private static readonly Counter GoalBreakerTrips = Metrics.CreateCounter(
        "spacetraders_goal_breaker_trips_total",
        "Goals blocked by the per-ship circuit breaker for taking too many steps in a minute.",
        "ship");

    /// <inheritdoc />
    public void GoalBreakerTripped(string shipSymbol) => GoalBreakerTrips.WithLabels(shipSymbol).Inc();
}
