namespace SpaceTraders.Domain.Enums;

/// <summary>
/// Maps SpaceTraders API nav status strings to <see cref="ShipLocalStatus"/> values.
/// </summary>
public static class ShipLocalStatusMapper
{
    /// <summary>
    /// Converts a SpaceTraders API nav status string (e.g. "DOCKED", "IN_ORBIT", "IN_TRANSIT")
    /// to the corresponding <see cref="ShipLocalStatus"/>.
    /// Returns <see cref="ShipLocalStatus.None"/> for unrecognised values.
    /// </summary>
    public static ShipLocalStatus FromApiStatus(string? apiStatus) =>
        apiStatus?.ToUpperInvariant() switch
        {
            "DOCKED" => ShipLocalStatus.Docked,
            "IN_ORBIT" => ShipLocalStatus.InOrbit,
            "IN_TRANSIT" => ShipLocalStatus.InTransit,
            _ => ShipLocalStatus.None,
        };
}
