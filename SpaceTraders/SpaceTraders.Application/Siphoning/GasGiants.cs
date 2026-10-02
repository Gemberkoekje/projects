using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Siphoning;

/// <summary>
/// What a gas siphon yields where (PLAN.md slice 6.7), as <see cref="Mining.AsteroidDeposits"/> does for mining
/// lasers. The game publishes no table, and a gas giant's traits name none of its gases (X1-DC53's gas giant C38
/// has only STRONG_MAGNETOSPHERE), so every gas giant counts as yielding the game's three gases, about equally:
/// those C39, the station at C38, exchanges. The first siphons show what is really there (the <c>Siphoned</c>
/// journal lines). Nothing narrows a siphon's yield: the API's siphon call takes no survey, and a surveyor's
/// deposits are ores only.
/// </summary>
public static class GasGiants
{
    /// <summary>Everything a gas siphon can yield. Other goods (ores, refined goods) are never siphoned.</summary>
    public static readonly IReadOnlySet<string> Gases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "HYDROCARBON",
        "LIQUID_HYDROGEN",
        "LIQUID_NITROGEN",
    };

    private const string GasGiantType = "GAS_GIANT";

    /// <summary>Whether ships can siphon at the waypoint: a gas giant.</summary>
    /// <param name="waypointType">The waypoint's type, as the API names it.</param>
    /// <returns>True for a waypoint a gas siphon works at.</returns>
    public static bool IsSiphonable(string waypointType)
        => GasGiantType.Equals(waypointType, StringComparison.OrdinalIgnoreCase);

    /// <summary>The gases a waypoint yields: none for one that can't be siphoned.</summary>
    /// <param name="waypoint">The waypoint.</param>
    /// <returns>The gases, by symbol.</returns>
    public static IReadOnlyList<string> GasesAt(WaypointCacheModel waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        return IsSiphonable(waypoint.Type) ? [.. Gases.Order(StringComparer.Ordinal)] : [];
    }

    /// <summary>Whether a waypoint yields a gas.</summary>
    /// <param name="waypoint">The waypoint.</param>
    /// <param name="gas">The gas.</param>
    /// <returns>True when the waypoint is a gas giant and the good is one of the gases.</returns>
    public static bool CanYield(WaypointCacheModel waypoint, string gas)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        return IsSiphonable(waypoint.Type) && Gases.Contains(gas ?? string.Empty);
    }
}
