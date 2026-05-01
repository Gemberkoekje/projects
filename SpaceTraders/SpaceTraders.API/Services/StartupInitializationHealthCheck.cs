using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SpaceTraders.API.Services;

/// <summary>
/// Reports whether deferred startup initialization has completed.
/// </summary>
public sealed class StartupInitializationHealthCheck(StartupInitializationState state) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (state.IsCompleted)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Deferred startup initialization completed."));
        }

        if (state.HasFailed)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Deferred startup initialization failed.",
                state.Failure));
        }

        return Task.FromResult(HealthCheckResult.Degraded("Deferred startup initialization is still running."));
    }
}
