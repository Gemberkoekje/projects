using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Commands.Ships;

/// <summary>
/// Standard result returned by ship-scoped commands describing the ship's
/// updated local state after the command executed (or its current state if
/// the command was rejected).
/// </summary>
public sealed record ShipCommandResult
{
    public required string ShipSymbol { get; init; }

    public required ShipLocalStatus Status { get; init; }

    public required string SystemSymbol { get; init; }

    public required string WaypointSymbol { get; init; }

    public DateTimeOffset ArrivesAt { get; init; }

    public int FuelCurrent { get; init; }

    public int FuelCapacity { get; init; }

    public int CargoCurrent { get; init; }

    public int CargoCapacity { get; init; }

    /// <summary>
    /// True when the command was accepted and the ship state advanced.
    /// False when the command was rejected (e.g. wrong local status).
    /// </summary>
    public bool Accepted { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipCommandResult(
        string ShipSymbol,
        ShipLocalStatus Status,
        string SystemSymbol,
        string WaypointSymbol,
        DateTimeOffset ArrivesAt = default,
        int FuelCurrent = 0,
        int FuelCapacity = 0,
        int CargoCurrent = 0,
        int CargoCapacity = 0,
        bool Accepted = true)
    {
        this.ShipSymbol = ShipSymbol;
        this.Status = Status;
        this.SystemSymbol = SystemSymbol;
        this.WaypointSymbol = WaypointSymbol;
        this.ArrivesAt = ArrivesAt;
        this.FuelCurrent = FuelCurrent;
        this.FuelCapacity = FuelCapacity;
        this.CargoCurrent = CargoCurrent;
        this.CargoCapacity = CargoCapacity;
        this.Accepted = Accepted;
    }

    /// <summary>
    /// Creates a rejected result that mirrors the ship's current state.
    /// </summary>
    public static ShipCommandResult Rejected(string shipSymbol, ShipLocalStatus currentStatus, string systemSymbol, string waypointSymbol) =>
        new(shipSymbol, currentStatus, systemSymbol, waypointSymbol, Accepted: false);
}
