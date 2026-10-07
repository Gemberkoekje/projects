using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;

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
/// A trade trip's requests go before both (PLAN.md slice 6.33, D115), asked on 2026-10-07: "I'd like trade ships to be
/// prioritized in rate limiting. So if a trade ship docks/undocks/jumps/navigates/buys/sells it should not have to wait for a
/// miner or a surveyor." A request made for a trade trip (<see cref="ApiPriority.IsTradeTrip"/>), a write or the refresh of the
/// market it trades at, never gives way:
/// <list type="bullet">
///   <item>any other write gives way while a trade trip's request is waiting, and leaves the last
///   <see cref="RequestBudget.TradeReserve"/> burst requests to them;</item>
///   <item>a write that has given way for <see cref="MaxWriteDelay"/> stops giving way, so a busy trading fleet can't starve the
///   miners;</item>
///   <item>a read gives way to a trade trip's request as to any write.</item>
/// </list>
/// The time a request waits is counted in <c>spacetraders_api_rate_limit_wait_seconds_total</c>, by
/// <c>kind</c>: <c>read</c>, <c>write</c> or <c>trade</c>.
/// </summary>
public sealed class RateLimitingHandler(RequestBudget budget, RateLimitStatus status, IAutomationMetrics metrics) : DelegatingHandler
{
    /// <summary>The <c>kind</c> of a GET in the wait metric.</summary>
    public const string ReadKind = "read";

    /// <summary>The <c>kind</c> of any other request in the wait metric.</summary>
    public const string WriteKind = "write";

    /// <summary>The <c>kind</c> of a trade trip's request in the wait metric, a GET or not (D115).</summary>
    public const string TradeKind = "trade";

    private static readonly TimeSpan GiveWayPoll = TimeSpan.FromMilliseconds(25);

    private enum Kind
    {
        Read,
        Write,
        Trade,
    }

    /// <summary>
    /// How long a read gives way to writes at most, 10 seconds: "a market refresh that can be done
    /// 10 seconds later without penalty" (D19). After that it waits only for the budget itself.
    /// </summary>
    public TimeSpan MaxReadDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a write gives way to the trade trips' requests at most, 10 seconds, as a read gives way to writes (D115). After
    /// that it waits only for the budget itself.
    /// </summary>
    public TimeSpan MaxWriteDelay { get; init; } = TimeSpan.FromSeconds(10);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var kind = ApiPriority.IsTradeTrip ? Kind.Trade : HttpMethod.Get.Equals(request.Method) ? Kind.Read : Kind.Write;
        var waited = await WaitForBudgetAsync(kind, cancellationToken);
        if (waited > TimeSpan.Zero)
        {
            metrics.RateLimitWait(waited, kind switch { Kind.Trade => TradeKind, Kind.Read => ReadKind, _ => WriteKind });
        }

        status.TotalRequests++;
        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>Waits until the budget lets the request go; returns how long that took (zero without a wait).</summary>
    private async Task<TimeSpan> WaitForBudgetAsync(Kind kind, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        var hasWaited = false;

        // Reads give way to every write, a trade trip's too; the other writes to the trade trips' requests alone.
        if (kind != Kind.Read)
        {
            budget.WriteWaiting();
        }

        if (kind == Kind.Trade)
        {
            budget.TradeRequestWaiting();
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var elapsed = TimeProvider.System.GetElapsedTime(started);
                var (givesWay, ahead, burstReserve) = kind switch
                {
                    Kind.Read when elapsed < MaxReadDelay => (true, budget.WritesWaiting, RequestBudget.WriteReserve),
                    Kind.Write when elapsed < MaxWriteDelay => (true, budget.TradeRequestsWaiting, RequestBudget.TradeReserve),
                    _ => (false, 0, 0),
                };
                if (givesWay && ahead > 0)
                {
                    hasWaited = true;
                    await Task.Delay(GiveWayPoll, cancellationToken);
                    continue;
                }

                var now = TimeProvider.System.GetUtcNow();
                var wait = budget.TryTake(now, burstReserve);
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
            if (kind != Kind.Read)
            {
                budget.WriteServed();
            }

            if (kind == Kind.Trade)
            {
                budget.TradeRequestServed();
            }
        }
    }
}
