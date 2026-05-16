using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces;


/// <summary>
/// Phase 12b: classifies a ship's capabilities derived from its cached mounts and frame,
/// providing a single source of truth instead of duplicated checks across goal executors.
/// </summary>
public interface IShipCapabilityRegistry
{
    /// <summary>Returns the capability set for the given ship.</summary>
    ShipCapabilities GetCapabilities(ShipModel ship);
}

/// <summary>
/// Snapshot of a ship's derived capabilities.
/// </summary>
/// <param name="CanMine">Ship has mining equipment (mining mount or miner-type frame).</param>
/// <param name="CanSiphon">Ship has gas-siphon equipment (siphon mount or siphon-type frame).</param>
/// <param name="CanSurvey">Ship has survey equipment (surveyor mount).</param>
/// <param name="HasCargo">Ship has a cargo hold (cargo capacity &gt; 0).</param>
/// <param name="HasFuelTank">Ship has a fuel tank (fuel capacity &gt; 0).</param>
/// <param name="CanRepair">Ship has cached frame data and can be sent for structural repairs.</param>
public sealed record ShipCapabilities(
    bool CanMine,
    bool CanSiphon,
    bool CanSurvey,
    bool HasCargo,
    bool HasFuelTank,
    bool CanRepair)
{
    /// <summary>
    /// Creates a <see cref="ShipCapabilities"/> snapshot from a <see cref="ShipModel"/>.
    /// This is the single source of truth for mapping raw ship data to capability flags.
    /// </summary>
    public static ShipCapabilities From(ShipModel ship) =>
        new(
            CanMine: ship.HasMiningEquipment,
            CanSiphon: ship.HasGasSiphonEquipment,
            CanSurvey: ship.HasSurveyEquipment,
            HasCargo: ship.CargoCapacity > 0,
            HasFuelTank: ship.FuelCapacity > 0,
            CanRepair: !string.IsNullOrWhiteSpace(ship.FrameJson));

    /// <summary>
    /// Ship can perform mining operations: has mining equipment, cargo capacity, and fuel.
    /// </summary>
    public bool IsMiningCapable => CanMine && HasCargo && HasFuelTank;

    /// <summary>
    /// Ship can perform trade runs: has cargo capacity and fuel.
    /// Dedicated haulers are preferred, but mining or siphon ships can trade when idle.
    /// </summary>
    public bool IsTradingCapable => HasCargo && HasFuelTank;

    /// <summary>
    /// Ship is a probe/scout: has fuel but no cargo hold and no mining or siphon equipment.
    /// </summary>
    public bool IsProbeCapable => HasFuelTank && !HasCargo && !CanMine && !CanSiphon;
}
