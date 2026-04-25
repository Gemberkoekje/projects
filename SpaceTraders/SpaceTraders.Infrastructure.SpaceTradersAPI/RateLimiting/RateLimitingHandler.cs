using System.Net;
using System.Threading.RateLimiting;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

public sealed class RateLimitingHandler : DelegatingHandler
{
    // PerSecond: 2 tokens, refills 2/s
    private readonly RateLimiter _perSecondLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 2,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = int.MaxValue,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        TokensPerPeriod = 2,
        AutoReplenishment = true
    });

    // Burst: 30 tokens over 60 s (0.5/s)
    private readonly RateLimiter _burstLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 30,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = int.MaxValue,
        ReplenishmentPeriod = TimeSpan.FromSeconds(60),
        TokensPerPeriod = 30,
        AutoReplenishment = true
    });

    private readonly RateLimitStatus _status;

    public RateLimitingHandler(RateLimitStatus status)
    {
        _status = status;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var perSecondLease = await _perSecondLimiter.AcquireAsync(permitCount: 1, cancellationToken);
        using var burstLease = await _burstLimiter.AcquireAsync(permitCount: 1, cancellationToken);

        if (!perSecondLease.IsAcquired || !burstLease.IsAcquired)
        {
            _status.ThrottledCount++;
            throw new InvalidOperationException("Rate limit token could not be acquired.");
        }

        _status.TotalRequests++;
        return await base.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _perSecondLimiter.Dispose();
            _burstLimiter.Dispose();
        }
        base.Dispose(disposing);
    }
}
