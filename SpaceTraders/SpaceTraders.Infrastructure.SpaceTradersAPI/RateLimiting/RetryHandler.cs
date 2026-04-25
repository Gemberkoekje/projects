using System.Net;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

/// <summary>On HTTP 502, retries up to 3 times with exponential back-off (1s, 2s, 4s).</summary>
public sealed class RetryHandler : DelegatingHandler
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

        return response;
    }
}
