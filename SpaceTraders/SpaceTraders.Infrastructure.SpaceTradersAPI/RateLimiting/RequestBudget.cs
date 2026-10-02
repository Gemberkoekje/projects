namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

/// <summary>
/// The request limit from the API guide (https://spacetraders.io/api-guide/rate-limits), which
/// applies per IP address and per account: 2 requests per second and, once those are used, a burst
/// of up to 30 more requests per 60 seconds.
/// </summary>
/// <remarks>
/// Both windows slide, so the client never sends more than any fixed window the server counts in
/// allows. Registered as a singleton: the HttpClient factory recreates its handlers every few
/// minutes, and the budget has to outlive them. It also counts the writes waiting for it, which reads
/// give way to (D19, <see cref="RateLimitingHandler"/>).
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

    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BurstDuration = TimeSpan.FromSeconds(60);

    private readonly Queue<DateTimeOffset> _perSecond = new();
    private readonly Queue<DateTimeOffset> _burst = new();
    private readonly Lock _lock = new();
    private int _writesWaiting;

    /// <summary>The writes waiting for the budget now; reads give way to them (D19).</summary>
    public int WritesWaiting => Volatile.Read(ref _writesWaiting);

    /// <summary>Counts a write as waiting for the budget, until <see cref="WriteServed"/>.</summary>
    public void WriteWaiting() => Interlocked.Increment(ref _writesWaiting);

    /// <summary>A write that was waiting has taken its request from the budget, or given up.</summary>
    public void WriteServed() => Interlocked.Decrement(ref _writesWaiting);

    /// <summary>
    /// Takes one request from the budget at <paramref name="now"/>. Returns <see cref="TimeSpan.Zero"/>
    /// when the request may go, or else how long to wait before asking again.
    /// </summary>
    /// <param name="now">The time of the request.</param>
    /// <param name="burstReserve">
    /// The burst requests this request must leave unused: <see cref="WriteReserve"/> for a read that
    /// gives way, 0 for a write. The 2 per second are open to every request.
    /// </param>
    /// <returns>Zero when the request may go; otherwise how long until it may ask again.</returns>
    public TimeSpan TryTake(DateTimeOffset now, int burstReserve = 0)
    {
        lock (_lock)
        {
            Forget(_perSecond, now - OneSecond);
            Forget(_burst, now - BurstDuration);

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
            var untilPerSecond = _perSecond.Peek() + OneSecond - now;
            var untilBurst = burstOpen > 0
                ? _burst.ElementAt(_burst.Count - burstOpen) + BurstDuration - now
                : TimeSpan.MaxValue;
            return untilPerSecond < untilBurst ? untilPerSecond : untilBurst;
        }
    }

    /// <summary>The burst requests still available at <paramref name="now"/>.</summary>
    public int BurstRemaining(DateTimeOffset now)
    {
        lock (_lock)
        {
            Forget(_burst, now - BurstDuration);
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
