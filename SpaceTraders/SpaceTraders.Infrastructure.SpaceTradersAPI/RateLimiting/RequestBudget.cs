namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

/// <summary>
/// The request limit from the API guide (https://spacetraders.io/api-guide/rate-limits), which
/// applies per IP address and per account: 2 requests per second and, once those are used, a burst
/// of up to 30 more requests per 60 seconds.
/// </summary>
/// <remarks>
/// Both windows slide, so the client never sends more than any fixed window the server counts in
/// allows. Three things keep the server's count and this one together (B59): each window is longer by
/// <see cref="JourneyMargin"/>, for a request's journey to the server; a 429 from the rate limiter holds
/// every request back until the reset it names (<see cref="PauseUntil"/>); and a new process starts with
/// its burst spent (<see cref="ForANewProcess"/>), as the server still counts what the process before it
/// sent. Registered as a singleton: the HttpClient factory recreates its handlers every few minutes,
/// and the budget has to outlive them. It also counts the writes waiting for it, which reads give way
/// to (D19, <see cref="RateLimitingHandler"/>), and the trade trips' requests among them, which the other writes give way to
/// (D115).
/// </remarks>
public sealed class RequestBudget
{
    public const int PerSecond = 2;
    public const int Burst = 30;

    /// <summary>
    /// The burst requests a read leaves to writes (D19): however many reads went just before, a write
    /// finds this many requests left and goes at once.
    /// </summary>
    public const int WriteReserve = 10;

    /// <summary>
    /// The burst requests a write that gives way leaves to the trade trips' requests (PLAN.md slice 6.33, D115): however many
    /// other writes went just before, a trade trip's request finds this many requests left and goes at once. Within the
    /// <see cref="WriteReserve"/>, so a read leaves them too.
    /// </summary>
    public const int TradeReserve = 5;

    /// <summary>
    /// Added to each window for a request's journey to the server (B59). A request that leaves a second
    /// after the one two before it reaches the server less than a second after it when that one took
    /// longer on its way, and the server counts three in its second. Half the 429s of 2026-10-03 and
    /// 2026-10-04 came that way, a few to some tens of milliseconds early.
    /// </summary>
    public static readonly TimeSpan JourneyMargin = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan SecondWindow = TimeSpan.FromSeconds(1) + JourneyMargin;
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(60) + JourneyMargin;

    private readonly Queue<DateTimeOffset> _perSecond = new();
    private readonly Queue<DateTimeOffset> _burst = new();
    private readonly Lock _lock = new();
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private int _writesWaiting;
    private int _tradeRequestsWaiting;

    /// <summary>The writes waiting for the budget now, a trade trip's requests among them; reads give way to them (D19).</summary>
    public int WritesWaiting => Volatile.Read(ref _writesWaiting);

    /// <summary>The trade trips' requests waiting for the budget now; the other writes give way to them (D115).</summary>
    public int TradeRequestsWaiting => Volatile.Read(ref _tradeRequestsWaiting);

    /// <summary>
    /// A budget for a process that has just started, with its burst spent at <paramref name="now"/>: the
    /// server counts the requests of the last minute whichever process sent them, and a process that
    /// stopped a moment ago may have used the burst (B59: the 429s at 10:07:50Z on 2026-10-04 came
    /// during a start). It goes at 2 a second until the burst comes back, a minute later.
    /// </summary>
    /// <param name="now">The time the process starts.</param>
    /// <returns>The budget.</returns>
    public static RequestBudget ForANewProcess(DateTimeOffset now)
    {
        var budget = new RequestBudget();
        for (var request = 0; request < Burst; request++)
        {
            budget._burst.Enqueue(now);
        }

        return budget;
    }

    /// <summary>Counts a write as waiting for the budget, until <see cref="WriteServed"/>.</summary>
    public void WriteWaiting() => Interlocked.Increment(ref _writesWaiting);

    /// <summary>A write that was waiting has taken its request from the budget, or given up.</summary>
    public void WriteServed() => Interlocked.Decrement(ref _writesWaiting);

    /// <summary>Counts a trade trip's request as waiting for the budget, until <see cref="TradeRequestServed"/> (D115).</summary>
    public void TradeRequestWaiting() => Interlocked.Increment(ref _tradeRequestsWaiting);

    /// <summary>A trade trip's request that was waiting has taken its request from the budget, or given up.</summary>
    public void TradeRequestServed() => Interlocked.Decrement(ref _tradeRequestsWaiting);

    /// <summary>
    /// Holds every request back until <paramref name="until"/> (B59): the rate limiter answered 429 and
    /// named when it lets the next request through. Until then the server's budget is empty for every
    /// request, not only the one it refused. A later pause than the one held replaces it; an earlier one
    /// changes nothing.
    /// </summary>
    /// <param name="until">When requests may go again.</param>
    public void PauseUntil(DateTimeOffset until)
    {
        lock (_lock)
        {
            if (until > _pausedUntil)
            {
                _pausedUntil = until;
            }
        }
    }

    /// <summary>
    /// Takes one request from the budget at <paramref name="now"/>. Returns <see cref="TimeSpan.Zero"/>
    /// when the request may go, or else how long to wait before asking again.
    /// </summary>
    /// <param name="now">The time of the request.</param>
    /// <param name="burstReserve">
    /// The burst requests this request must leave unused: <see cref="WriteReserve"/> for a read that
    /// gives way, <see cref="TradeReserve"/> for another write that gives way to the trade trips' requests (D115), 0 for a
    /// trade trip's request, or a request that gives way no longer. The 2 per second are open to every request.
    /// </param>
    /// <returns>Zero when the request may go; otherwise how long until it may ask again.</returns>
    public TimeSpan TryTake(DateTimeOffset now, int burstReserve = 0)
    {
        lock (_lock)
        {
            if (now < _pausedUntil)
            {
                return _pausedUntil - now;
            }

            Forget(_perSecond, now - SecondWindow);
            Forget(_burst, now - BurstWindow);

            if (_perSecond.Count < PerSecond)
            {
                _perSecond.Enqueue(now);
                return TimeSpan.Zero;
            }

            var burstOpen = Burst - Math.Clamp(burstReserve, 0, Burst);
            if (_burst.Count < burstOpen)
            {
                _burst.Enqueue(now);
                return TimeSpan.Zero;
            }

            // The burst opens for this request once enough of its oldest requests have left the window.
            var untilPerSecond = _perSecond.Peek() + SecondWindow - now;
            var untilBurst = burstOpen > 0
                ? _burst.ElementAt(_burst.Count - burstOpen) + BurstWindow - now
                : TimeSpan.MaxValue;
            return untilPerSecond < untilBurst ? untilPerSecond : untilBurst;
        }
    }

    /// <summary>The burst requests still available at <paramref name="now"/>.</summary>
    public int BurstRemaining(DateTimeOffset now)
    {
        lock (_lock)
        {
            Forget(_burst, now - BurstWindow);
            return Burst - _burst.Count;
        }
    }

    private static void Forget(Queue<DateTimeOffset> requests, DateTimeOffset windowStart)
    {
        while (requests.Count > 0 && requests.Peek() <= windowStart)
        {
            requests.Dequeue();
        }
    }
}
