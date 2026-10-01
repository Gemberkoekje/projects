using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

/// <summary>
/// Keeps outbound requests within the API guide's limit (<see cref="RequestBudget"/>). Requests
/// that change something (anything but GET) go before reads. The time a request waits is counted
/// in <c>spacetraders_api_rate_limit_wait_seconds_total</c>.
/// </summary>
public sealed class RateLimitingHandler(RequestBudget budget, RateLimitStatus status, IAutomationMetrics metrics) : DelegatingHandler
{
    private static readonly TimeSpan PriorityWait = TimeSpan.FromMilliseconds(25);
    private static int s_pendingPriorityRequests;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isPriorityRequest = !HttpMethod.Get.Equals(request.Method);

        if (isPriorityRequest)
        {
            Interlocked.Increment(ref s_pendingPriorityRequests);
        }

        try
        {
            var waited = await WaitForBudgetAsync(isPriorityRequest, cancellationToken);
            if (waited > TimeSpan.Zero)
            {
                metrics.RateLimitWait(waited);
            }

            status.TotalRequests++;
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            if (isPriorityRequest)
            {
                Interlocked.Decrement(ref s_pendingPriorityRequests);
            }
        }
    }

    /// <summary>Waits until the budget lets the request go; returns how long that took (zero without a wait).</summary>
    private async Task<TimeSpan> WaitForBudgetAsync(bool isPriorityRequest, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        var hasWaited = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!isPriorityRequest && Volatile.Read(ref s_pendingPriorityRequests) > 0)
            {
                hasWaited = true;
                await Task.Delay(PriorityWait, cancellationToken);
                continue;
            }

            var now = TimeProvider.System.GetUtcNow();
            var wait = budget.TryTake(now);
            if (wait == TimeSpan.Zero)
            {
                status.BurstLimit = RequestBudget.Burst;
                status.BurstRemaining = budget.BurstRemaining(now);
                return hasWaited ? TimeProvider.System.GetElapsedTime(started) : TimeSpan.Zero;
            }

            hasWaited = true;
            await Task.Delay(wait, cancellationToken);
        }
    }
}
