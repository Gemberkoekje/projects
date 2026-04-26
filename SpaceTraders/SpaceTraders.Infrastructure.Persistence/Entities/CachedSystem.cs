namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedSystem
{
    public string AgentToken { get; init; } = string.Empty;

    required public string Symbol { get; init; }

    required public string SectorSymbol { get; init; }

    required public string Type { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public DateTimeOffset LastObservedAt { get; init; }
}
