namespace SpaceTraders.Application.Ports;

/// <summary>What a waypoint's symbol tells: its system is the symbol up to its last dash (X1-DC53-I55 is in X1-DC53).</summary>
public static class WaypointSymbols
{
    /// <summary>The system a waypoint is in.</summary>
    /// <param name="waypointSymbol">The waypoint.</param>
    /// <returns>The symbol up to its last dash; the symbol itself when it has none.</returns>
    public static string SystemOf(string waypointSymbol)
    {
        ArgumentNullException.ThrowIfNull(waypointSymbol);
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}
