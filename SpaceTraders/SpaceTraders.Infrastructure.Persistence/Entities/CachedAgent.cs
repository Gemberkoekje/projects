namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedAgent
{
    public required string Symbol { get; set; }

    public string? AccountId { get; set; }

    public string? HeadquartersSymbol { get; set; }

    public required string StartingFaction { get; set; }

    public long Credits { get; set; }

    public int ShipCount { get; set; }

    public DateTimeOffset LastSyncedAt { get; set; }
}
