namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

/// <summary>
/// Keeps outbound requests within the API guide's limit (<see cref="RequestBudget"/>). Requests
/// that change something (anything but GET) go before reads.
/// </summary>
public sealed class RateLimitingHandler(RequestBudget budget, RateLimitStatus status) : DelegatingHandler
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
            await WaitForBudgetAsync(isPriorityRequest, cancellationToken);
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

    private async Task WaitForBudgetAsync(bool isPriorityRequest, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!isPriorityRequest && Volatile.Read(ref s_pendingPriorityRequests) > 0)
            {
                await Task.Delay(PriorityWait, cancellationToken);
                continue;
            }

            var now = TimeProvider.System.GetUtcNow();
            var wait = budget.TryTake(now);
            if (wait == TimeSpan.Zero)
            {
                status.BurstLimit = RequestBudget.Burst;
                status.BurstRemaining = budget.BurstRemaining(now);
                return;
            }

            await Task.Delay(wait, cancellationToken);
        }
    }
}
