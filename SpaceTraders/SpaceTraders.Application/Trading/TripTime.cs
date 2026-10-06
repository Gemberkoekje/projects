namespace SpaceTraders.Application.Trading;

/// <summary>
/// How long a trip takes, as the API reckons its flights (PLAN.md slice 6.27, D95): the trading plan ranks its routes by what
/// they earn an hour, and the role board values each role per hour (D38), both by this timing, so the two agree. A flight is
/// timed in CRUISE, leg by leg through its refuelling stops, and every landing adds <see cref="StopSeconds"/>.
/// </summary>
public static class TripTime
{
    /// <summary>The engine speed of a ship whose engine isn't cached yet: the Impulse Drive I of the drones and probes.</summary>
    public const int DefaultEngineSpeed = 9;

    /// <summary>Seconds at each landing: docking, a trade or a refuel, the market's refresh, orbiting again.</summary>
    public const double StopSeconds = 10;

    /// <summary>What the API adds to every flight.</summary>
    private const double SecondsPerFlight = 15;

    /// <summary>The API's CRUISE multiplier: a unit of distance takes 25 seconds over the engine's speed.</summary>
    private const double CruiseMultiplier = 25;

    /// <summary>The seconds a flight takes in CRUISE, leg by leg through its stops, as the API reckons them.</summary>
    /// <param name="map">The system.</param>
    /// <param name="from">Where the flight starts.</param>
    /// <param name="stops">Where it lands, in order, the destination last.</param>
    /// <param name="speed">The engine's speed.</param>
    /// <returns>The seconds in flight; 0 to stay put.</returns>
    public static double CruiseSeconds(TradeMarketMap map, string from, IReadOnlyList<string> stops, int speed)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stops);

        var seconds = 0.0;
        var at = from;
        foreach (var stop in stops)
        {
            if (map.TryGetDistance(at, stop, out var distance))
            {
                seconds += SecondsPerFlight + (Math.Max(1, Math.Round(distance)) * CruiseMultiplier / Math.Max(1, speed));
            }

            at = stop;
        }

        return seconds;
    }

    /// <summary>
    /// A trade trip's seconds from where the ship is (D95): the flight to the buy market and the haul on to the sell market,
    /// both through their refuelling stops, and a stop at each landing; a ship already at its buy market still stops there to
    /// buy.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="from">Where the ship is.</param>
    /// <param name="approachStops">The flight to the buy market's landings, the buy market last; none when the ship is there.</param>
    /// <param name="buyWaypointSymbol">The buy market.</param>
    /// <param name="haulStops">The haul's landings, the sell market last.</param>
    /// <param name="speed">The engine's speed.</param>
    /// <returns>The trip's seconds.</returns>
    public static double TradeSeconds(
        TradeMarketMap map,
        string from,
        IReadOnlyList<string> approachStops,
        string buyWaypointSymbol,
        IReadOnlyList<string> haulStops,
        int speed)
    {
        ArgumentNullException.ThrowIfNull(approachStops);
        ArgumentNullException.ThrowIfNull(haulStops);

        return CruiseSeconds(map, from, approachStops, speed)
            + CruiseSeconds(map, buyWaypointSymbol, haulStops, speed)
            + (StopSeconds * (Math.Max(1, approachStops.Count) + haulStops.Count));
    }

    /// <summary>What a trip earns an hour.</summary>
    /// <param name="credits">What it earns.</param>
    /// <param name="seconds">How long it takes.</param>
    /// <returns>The credits an hour; 0 for a trip without a time.</returns>
    public static double PerHour(long credits, double seconds) => seconds <= 0 ? 0 : credits * 3600.0 / seconds;

    /// <summary>A trip's time in whole minutes, at least 1, as the journal and the trading plan's reasons give it.</summary>
    /// <param name="seconds">The trip's seconds.</param>
    /// <returns>The minutes.</returns>
    public static double Minutes(double seconds) => Math.Max(1, Math.Round(seconds / 60));
}
