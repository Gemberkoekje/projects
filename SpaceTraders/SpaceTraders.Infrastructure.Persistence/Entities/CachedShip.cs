namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedShip
{
    public required string Symbol { get; set; }

    public string? SystemSymbol { get; set; }

    public string? WaypointSymbol { get; set; }

    public string? Status { get; set; }

    public string? FlightMode { get; set; }

    public int FuelCurrent { get; set; }

    public int FuelCapacity { get; set; }

    public DateTimeOffset? ArrivesAt { get; set; }

    public DateTimeOffset LastSyncedAt { get; set; }
}
