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

    public string? TraitsJson { get; init; }

    public string? ModifiersJson { get; init; }

    public string? OrbitalsJson { get; init; }

    public string? ParentSymbol { get; init; }

    public bool IsUnderConstruction { get; init; }

    public string? ChartJson { get; init; }

    public DateTimeOffset LastObservedAt { get; init; }
}
