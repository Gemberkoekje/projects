namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedWaypoint
{
    public required string Symbol { get; set; }

    public required string SystemSymbol { get; set; }

    public required string Type { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public bool HasMarket { get; set; }

    public bool HasShipyard { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }
}
