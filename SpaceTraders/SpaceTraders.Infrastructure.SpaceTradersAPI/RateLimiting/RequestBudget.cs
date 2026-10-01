namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

/// <summary>
/// The request limit from the API guide (https://spacetraders.io/api-guide/rate-limits), which
/// applies per IP address and per account: 2 requests per second and, once those are used, a burst
/// of up to 30 more requests per 60 seconds.
/// </summary>
/// <remarks>
/// Both windows slide, so the client never sends more than any fixed window the server counts in
/// allows. Registered as a singleton: the HttpClient factory recreates its handlers every few
/// minutes, and the budget has to outlive them.
/// </remarks>
public sealed class RequestBudget
{
    public const int PerSecond = 2;

    public const int Burst = 30;

    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BurstDuration = TimeSpan.FromSeconds(60);

    private readonly Queue<DateTimeOffset> _perSecond = new();
    private readonly Queue<DateTimeOffset> _burst = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// Takes one request from the budget at <paramref name="now"/>. Returns <see cref="TimeSpan.Zero"/>
    /// when the request may go, or else how long to wait before asking again.
    /// </summary>
    public TimeSpan TryTake(DateTimeOffset now)
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

            if (_burst.Count < Burst)
            {
                _burst.Enqueue(now);
                return TimeSpan.Zero;
            }

            var untilPerSecond = _perSecond.Peek() + OneSecond - now;
            var untilBurst = _burst.Peek() + BurstDuration - now;
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
