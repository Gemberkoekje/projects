using System.Net;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

/// <summary>On HTTP 502, retries up to 3 times with exponential back-off (1s, 2s, 4s).
/// Also notifies <see cref="IApiAvailabilityState"/> when the API is unavailable after
/// exhausting retries, and marks it available again on a successful response.</summary>
public sealed class RetryHandler(IApiAvailabilityState availabilityState) : DelegatingHandler
{
    private static readonly TimeSpan[] Delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        for (var attempt = 0; attempt < Delays.Length && response.StatusCode == HttpStatusCode.BadGateway; attempt++)
        {
            response.Dispose();
            await Task.Delay(Delays[attempt], cancellationToken);
            response = await base.SendAsync(request, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.BadGateway)
        {
            availabilityState.MarkUnavailable();
        }
        else
        {
            availabilityState.MarkAvailable();
        }

        return response;
    }
}
