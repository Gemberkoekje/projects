using System.ComponentModel.DataAnnotations.Schema;

namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedShip
{
    public required string Symbol { get; set; }

    public string? SystemSymbol { get; set; }

    public string? WaypointSymbol { get; set; }

    public string? DestWaypointSymbol { get; set; }

    public string? Status { get; set; }

    public string? FlightMode { get; set; }

    public int FuelCurrent { get; set; }

    public int FuelCapacity { get; set; }

    public int CargoCurrent { get; set; }

    public int CargoCapacity { get; set; }

    public string? CargoJson { get; set; }

    public DateTimeOffset? ArrivesAt { get; set; }

    public DateTimeOffset LastSyncedAt { get; set; }

    [NotMapped]
    public bool IsInTransit => ArrivesAt.HasValue && ArrivesAt.Value > DateTimeOffset.UtcNow;

    public void ApplyArrivalIfDue()
    {
        if (ArrivesAt.HasValue && !IsInTransit)
        {
            WaypointSymbol = DestWaypointSymbol ?? WaypointSymbol;
            DestWaypointSymbol = null;
            ArrivesAt = null;
            Status = "IN_ORBIT";
        }
    }
}
