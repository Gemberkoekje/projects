using System.Globalization;
using System.Net;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Metrics;

/// <summary>
/// The innermost handler: counts every request that goes out to the SpaceTraders API by method,
/// route template (<see cref="ApiEndpointTemplate"/>) and status code, retries included, and every
/// 429 by where it came from.
/// </summary>
public sealed class ApiRequestMetricsHandler(IAutomationMetrics metrics) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var endpoint = ApiEndpointTemplate.FromPath(request.RequestUri?.AbsolutePath ?? string.Empty);

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            metrics.ApiRequest(method, endpoint, "error");
            throw;
        }

        metrics.ApiRequest(method, endpoint, ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            metrics.ApiThrottled(RateLimitResponseHandler.IsFromRateLimiter(response) ? "rate_limiter" : "infrastructure");
        }

        return response;
    }
}
