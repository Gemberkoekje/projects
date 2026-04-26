namespace SpaceTraders.Application.Interfaces;

public interface IRateLimitStatus
{
    int Remaining { get; }

    int Limit { get; }

    int BurstRemaining { get; }

    int BurstLimit { get; }

    DateTimeOffset ResetAt { get; }

    string? LimitType { get; }

    int TotalRequests { get; }

    int ThrottledCount { get; }
}
