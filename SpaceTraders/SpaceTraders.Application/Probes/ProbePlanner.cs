using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Probes;

/// <summary>
/// Where the probes fly (PLAN.md slice 6.3, D29, D30), with no I/O. The goal is a probe at every market,
/// where the market watch keeps its prices fresh. Until there are that many, the probes roam between
/// nearby markets, the one whose prices are oldest first:
/// <list type="number">
///   <item>a shipyard where a purchase waits for one of our ships (<see cref="ShipyardCall"/>, D30) gets
///   the nearest free probe, which stays there until the call closes;</item>
///   <item>every other free probe flies to a market that is due (prices older than the interval, the
///   market watch's <c>Market.RefreshMinutes</c>) and that no probe is at or flying to. Each pair of free
///   probe and due market is scored by the market's age minus <see cref="FlightWeight"/> times the flight
///   there, and the best pair goes first, so a market goes to the probe nearest it.</item>
/// </list>
/// With a probe at every market nothing is due that isn't held, so the probes stay where they are.
/// </summary>
public static class ProbePlanner
{
    /// <summary>
    /// How much a second of flight counts against a market's age: a market ten minutes away must be twenty
    /// minutes staler than one next door to go first. Of 1 to 6, simulated on X1-DC53's markets, 2 kept the
    /// prices youngest for two to five probes without leaving the far markets much older.
    /// </summary>
    public const double FlightWeight = 2;

    /// <summary>The engine speed of a probe whose engine isn't cached yet: SHIP_PROBE's Impulse Drive I.</summary>
    public const int DefaultProbeSpeed = 9;

    /// <summary>Plans this pass's flights.</summary>
    /// <param name="snapshot">The system, its markets and probes, the open calls, and the time.</param>
    /// <returns>One flight per probe that moves; the probes not listed stay.</returns>
    public static IReadOnlyList<ProbeMove> Plan(ProbeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var moves = new List<ProbeMove>();
        var held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var probe in snapshot.Probes)
        {
            Hold(held, probe.WaypointSymbol);
        }

        var calledAt = snapshot.Calls.Select(call => call.WaypointSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A free probe at a shipyard that calls stays: the purchase needs it there.
        var free = snapshot.Probes
            .Where(probe => probe.IsFree
                && snapshot.Positions.ContainsKey(probe.WaypointSymbol)
                && !calledAt.Contains(probe.WaypointSymbol))
            .OrderBy(probe => probe.Symbol, StringComparer.Ordinal)
            .ToList();

        foreach (var call in snapshot.Calls)
        {
            if (held.ContainsKey(call.WaypointSymbol)
                || snapshot.ShipsAt.Contains(call.WaypointSymbol)
                || !snapshot.Positions.ContainsKey(call.WaypointSymbol)
                || free.Count == 0)
            {
                continue;
            }

            var nearest = free
                .OrderBy(probe => FlightSeconds(snapshot, probe, call.WaypointSymbol))
                .ThenBy(probe => probe.Symbol, StringComparer.Ordinal)
                .First();
            moves.Add(new ProbeMove(nearest.Symbol, call.WaypointSymbol, ForPurchase: true, call.ShipType));
            Fly(held, free, nearest, call.WaypointSymbol);
        }

        var due = snapshot.Markets
            .Where(market => snapshot.Positions.ContainsKey(market.WaypointSymbol)
                && snapshot.Now - market.LastSeenAt >= snapshot.DueAfter)
            .ToList();
        while (free.Count > 0)
        {
            var pairs = free
                .SelectMany(probe => due
                    .Where(market => !held.ContainsKey(market.WaypointSymbol))
                    .Select(market => (Probe: probe, Market: market, Score: Score(snapshot, probe, market))))
                .ToList();
            if (pairs.Count == 0)
            {
                break;
            }

            var (probe, market, _) = pairs
                .OrderByDescending(pair => pair.Score)
                .ThenBy(pair => pair.Market.WaypointSymbol, StringComparer.Ordinal)
                .ThenBy(pair => pair.Probe.Symbol, StringComparer.Ordinal)
                .First();
            moves.Add(new ProbeMove(probe.Symbol, market.WaypointSymbol, ForPurchase: false, ShipType: string.Empty));
            Fly(held, free, probe, market.WaypointSymbol);
        }

        return moves;
    }

    /// <summary>
    /// Seconds a flight in CRUISE takes, as the API reckons it: 15, plus the distance (rounded, at least 1)
    /// times 25 over the engine's speed.
    /// </summary>
    /// <param name="from">Where the flight starts.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="speed">The engine's speed; <see cref="DefaultProbeSpeed"/> when it is 0 or less.</param>
    /// <returns>The flight time in seconds.</returns>
    public static double FlightSeconds(WaypointPosition from, WaypointPosition to, int speed)
    {
        var distance = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
        return 15 + (Math.Max(1, Math.Round(distance)) * 25 / (speed > 0 ? speed : DefaultProbeSpeed));
    }

    private static double FlightSeconds(ProbeSnapshot snapshot, ProbeShip probe, string destination)
        => FlightSeconds(snapshot.Positions[probe.WaypointSymbol], snapshot.Positions[destination], probe.Speed);

    private static double Score(ProbeSnapshot snapshot, ProbeShip probe, ProbeMarket market)
        => (snapshot.Now - market.LastSeenAt).TotalSeconds - (FlightWeight * FlightSeconds(snapshot, probe, market.WaypointSymbol));

    private static void Fly(Dictionary<string, int> held, List<ProbeShip> free, ProbeShip probe, string destination)
    {
        free.Remove(probe);
        if (held.TryGetValue(probe.WaypointSymbol, out var count))
        {
            if (count > 1)
            {
                held[probe.WaypointSymbol] = count - 1;
            }
            else
            {
                held.Remove(probe.WaypointSymbol);
            }
        }

        Hold(held, destination);
    }

    private static void Hold(Dictionary<string, int> held, string waypointSymbol)
        => held[waypointSymbol] = held.GetValueOrDefault(waypointSymbol) + 1;
}

/// <summary>Everything one pass of the probe plan decides from, for one system.</summary>
public sealed record ProbeSnapshot
{
    /// <summary>Where each waypoint of the system is.</summary>
    public required IReadOnlyDictionary<string, WaypointPosition> Positions { get; init; }

    /// <summary>The system's markets, with when their prices were last seen.</summary>
    public required IReadOnlyList<ProbeMarket> Markets { get; init; }

    /// <summary>The probes in the system.</summary>
    public required IReadOnlyList<ProbeShip> Probes { get; init; }

    /// <summary>The waypoints where one of our ships is, probe or not, and not in flight.</summary>
    public required IReadOnlySet<string> ShipsAt { get; init; }

    /// <summary>The shipyards where a purchase waits for one of our ships, the oldest call first.</summary>
    public required IReadOnlyList<ShipyardCall> Calls { get; init; }

    /// <summary>The time of the pass.</summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>How old a market's prices may get before a probe flies there.</summary>
    public required TimeSpan DueAfter { get; init; }
}

/// <summary>Where a waypoint is.</summary>
/// <param name="X">Its x coordinate.</param>
/// <param name="Y">Its y coordinate.</param>
public readonly record struct WaypointPosition(int X, int Y);

/// <summary>A market, and when its prices were last seen.</summary>
public sealed record ProbeMarket
{
    /// <summary>Creates the market's view.</summary>
    /// <param name="WaypointSymbol">The market's waypoint.</param>
    /// <param name="LastSeenAt">When its prices were last seen; <see cref="DateTimeOffset.MinValue"/> when never.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ProbeMarket(string WaypointSymbol, DateTimeOffset LastSeenAt)
    {
        this.WaypointSymbol = WaypointSymbol;
        this.LastSeenAt = LastSeenAt;
    }

    /// <summary>The market's waypoint.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>When its prices were last seen; <see cref="DateTimeOffset.MinValue"/> when never.</summary>
    public required DateTimeOffset LastSeenAt { get; init; }
}

/// <summary>A probe as the plan sees it.</summary>
public sealed record ProbeShip
{
    /// <summary>Creates the probe's view.</summary>
    /// <param name="Symbol">The ship's symbol.</param>
    /// <param name="WaypointSymbol">Where it is, or, while it flies or has a flight to make, where it goes.</param>
    /// <param name="IsFree">Whether the plan may send it somewhere: not in flight, and without a flight to make.</param>
    /// <param name="Speed">Its engine's speed.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ProbeShip(string Symbol, string WaypointSymbol, bool IsFree, int Speed)
    {
        this.Symbol = Symbol;
        this.WaypointSymbol = WaypointSymbol;
        this.IsFree = IsFree;
        this.Speed = Speed;
    }

    /// <summary>The ship's symbol.</summary>
    public required string Symbol { get; init; }

    /// <summary>Where it is, or, while it flies or has a flight to make, where it goes.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>Whether the plan may send it somewhere: not in flight, and without a flight to make.</summary>
    public required bool IsFree { get; init; }

    /// <summary>Its engine's speed.</summary>
    public required int Speed { get; init; }
}

/// <summary>A flight the plan gives a probe.</summary>
public sealed record ProbeMove
{
    /// <summary>Creates a flight.</summary>
    /// <param name="ShipSymbol">The probe.</param>
    /// <param name="WaypointSymbol">Where it flies.</param>
    /// <param name="ForPurchase">True when a shipyard called for it (D30); false when it roams to a market.</param>
    /// <param name="ShipType">For a call, the ship the purchase there waits to buy; otherwise empty.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ProbeMove(string ShipSymbol, string WaypointSymbol, bool ForPurchase, string ShipType)
    {
        this.ShipSymbol = ShipSymbol;
        this.WaypointSymbol = WaypointSymbol;
        this.ForPurchase = ForPurchase;
        this.ShipType = ShipType;
    }

    /// <summary>The probe.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>Where it flies.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>True when a shipyard called for it (D30); false when it roams to a market.</summary>
    public required bool ForPurchase { get; init; }

    /// <summary>For a call, the ship the purchase there waits to buy; otherwise empty.</summary>
    public required string ShipType { get; init; }
}
