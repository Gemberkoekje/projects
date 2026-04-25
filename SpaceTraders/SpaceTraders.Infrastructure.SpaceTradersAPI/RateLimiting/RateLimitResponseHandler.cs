using System.Net;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

/// <summary>On HTTP 429, waits until the x-ratelimit-reset header time then retries once.</summary>
public sealed class RateLimitResponseHandler(RateLimitStatus status) : DelegatingHandler
{
    private readonly RateLimitStatus _status = status;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _status.ThrottledCount++;
            var resetAt = ParseResetHeader(response);
            var delay = resetAt - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(50);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            response.Dispose();
            response = await base.SendAsync(request, cancellationToken);
        }

        UpdateStatus(response);
        return response;
    }

    private static DateTimeOffset ParseResetHeader(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var values))
        {
            var val = values.FirstOrDefault();
            if (val is not null && DateTimeOffset.TryParse(val, out var reset))
                return reset;
        }
        return DateTimeOffset.UtcNow.AddSeconds(1);
    }

    private void UpdateStatus(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) &&
            int.TryParse(remaining.FirstOrDefault(), out var r))
            _status.Remaining = r;

        if (response.Headers.TryGetValues("x-ratelimit-limit", out var limit) &&
            int.TryParse(limit.FirstOrDefault(), out var l))
            _status.Limit = l;

        if (response.Headers.TryGetValues("x-ratelimit-reset", out var reset) &&
            DateTimeOffset.TryParse(reset.FirstOrDefault(), out var resetAt))
            _status.ResetAt = resetAt;
    }
}
