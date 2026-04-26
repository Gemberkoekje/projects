namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedWaypoint
{
    public string AgentToken { get; init; } = string.Empty;

    required public string Symbol { get; init; }

    required public string SystemSymbol { get; init; }

    required public string Type { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public bool HasMarket { get; init; }

    public bool HasShipyard { get; init; }

    public DateTimeOffset LastObservedAt { get; init; }
}
