namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class ActivityLog
{
    public long Id { get; init; }

    public string AgentToken { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }

    required public string ShipSymbol { get; init; }

    required public string EventType { get; init; }

    required public string Message { get; init; }

    public string? JsonDetails { get; init; }
}
