using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Metrics;

/// <summary>
/// The outermost handler (slice 2.10): counts every request the bot initiates to the SpaceTraders API by method and
/// route template (<see cref="ApiEndpointTemplate"/>), once, as it starts. That is before the pause after a 502 and the
/// local request budget, so a request still waiting for the budget, or one the pause refuses, counts here before
/// <see cref="ApiRequestMetricsHandler"/> counts it going out, or without it ever going out; a retry of a 429 goes out
/// again, and counts there again, but not here.
/// </summary>
public sealed class ApiRequestInitiatedHandler(IAutomationMetrics metrics) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        metrics.ApiRequestInitiated(request.Method.Method, ApiEndpointTemplate.FromPath(request.RequestUri?.AbsolutePath ?? string.Empty));
        return base.SendAsync(request, cancellationToken);
    }
}
