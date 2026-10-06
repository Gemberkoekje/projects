using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Exploring;

/// <summary>
/// Which waypoints an exploring ship charts (PLAN.md slice 6.30, D99), asked on 2026-10-06: "Every uncharted market or
/// shipyard, I'm not sure if every single asteroid needs to be charted but I don't want my explorer to waste time on that."
/// An uncharted waypoint hides its traits, a marketplace or shipyard among them, so the ship charts every uncharted waypoint
/// of a type that can hold one: every type but ASTEROID and GAS_GIANT, none of which held a market or shipyard in the seven
/// systems seen by then.
/// </summary>
public static class Charting
{
    /// <summary>The trait the API gives a waypoint nobody has charted.</summary>
    public const string UnchartedTrait = "UNCHARTED";

    /// <summary>The waypoint types the exploring ship doesn't chart: none of them held a market or shipyard.</summary>
    private static readonly IReadOnlySet<string> NotCharted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ASTEROID", "GAS_GIANT" };

    /// <summary>Whether nobody has charted the waypoint yet: its traits hold <see cref="UnchartedTrait"/>.</summary>
    /// <param name="waypoint">The waypoint, as cached.</param>
    /// <returns>True for an uncharted waypoint.</returns>
    public static bool IsUncharted(WaypointCacheModel waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        return AsteroidDeposits.TraitSymbols(waypoint.TraitsJson).Contains(UnchartedTrait, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether the exploring ship charts the waypoint (D99): uncharted, and of a type that can hold a market or shipyard.</summary>
    /// <param name="waypoint">The waypoint, as cached.</param>
    /// <returns>True for a waypoint to chart.</returns>
    public static bool ShouldChart(WaypointCacheModel waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        return !NotCharted.Contains(waypoint.Type) && IsUncharted(waypoint);
    }

    /// <summary>A waypoint as the API gave it, as the cache keeps it.</summary>
    /// <param name="waypoint">The waypoint the API gave.</param>
    /// <param name="now">When it was seen.</param>
    /// <returns>The cache row.</returns>
    public static WaypointCacheModel ToCache(WaypointDataModel waypoint, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        return new(
            waypoint.Symbol,
            waypoint.SystemSymbol,
            waypoint.Type,
            waypoint.X,
            waypoint.Y,
            waypoint.HasMarket,
            waypoint.HasShipyard,
            now,
            waypoint.TraitsJson ?? "[]",
            waypoint.ModifiersJson ?? "[]",
            waypoint.OrbitalsJson,
            waypoint.ParentSymbol,
            waypoint.IsUnderConstruction,
            waypoint.ChartJson);
    }
}
