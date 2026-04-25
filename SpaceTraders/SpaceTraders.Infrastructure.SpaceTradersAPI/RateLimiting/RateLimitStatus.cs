using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

public sealed class RateLimitStatus : IRateLimitStatus
{
    public int Remaining { get; set; }
    public int Limit { get; set; }
    public int BurstRemaining { get; set; }
    public int BurstLimit { get; set; }
    public DateTimeOffset ResetAt { get; set; }
    public string? LimitType { get; set; }
    public int TotalRequests { get; set; }
    public int ThrottledCount { get; set; }
}
