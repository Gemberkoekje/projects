using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Probes;

/// <summary>
/// What one more probe in a system is worth an hour, for whether it pays back before the server resets (PLAN.md slice
/// 6.71, D151), asked on 2026-11-15: "putting a probe up 10 minutes before the server resets does not give any value at
/// all". Chosen: "Market promise pays back". It is the best promise (<see cref="ProbePromise"/>, D134, D135) of the
/// system's markets no probe is at or on its way to. A market never seen has no promise, as D134 puts it first for
/// that: it counts as the best promise of the system's markets that were seen, and in a system with none seen, as the
/// average of the markets elsewhere that promise anything ("System's best, else average"). With none of that known,
/// nothing is.
/// </summary>
public static class ProbeWorth
{
    /// <summary>What the promise is: the market's.</summary>
    public const string Basis = "market promise";

    /// <summary>What one more probe in the system is worth an hour (<see cref="ProbeWorth"/>).</summary>
    /// <param name="markets">The system's markets, with when their prices were last seen.</param>
    /// <param name="probes">The probes in the system or on their way, each where it is or goes.</param>
    /// <param name="promises">
    /// The promise of each of the system's markets, by waypoint; a market it leaves out promises nothing.
    /// </param>
    /// <param name="elsewhere">The promises above 0 of the markets of the other systems the plan knows.</param>
    /// <returns>The worth; unknown when no market's promise tells.</returns>
    public static ShipWorth Of(
        IReadOnlyList<ProbeMarket> markets,
        IReadOnlyList<ProbeShip> probes,
        IReadOnlyDictionary<string, double> promises,
        IReadOnlyCollection<double> elsewhere)
    {
        ArgumentNullException.ThrowIfNull(markets);
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(promises);
        ArgumentNullException.ThrowIfNull(elsewhere);

        var watched = probes.Select(probe => probe.WaypointSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unwatched = markets.Where(market => !watched.Contains(market.WaypointSymbol)).ToList();
        var seen = markets.Where(market => market.LastSeenAt > DateTimeOffset.MinValue).ToList();

        // A market never seen counts as the system's best seen market, else the average of those elsewhere that promise
        // anything.
        var neverSeen = seen.Count > 0
            ? ShipWorth.Of(seen.Max(market => promises.GetValueOrDefault(market.WaypointSymbol)), Basis)
            : elsewhere.Count > 0 ? ShipWorth.Of(elsewhere.Average(), Basis) : ShipWorth.Unknown;

        var worths = unwatched
            .Select(market => market.LastSeenAt > DateTimeOffset.MinValue
                ? ShipWorth.Of(promises.GetValueOrDefault(market.WaypointSymbol), Basis)
                : neverSeen)
            .Where(worth => worth.Known)
            .ToList();
        return worths.Count > 0 ? ShipWorth.Of(worths.Max(worth => worth.PerHour), Basis) : ShipWorth.Unknown;
    }
}
