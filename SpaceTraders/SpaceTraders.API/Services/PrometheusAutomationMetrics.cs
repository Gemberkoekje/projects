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

    private static readonly Gauge DatabaseSizeBytes = Metrics.CreateGauge(
        "spacetraders_db_size_bytes",
        "Size of the bot's Postgres database (pg_database_size), read every 5 minutes.");

    /// <inheritdoc />
    public void GoalBreakerTripped(string shipSymbol) => GoalBreakerTrips.WithLabels(shipSymbol).Inc();

    /// <inheritdoc />
    public void DatabaseSize(long bytes) => DatabaseSizeBytes.Set(bytes);
}
