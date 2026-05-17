using System.Net;
using System.Threading;
using System.Threading.RateLimiting;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

public sealed class RateLimitingHandler : DelegatingHandler
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);
    private static int s_pendingPriorityRequests;

    // PerSecond: 2 tokens, refills 2/s
    private readonly RateLimiter _perSecondLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 2,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        TokensPerPeriod = 2,
        AutoReplenishment = true,
    });

    // Burst: 30 tokens over 60 s (0.5/s)
    private readonly RateLimiter _burstLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 30,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0,
        ReplenishmentPeriod = TimeSpan.FromSeconds(60),
        TokensPerPeriod = 30,
        AutoReplenishment = true,
    });

    private readonly RateLimitStatus _status;

    public RateLimitingHandler(RateLimitStatus status)
    {
        _status = status;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isPriorityRequest = !HttpMethod.Get.Equals(request.Method);

        if (isPriorityRequest)
        {
            Interlocked.Increment(ref s_pendingPriorityRequests);
        }

        try
        {
            var (perSecondLease, burstLease) = await AcquireRateLimitAsync(isPriorityRequest, cancellationToken);
            using (perSecondLease)
            using (burstLease)
            {
                _status.TotalRequests++;
                return await base.SendAsync(request, cancellationToken);
            }
        }
        finally
        {
            if (isPriorityRequest)
            {
                Interlocked.Decrement(ref s_pendingPriorityRequests);
            }
        }
    }

    private async Task<(RateLimitLease PerSecondLease, RateLimitLease BurstLease)> AcquireRateLimitAsync(bool isPriorityRequest, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!isPriorityRequest && Volatile.Read(ref s_pendingPriorityRequests) > 0)
            {
                await Task.Delay(RetryDelay, cancellationToken);
                continue;
            }

            var perSecondLease = _perSecondLimiter.AttemptAcquire(permitCount: 1);
            if (!perSecondLease.IsAcquired)
            {
                perSecondLease.Dispose();
                _status.ThrottledCount++;
                await Task.Delay(RetryDelay, cancellationToken);
                continue;
            }

            var burstLease = _burstLimiter.AttemptAcquire(permitCount: 1);
            if (!burstLease.IsAcquired)
            {
                burstLease.Dispose();
                perSecondLease.Dispose();
                _status.ThrottledCount++;
                await Task.Delay(RetryDelay, cancellationToken);
                continue;
            }

            return (perSecondLease, burstLease);
        }
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
