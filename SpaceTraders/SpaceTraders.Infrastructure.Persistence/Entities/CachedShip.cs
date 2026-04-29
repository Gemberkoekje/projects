using System.ComponentModel.DataAnnotations.Schema;

namespace SpaceTraders.Infrastructure.Persistence.Entities;

[Mutable]
public sealed class CachedShip
{
    public string AgentToken { get; set; } = string.Empty;

    required public string Symbol { get; set; }

    public string? SystemSymbol { get; set; }

    public string? WaypointSymbol { get; set; }

    public string? DestWaypointSymbol { get; set; }

    public string? Status { get; set; }

    public string? FlightMode { get; set; }

    public string ShipType { get; set; } = string.Empty;

    public string? MountsJson { get; set; }

    public string? ModulesJson { get; set; }

    public string? FrameJson { get; set; }

    public string? ReactorJson { get; set; }

    public string? EngineJson { get; set; }

    public DateTimeOffset? CooldownExpiresAt { get; set; }

    public int FuelCurrent { get; set; }

    public int FuelCapacity { get; set; }

    public int CargoCurrent { get; set; }

    public int CargoCapacity { get; set; }

    public string? CargoJson { get; set; }

    public DateTimeOffset? ArrivesAt { get; set; }

    public DateTimeOffset LastSyncedAt { get; set; }

    [NotMapped]
    public bool IsInTransit => ArrivesAt.HasValue && ArrivesAt.Value > TimeProvider.System.GetUtcNow();

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
