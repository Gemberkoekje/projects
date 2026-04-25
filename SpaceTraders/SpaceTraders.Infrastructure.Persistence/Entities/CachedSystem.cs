namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedSystem
{
    public required string Symbol { get; set; }

    public required string SectorSymbol { get; set; }

    public required string Type { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }
}
