using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Phase 12b: derives a ship's capabilities from its cached mounts and frame.
/// All goal executors that need to validate ship capabilities should use
/// <see cref="IShipCapabilityRegistry"/> rather than inspecting <see cref="ShipModel"/>
/// properties directly.
/// </summary>
public sealed class ShipCapabilityRegistry : IShipCapabilityRegistry
{
    /// <inheritdoc/>
    public ShipCapabilities GetCapabilities(ShipModel ship) => ShipCapabilities.From(ship);
}
