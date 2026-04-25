namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedShipyard
{
    public required string WaypointSymbol { get; set; }

    public required string SystemSymbol { get; set; }

    public string? ShipTypesJson { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }
}
