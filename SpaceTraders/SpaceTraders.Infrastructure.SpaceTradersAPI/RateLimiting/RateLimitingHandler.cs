using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

/// <summary>
/// Keeps outbound requests within the API guide's limit (<see cref="RequestBudget"/>), and lets
/// writes go before reads (D19). A write is anything but GET: moving a ship, docking, trading. A read,
/// such as a market refresh, loses nothing by going a few seconds later:
/// <list type="bullet">
///   <item>a read gives way while any write is waiting for the budget;</item>
///   <item>a read leaves the last <see cref="RequestBudget.WriteReserve"/> burst requests to writes, so
///   a run of reads can't keep a write waiting;</item>
///   <item>a read that has given way for <see cref="MaxReadDelay"/> stops giving way, so a busy fleet
///   can't starve its reads: a ship needs its market's prices before it trades.</item>
/// </list>
/// The time a request waits is counted in <c>spacetraders_api_rate_limit_wait_seconds_total</c>, by
/// <c>kind</c>: <c>read</c> or <c>write</c>.
/// </summary>
public sealed class RateLimitingHandler(RequestBudget budget, RateLimitStatus status, IAutomationMetrics metrics) : DelegatingHandler
{
    /// <summary>The <c>kind</c> of a GET in the wait metric.</summary>
    public const string ReadKind = "read";

    /// <summary>The <c>kind</c> of any other request in the wait metric.</summary>
    public const string WriteKind = "write";

    private static readonly TimeSpan GiveWayPoll = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// How long a read gives way to writes at most, 10 seconds: "a market refresh that can be done
    /// 10 seconds later without penalty" (D19). After that it waits only for the budget itself.
    /// </summary>
    public TimeSpan MaxReadDelay { get; init; } = TimeSpan.FromSeconds(10);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isWrite = !HttpMethod.Get.Equals(request.Method);
        var waited = await WaitForBudgetAsync(isWrite, cancellationToken);
        if (waited > TimeSpan.Zero)
        {
            metrics.RateLimitWait(waited, isWrite ? WriteKind : ReadKind);
        }

        status.TotalRequests++;
        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>Waits until the budget lets the request go; returns how long that took (zero without a wait).</summary>
    private async Task<TimeSpan> WaitForBudgetAsync(bool isWrite, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        var hasWaited = false;
        if (isWrite)
        {
            budget.WriteWaiting();
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var givesWay = !isWrite && TimeProvider.System.GetElapsedTime(started) < MaxReadDelay;
                if (givesWay && budget.WritesWaiting > 0)
                {
                    hasWaited = true;
                    await Task.Delay(GiveWayPoll, cancellationToken);
                    continue;
                }

                var now = TimeProvider.System.GetUtcNow();
                var wait = budget.TryTake(now, givesWay ? RequestBudget.WriteReserve : 0);
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
        finally
        {
            if (isWrite)
            {
                budget.WriteServed();
            }
        }
    }
}
