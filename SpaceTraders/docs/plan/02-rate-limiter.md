# 02 – Rate Limiter & Resilient HTTP Client

## Goals
- Never exceed 2 req/s sustained or 30 req/60 s burst per the SpaceTraders limits.
- Automatically retry on 429 (using `x-ratelimit-reset` header) and 502 (with exponential back-off).
- Expose real-time rate-limit metrics to the monitoring dashboard.
- Fully transparent to callers – implemented as `DelegatingHandler` chain.

---

## 2.1 Token Bucket Implementation

Use a **dual-bucket** token bucket:

| Bucket | Capacity | Refill rate |
|--------|----------|-------------|
| PerSecond | 2 tokens | 2 / second |
| Burst | 30 tokens | full refill every 60 s |

Both buckets must be satisfied before a request is dispatched. If either is empty the handler **waits** (not drops) so the queue drains automatically.

```csharp
// SpaceTraders.Infrastructure.SpaceTradersAPI/RateLimiting/TokenBucketRateLimiter.cs

public sealed class TokenBucketRateLimiter
{
    // PerSecond bucket: 2 tokens, refills 2/s
    // Burst bucket:    30 tokens, refills 30/60s = 0.5/s
    // Await both before releasing the next request slot
}
```

> **Library choice:** Use `System.Threading.RateLimiting` (built into .NET 7+) with two
> `TokenBucketRateLimiter` instances composed in a `ChainedRateLimiter`.

---

## 2.2 DelegatingHandler Chain

```
HttpClient
  └── RateLimitingHandler          ← acquires token before sending
        └── RateLimitResponseHandler ← on 429: waits until x-ratelimit-reset, then retries
              └── RetryHandler       ← on 502: exponential back-off (1s, 2s, 4s, max 3 retries)
                    └── MetricsHandler ← records request count, latency, errors to in-memory store
```

### `RateLimitingHandler`
```csharp
protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken ct)
{
    using var lease = await _rateLimiter.AcquireAsync(permitCount: 1, ct);
    if (!lease.IsAcquired) throw new RateLimitExceededException();
    return await base.SendAsync(request, ct);
}
```

### `RateLimitResponseHandler`
```csharp
// On 429: parse x-ratelimit-reset (ISO-8601), delay until that time + 50 ms jitter, retry once.
// Update internal RateLimitStatus so the dashboard can show it.
```

### `RetryHandler`
```csharp
// On 502: retry up to 3 times with delays 1s, 2s, 4s.
// On persistent 502: publish DDoSProtectionTriggeredEvent via IMediator.
```

---

## 2.3 Rate Limit Status (in-memory, observable)

```csharp
// SpaceTraders.Infrastructure.SpaceTradersAPI/RateLimiting/RateLimitStatus.cs

public sealed class RateLimitStatus
{
    public int    Remaining      { get; set; }
    public int    Limit          { get; set; }
    public int    BurstRemaining { get; set; }
    public int    BurstLimit     { get; set; }
    public DateTimeOffset ResetAt { get; set; }
    public string? LimitType     { get; set; }
    public int    TotalRequests  { get; set; }
    public int    ThrottledCount { get; set; }
}
```

Registered as **singleton** and injected into both the handler and the dashboard.

---

## 2.4 Request Queue & Prioritisation

High-level strategy:
- All outgoing requests are enqueued in a `PriorityChannel<ApiRequest>`.
- **Priority levels:** `Critical` (contract deadlines, refuel), `Normal` (trade, mine), `Low` (scout, cache refresh).
- A single `ApiDispatcher` hosted service drains the queue at ≤ 2 req/s.

```
PriorityChannel
  Priority.Critical  ─┐
  Priority.Normal    ─┼──► ApiDispatcher (BackgroundService) ──► RateLimitingHandler ──► ST API
  Priority.Low       ─┘
```

This prevents low-priority cache refreshes from starving contract-critical calls.

---

## 2.5 Resilience with Microsoft.Extensions.Http.Resilience

Add `Microsoft.Extensions.Http.Resilience` (already part of .NET ecosystem):

```csharp
services.AddHttpClient<ISpaceTradersApiClient, SpaceTradersApiClient>()
    .AddHttpMessageHandler<RateLimitingHandler>()
    .AddHttpMessageHandler<RateLimitResponseHandler>()
    .AddHttpMessageHandler<RetryHandler>()
    .AddHttpMessageHandler<MetricsHandler>();
```

---

## 2.6 Circuit Breaker

In addition to the per-request retry in `RetryHandler`, a circuit breaker prevents cascading failures
when the SpaceTraders API is broadly unavailable.

Use `Microsoft.Extensions.Http.Resilience`'s `AddResilienceHandler` with a `CircuitBreakerStrategyOptions`:

```csharp
services.AddHttpClient<ISpaceTradersApiClient, SpaceTradersApiClient>()
    .AddResilienceHandler("spacetraders", builder =>
    {
        // Trip after 5 consecutive 502s; stay open for 30 s; then half-open (1 probe)
        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            HandledStatusCodes      = { HttpStatusCode.BadGateway },
            FailureRatio            = 1.0,           // 100 % failures in the sampling window
            MinimumThroughput       = 5,
            SamplingDuration        = TimeSpan.FromSeconds(30),
            BreakDuration           = TimeSpan.FromSeconds(30),
        });
    });
```

When the circuit is **open**:
- `ApiDispatcher` catches `BrokenCircuitException`, publishes `ApiUnavailableEvent`.
- Automation pauses (same as §10.5).
- The breaker's built-in half-open probe re-enables the circuit automatically on success.

---

## 2.7 Priority Queue Overflow

The `PriorityChannel` has a bounded capacity to prevent unbounded memory growth.

| Priority | Capacity | Overflow behaviour |
|----------|----------|--------------------|
| Critical | 50 | Block caller (await) – critical requests must not be dropped |
| Normal   | 200 | Drop oldest entry, log warning |
| Low      | 100 | Drop new entry silently, increment `DroppedLowPriorityCount` in `RateLimitStatus` |

```csharp
// PriorityApiQueue.cs
private readonly Channel<ApiRequest> _critical = Channel.CreateBounded<ApiRequest>(
    new BoundedChannelOptions(50) { FullMode = BoundedChannelFullMode.Wait });

private readonly Channel<ApiRequest> _normal = Channel.CreateBounded<ApiRequest>(
    new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest });

private readonly Channel<ApiRequest> _low = Channel.CreateBounded<ApiRequest>(
    new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite });
```

The `RateLimitStatus.DroppedLowPriorityCount` counter is surfaced on the dashboard so operators
can detect when the queue is consistently saturated and tune `MaxShips` or `Trade.MaxHaulDistance`
accordingly.

---

## 2.8 Folder Structure

```
SpaceTraders.Infrastructure.SpaceTradersAPI/
├── RateLimiting/
│   ├── DualBucketRateLimiter.cs
│   ├── RateLimitStatus.cs
│   ├── RateLimitingHandler.cs
│   ├── RateLimitResponseHandler.cs
│   ├── RetryHandler.cs
│   ├── MetricsHandler.cs
│   └── ApiDispatcher.cs             ← IHostedService, drains PriorityChannel
└── Queue/
    ├── ApiRequest.cs
    ├── RequestPriority.cs           ← enum: Critical, Normal, Low
    └── PriorityApiQueue.cs          ← wraps System.Threading.Channels
```
