namespace SpaceTraders.Application.Trading;

/// <summary>
/// How long a trip takes, as the API reckons its flights (PLAN.md slice 6.27, D95): the trading plan ranks its routes by what
/// they earn an hour, and the role board values each role per hour (D38), both by this timing, so the two agree. A flight is
/// timed in CRUISE, leg by leg through its refuelling stops, and every landing adds <see cref="StopSeconds"/>. A jump between
/// systems (slice 6.29) takes no time itself, but starts a cooldown, and the next jump waits for it: a flight on doesn't.
/// </summary>
public static class TripTime
{
    /// <summary>The engine speed of a ship whose engine isn't cached yet: the Impulse Drive I of the drones and probes.</summary>
    public const int DefaultEngineSpeed = 9;

    /// <summary>Seconds at each landing: docking, a trade or a refuel, the market's refresh, orbiting again; or a jump.</summary>
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
                seconds += FlightSeconds(distance, speed);
            }

            at = stop;
        }

        return seconds;
    }

    /// <summary>
    /// A trade trip's seconds from where the ship is (D95): the flight to the buy market and the haul on to the sell market,
    /// both through their refuelling stops, and a stop at each landing; a ship already at its buy market still stops there to
    /// buy. Through the gates (slice 6.29), each jump after the first waits out the cooldown the jump before it started, less
    /// the time the ship flew and traded meanwhile; the first waits out what is left of the ship's own.
    /// </summary>
    /// <param name="map">The system, or the systems within the trading plan's reach.</param>
    /// <param name="from">Where the ship is.</param>
    /// <param name="approachStops">The flight to the buy market's landings, the buy market last; none when the ship is there.</param>
    /// <param name="buyWaypointSymbol">The buy market.</param>
    /// <param name="haulStops">The haul's landings, the sell market last.</param>
    /// <param name="speed">The engine's speed.</param>
    /// <param name="cooldownLeftSeconds">What is left of the ship's cooldown now, which its first jump waits for.</param>
    /// <returns>The trip's seconds.</returns>
    public static double TradeSeconds(
        TradeMarketMap map,
        string from,
        IReadOnlyList<string> approachStops,
        string buyWaypointSymbol,
        IReadOnlyList<string> haulStops,
        int speed,
        double cooldownLeftSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(approachStops);
        ArgumentNullException.ThrowIfNull(haulStops);

        var clock = new Clock(map, speed, cooldownLeftSeconds);
        clock.Fly(from, approachStops);
        if (approachStops.Count == 0)
        {
            clock.Land();
        }

        clock.Fly(buyWaypointSymbol, haulStops);
        return clock.Seconds;
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

    /// <summary>A flight's seconds in CRUISE: 15, and the distance, rounded and at least 1, times 25 over the engine's speed.</summary>
    private static double FlightSeconds(double distance, int speed)
        => SecondsPerFlight + (Math.Max(1, Math.Round(distance)) * CruiseMultiplier / Math.Max(1, speed));

    /// <summary>A trip's time, landing by landing, with the cooldown of its last jump.</summary>
    private sealed class Clock(TradeMarketMap map, int speed, double cooldownLeftSeconds)
    {
        private double _cooldownEnds = Math.Max(0, cooldownLeftSeconds);

        public double Seconds { get; private set; }

        /// <summary>Flies from <paramref name="from"/> through <paramref name="stops"/>: a flight within a system, a jump between two.</summary>
        public void Fly(string from, IReadOnlyList<string> stops)
        {
            var at = from;
            foreach (var stop in stops)
            {
                if (map.TryGetDistance(at, stop, out var distance))
                {
                    Seconds += FlightSeconds(distance, speed);
                }
                else if (!map.SameSystem(at, stop))
                {
                    Seconds = Math.Max(Seconds, _cooldownEnds);
                    _cooldownEnds = Seconds + map.Gates.CooldownSeconds(map.SystemOf(at), map.SystemOf(stop));
                }

                Land();
                at = stop;
            }
        }

        public void Land() => Seconds += StopSeconds;
    }
}
