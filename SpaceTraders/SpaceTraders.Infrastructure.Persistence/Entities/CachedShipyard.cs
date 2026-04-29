namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedShipyard
{
    public string AgentToken { get; init; } = string.Empty;

    required public string WaypointSymbol { get; init; }

    required public string SystemSymbol { get; init; }

    public string? ShipTypesJson { get; init; }

    public string? ShipsDetailJson { get; init; }

    public DateTimeOffset LastObservedAt { get; init; }
}
