using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

/// <summary>
/// Retries a 429 the way the API guide asks. A 429 from the API's rate limiter carries
/// <c>x-ratelimit-*</c> headers: wait until <c>x-ratelimit-reset</c> (or <c>retry-after</c>), then
/// retry. A 429 without them comes from the cloud infrastructure: back off exponentially. Either
/// way it gives up after <see cref="MaxRetries"/> retries and returns the 429. Each 429 is logged with the limiter's
/// headers (<see cref="RateLimitHeaders"/>, B59): what the server counted, to hold the local budget against. A 429 from
/// the rate limiter holds every request back until its reset, not only the one it refused (<see cref="RequestBudget.PauseUntil"/>,
/// B59): the requests after it went out at once and drew 429s of their own.
/// </summary>
public sealed class RateLimitResponseHandler : DelegatingHandler
{
    public const int MaxRetries = 5;

    /// <summary>The waits before each retry of a 429 without rate-limit headers.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultBackoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
    ];

    private static readonly TimeSpan ResetMargin = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaxRateLimiterWait = TimeSpan.FromSeconds(60);

    private readonly RateLimitStatus _status;
    private readonly RequestBudget _budget;
    private readonly ILogger<RateLimitResponseHandler> _logger;
    private readonly IReadOnlyList<TimeSpan> _backoff;

    public RateLimitResponseHandler(RateLimitStatus status, RequestBudget budget, ILogger<RateLimitResponseHandler> logger)
        : this(status, budget, logger, DefaultBackoff)
    {
    }

    public RateLimitResponseHandler(RateLimitStatus status, RequestBudget budget, ILogger<RateLimitResponseHandler> logger, IReadOnlyList<TimeSpan> backoff)
    {
        _status = status;
        _budget = budget;
        _logger = logger;
        _backoff = backoff;
    }

    /// <summary>True when a 429 came from the API's rate limiter rather than the cloud infrastructure.</summary>
    public static bool IsFromRateLimiter(HttpResponseMessage response) => response.Headers.Contains("x-ratelimit-type");

    /// <summary>
    /// The rate limiter's headers on a response, as <c>name=value</c> pairs by name (B59): the <c>x-ratelimit-*</c> headers
    /// and <c>retry-after</c>, whatever the server sends. "none" without any.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The headers, for a log line.</returns>
    public static string RateLimitHeaders(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var headers = response.Headers
            .Where(header => header.Key.StartsWith("x-ratelimit-", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("retry-after", StringComparison.OrdinalIgnoreCase))
            .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .Select(header => $"{header.Key}={string.Join(',', header.Value)}")
            .ToList();
        return headers.Count == 0 ? "none" : string.Join(", ", headers);
    }

    /// <summary>
    /// How long the rate limiter asks to wait: until <c>x-ratelimit-reset</c>, else <c>retry-after</c>,
    /// else one second. Never more than a minute, the longest window the API counts in.
    /// </summary>
    public static TimeSpan RateLimiterWait(HttpResponseMessage response, DateTimeOffset now)
    {
        var wait = TimeSpan.FromSeconds(1);
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var resets)
            && DateTimeOffset.TryParse(resets.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var reset))
        {
            wait = reset - now + ResetMargin;
        }
        else if (response.Headers.RetryAfter is { } retryAfter)
        {
            wait = retryAfter.Delta ?? (retryAfter.Date - now) ?? wait;
        }

        if (wait < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return wait > MaxRateLimiterWait ? MaxRateLimiterWait : wait;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        var retry = 0;
        while (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _status.ThrottledCount++;
            UpdateStatus(response);

            var fromRateLimiter = IsFromRateLimiter(response);
            if (retry == MaxRetries)
            {
                _logger.LogWarning(
                    "429 from {Source} for {Endpoint}; giving up after {Retries} retries. The limiter's headers: {RateLimitHeaders:l}.",
                    fromRateLimiter ? "the API's rate limiter" : "the cloud infrastructure",
                    request.RequestUri?.AbsolutePath,
                    MaxRetries,
                    RateLimitHeaders(response));
                return response;
            }

            var now = TimeProvider.System.GetUtcNow();
            var wait = fromRateLimiter
                ? RateLimiterWait(response, now)
                : _backoff[Math.Min(retry, _backoff.Count - 1)];
            if (fromRateLimiter)
            {
                _budget.PauseUntil(now + wait);
            }

            _logger.LogWarning(
                "429 from {Source} for {Endpoint}; retrying in {Wait} ({Retry} of {MaxRetries}). The limiter's headers: {RateLimitHeaders:l}.",
                fromRateLimiter ? "the API's rate limiter" : "the cloud infrastructure",
                request.RequestUri?.AbsolutePath,
                wait,
                retry + 1,
                MaxRetries,
                RateLimitHeaders(response));

            response.Dispose();
            await Task.Delay(wait, cancellationToken);
            response = await base.SendAsync(request, cancellationToken);
            retry++;
        }

        UpdateStatus(response);
        return response;
    }

    private void UpdateStatus(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-ratelimit-type", out var type))
            _status.LimitType = type.FirstOrDefault();

        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) &&
            int.TryParse(remaining.FirstOrDefault(), CultureInfo.InvariantCulture, out var r))
            _status.Remaining = r;

        if (response.Headers.TryGetValues("x-ratelimit-limit-per-second", out var limit) &&
            int.TryParse(limit.FirstOrDefault(), CultureInfo.InvariantCulture, out var l))
            _status.Limit = l;

        if (response.Headers.TryGetValues("x-ratelimit-reset", out var reset) &&
            DateTimeOffset.TryParse(reset.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var resetAt))
            _status.ResetAt = resetAt;
    }
}
