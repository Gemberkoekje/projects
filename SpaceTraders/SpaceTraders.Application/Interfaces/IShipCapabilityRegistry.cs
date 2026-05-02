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
    bool CanRepair);
