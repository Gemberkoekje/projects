namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedAgent
{
    public string AgentToken { get; init; } = string.Empty;

    required public string Symbol { get; init; }

    public string? AccountId { get; init; }

    public string? HeadquartersSymbol { get; init; }

    required public string StartingFaction { get; init; }

    public long Credits { get; init; }

    public int ShipCount { get; init; }

    public DateTimeOffset LastSyncedAt { get; init; }
}
