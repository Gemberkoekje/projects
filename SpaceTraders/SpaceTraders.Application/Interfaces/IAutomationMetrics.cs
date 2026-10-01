namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Records automation metrics. The API host exports them to Prometheus.
/// </summary>
public interface IAutomationMetrics
{
    /// <summary>Counts a goal the circuit breaker blocked (<c>spacetraders_goal_breaker_trips_total</c>).</summary>
    void GoalBreakerTripped(string shipSymbol);
}
