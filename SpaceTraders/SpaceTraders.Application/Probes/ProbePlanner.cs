using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Probes;

/// <summary>
/// Where the probes fly (PLAN.md slice 6.3, D29, D30), with no I/O. The goal is a probe at every market,
/// where the market watch keeps its prices fresh. Until there are that many, the probes roam between
/// nearby markets, the one whose prices are oldest first:
/// <list type="number">
///   <item>a shipyard where a purchase waits for one of our ships (<see cref="ShipyardCall"/>, D30) gets
///   the nearest free probe, which stays there until the call closes;</item>
///   <item>a probe parks at each shipyard (slice 6.32, D109, D110: "Stays parked"), so a purchase there needs no probe
///   called: first those that sell SHIP_EXPLORER, then the others, each the nearest free probe that isn't parked at a
///   shipyard already; one parked at a shipyard that sells no explorer gives way to one that does;</item>
///   <item>every other free probe flies to a market that is due (prices older than the interval, the
///   market watch's <c>Market.RefreshMinutes</c>) and that no probe is at or flying to. Each pair of free
///   probe and due market is scored by the market's age minus <see cref="FlightWeight"/> times the flight
///   there, and the best pair goes first, so a market goes to the probe nearest it.</item>
/// </list>
/// Once there is a probe for every market (B69), each market keeps one probe, and only the spares fly: those at a
/// waypoint that is no market, or at a market that has another, go to the markets without a probe, due or not, the
/// shipyards first, then scored the same way. A market our other ships keep fresh is never due, so roaming would never
/// fill it, and a probe next door would leave its own market for a due one. With a probe at every market, the probes
/// stay where they are.
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

    /// <summary>The shipyards a probe parks at, in the order they get one (slice 6.32, D109).</summary>
    private static readonly IReadOnlyList<ShipyardKind> ParkingOrder = [ShipyardKind.Explorer, ShipyardKind.Shipyard];

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

        var markets = snapshot.Markets
            .Where(market => snapshot.Positions.ContainsKey(market.WaypointSymbol))
            .ToList();
        if (snapshot.Probes.Count >= markets.Count)
        {
            // B69: a probe for every market; each keeps its own, and the spares fill the markets without one, the shipyards
            // first (slice 6.32, D109).
            var spares = Spares(markets, held, free);
            Park(snapshot, spares, [], markets, held, free, moves);
            SendToMarkets(snapshot, spares, markets, held, free, moves);
            return moves;
        }

        // Slice 6.32 (D110): with fewer probes than markets, a probe at a shipyard stays parked there, and the others roam.
        var parked = Parked(markets, held, free);
        var roaming = free.Except(parked).ToList();
        var yielding = parked
            .Where(probe => markets.Any(market => market.Shipyard == ShipyardKind.Shipyard && market.WaypointSymbol.Equals(probe.WaypointSymbol, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Park(snapshot, roaming, yielding, markets, held, free, moves);

        var due = markets
            .Where(market => snapshot.Now - market.LastSeenAt >= snapshot.DueAfter)
            .ToList();
        SendToMarkets(snapshot, roaming, due, held, free, moves);
        return moves;
    }

    /// <summary>
    /// The free probes a system can spare for another (PLAN.md slice 6.28): with more probes than markets, as many as it has
    /// too many, of those it would settle at no market of its own (B69): at a waypoint that is no market, or at a market that
    /// has another probe, by symbol. A probe at a shipyard that calls stays: the purchase needs it there.
    /// </summary>
    /// <param name="snapshot">The system, its markets and probes, the open calls, and the time.</param>
    /// <returns>The probes the system can spare; none while it has a market for each.</returns>
    public static IReadOnlyList<ProbeShip> Surplus(ProbeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var markets = snapshot.Markets
            .Where(market => snapshot.Positions.ContainsKey(market.WaypointSymbol))
            .ToList();
        var surplus = snapshot.Probes.Count - markets.Count;
        if (surplus <= 0)
        {
            return [];
        }

        var held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var probe in snapshot.Probes)
        {
            Hold(held, probe.WaypointSymbol);
        }

        var calledAt = snapshot.Calls.Select(call => call.WaypointSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var free = snapshot.Probes
            .Where(probe => probe.IsFree
                && snapshot.Positions.ContainsKey(probe.WaypointSymbol)
                && !calledAt.Contains(probe.WaypointSymbol))
            .OrderBy(probe => probe.Symbol, StringComparer.Ordinal)
            .ToList();
        return [.. Spares(markets, held, free).OrderBy(probe => probe.Symbol, StringComparer.Ordinal).Take(surplus)];
    }

    /// <summary>
    /// The market a probe that comes into the system from another flies to first (PLAN.md slice 6.28): of those no probe is at
    /// or on its way to, a shipyard first (slice 6.32, D109), one that sells SHIP_EXPLORER before the others, the nearest the
    /// system's jump gate, where it comes in; else the one a roaming probe would pick from the gate: the oldest prices first,
    /// less <see cref="FlightWeight"/> times the flight there. Never-seen prices count as the oldest.
    /// </summary>
    /// <param name="snapshot">The system, its markets and probes, and the time.</param>
    /// <param name="gateWaypointSymbol">The system's jump gate; without its position, the oldest prices win.</param>
    /// <param name="speed">The probe's engine speed.</param>
    /// <returns>The market; empty when each has a probe at it or on its way.</returns>
    public static string Entry(ProbeSnapshot snapshot, string gateWaypointSymbol, int speed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(gateWaypointSymbol);

        var held = snapshot.Probes.Select(probe => probe.WaypointSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasGate = snapshot.Positions.TryGetValue(gateWaypointSymbol, out var gate);
        var open = snapshot.Markets
            .Where(market => !held.Contains(market.WaypointSymbol) && snapshot.Positions.ContainsKey(market.WaypointSymbol))
            .ToList();
        foreach (var kind in ParkingOrder)
        {
            var shipyard = open
                .Where(market => market.Shipyard == kind)
                .OrderBy(market => hasGate ? FlightSeconds(gate, snapshot.Positions[market.WaypointSymbol], speed) : 0)
                .ThenBy(market => market.WaypointSymbol, StringComparer.Ordinal)
                .FirstOrDefault();
            if (shipyard is not null)
            {
                return shipyard.WaypointSymbol;
            }
        }

        return open
            .Select(market => (
                market.WaypointSymbol,
                Score: (snapshot.Now - market.LastSeenAt).TotalSeconds
                    - (hasGate ? FlightWeight * FlightSeconds(gate, snapshot.Positions[market.WaypointSymbol], speed) : 0)))
            .OrderByDescending(market => market.Score)
            .ThenBy(market => market.WaypointSymbol, StringComparer.Ordinal)
            .Select(market => market.WaypointSymbol)
            .FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// Where a probe counts (B15, slice 6.28): while it flies or has a flight to make, at the market its flight goes to, in this
    /// system or another; in flight without one, where it lands; otherwise where it is. A probe that only passes through a
    /// system, jumping on to another, counts for the other.
    /// </summary>
    /// <param name="probe">The probe, as cached.</param>
    /// <param name="goal">Its active goal, if it has one.</param>
    /// <returns>The waypoint it holds, empty when none is known, and the system it counts for.</returns>
    public static (string WaypointSymbol, string SystemSymbol) Whereabouts(ShipModel probe, ShipGoal? goal)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var flight = goal is DeployProbeGoal { Status: not GoalStatus.Completed and not GoalStatus.Blocked } deploy
            ? deploy.TargetWaypointSymbol
            : string.Empty;
        var waypoint = probe.LocalStatus == ShipLocalStatus.InTransit
            ? FirstOf(flight, probe.DestWaypointSymbol, probe.WaypointSymbol)
            : FirstOf(flight, probe.WaypointSymbol);
        return (waypoint, waypoint.Length > 0 ? WaypointSymbols.SystemOf(waypoint) : probe.SystemSymbol ?? string.Empty);
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

    /// <summary>
    /// Sends the probes to the markets no probe is at or flying to, the best pair of probe and market first, until either
    /// runs out.
    /// </summary>
    private static void SendToMarkets(
        ProbeSnapshot snapshot,
        List<ProbeShip> probes,
        IReadOnlyList<ProbeMarket> markets,
        Dictionary<string, int> held,
        List<ProbeShip> free,
        List<ProbeMove> moves)
    {
        while (probes.Count > 0)
        {
            var pairs = probes
                .SelectMany(probe => markets
                    .Where(market => !held.ContainsKey(market.WaypointSymbol))
                    .Select(market => (Probe: probe, Market: market, Score: Score(snapshot, probe, market))))
                .ToList();
            if (pairs.Count == 0)
            {
                return;
            }

            var (probe, market, _) = pairs
                .OrderByDescending(pair => pair.Score)
                .ThenBy(pair => pair.Market.WaypointSymbol, StringComparer.Ordinal)
                .ThenBy(pair => pair.Probe.Symbol, StringComparer.Ordinal)
                .First();
            moves.Add(new ProbeMove(probe.Symbol, market.WaypointSymbol, ForPurchase: false, ShipType: string.Empty));
            probes.Remove(probe);
            Fly(held, free, probe, market.WaypointSymbol);
        }
    }

    /// <summary>
    /// Parks a probe at each shipyard no probe is at or flying to (slice 6.32, D109, D110), in <see cref="ParkingOrder"/>: those
    /// that sell SHIP_EXPLORER first, each the nearest of <paramref name="probes"/> or of the <paramref name="yielding"/> probes,
    /// parked at a shipyard that sells none, which give way; then the other shipyards, each the nearest of
    /// <paramref name="probes"/>. The nearest pair goes first.
    /// </summary>
    private static void Park(
        ProbeSnapshot snapshot,
        List<ProbeShip> probes,
        List<ProbeShip> yielding,
        IReadOnlyList<ProbeMarket> markets,
        Dictionary<string, int> held,
        List<ProbeShip> free,
        List<ProbeMove> moves)
    {
        foreach (var kind in ParkingOrder)
        {
            while (Nearest(snapshot, kind == ShipyardKind.Explorer ? [.. probes, .. yielding] : probes, markets, kind, held) is { } pair)
            {
                moves.Add(new ProbeMove(pair.Probe.Symbol, pair.Shipyard.WaypointSymbol, ForPurchase: false, ShipType: string.Empty));
                probes.Remove(pair.Probe);
                yielding.Remove(pair.Probe);
                Fly(held, free, pair.Probe, pair.Shipyard.WaypointSymbol);
            }
        }
    }

    /// <summary>The nearest pair of a probe and a shipyard of <paramref name="kind"/> that no probe is at or flying to; null for none.</summary>
    private static (ProbeShip Probe, ProbeMarket Shipyard)? Nearest(
        ProbeSnapshot snapshot,
        IReadOnlyList<ProbeShip> probes,
        IReadOnlyList<ProbeMarket> markets,
        ShipyardKind kind,
        Dictionary<string, int> held)
        => probes
            .SelectMany(probe => markets
                .Where(market => market.Shipyard == kind && !held.ContainsKey(market.WaypointSymbol))
                .Select(market => (Probe: probe, Shipyard: market, Seconds: FlightSeconds(snapshot, probe, market.WaypointSymbol))))
            .OrderBy(pair => pair.Seconds)
            .ThenBy(pair => pair.Shipyard.WaypointSymbol, StringComparer.Ordinal)
            .ThenBy(pair => pair.Probe.Symbol, StringComparer.Ordinal)
            .Select(pair => ((ProbeShip Probe, ProbeMarket Shipyard)?)(pair.Probe, pair.Shipyard))
            .FirstOrDefault();

    /// <summary>
    /// The free probes parked at the shipyards (slice 6.32, D110): at each, the first by symbol, unless another probe flies
    /// there or has a flight to make there, which keeps it.
    /// </summary>
    private static List<ProbeShip> Parked(IReadOnlyList<ProbeMarket> markets, Dictionary<string, int> held, List<ProbeShip> free)
    {
        var shipyards = markets
            .Where(market => market.Shipyard != ShipyardKind.None)
            .Select(market => market.WaypointSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. free
            .Where(probe => shipyards.Contains(probe.WaypointSymbol))
            .GroupBy(probe => probe.WaypointSymbol, StringComparer.OrdinalIgnoreCase)
            .Where(group => held.GetValueOrDefault(group.Key) == group.Count())
            .Select(group => group.First())];
    }

    /// <summary>
    /// The free probes their waypoint can spare (B69): all at a waypoint that is no market; at a market, all but the first
    /// by symbol, or all while another probe flies there.
    /// </summary>
    private static List<ProbeShip> Spares(IReadOnlyList<ProbeMarket> markets, Dictionary<string, int> held, List<ProbeShip> free)
    {
        var marketSymbols = markets.Select(market => market.WaypointSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. free
            .GroupBy(probe => probe.WaypointSymbol, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => marketSymbols.Contains(group.Key) && held.GetValueOrDefault(group.Key) == group.Count()
                ? group.Skip(1)
                : group)];
    }

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

    private static string FirstOf(params string?[] symbols)
        => symbols.FirstOrDefault(symbol => !string.IsNullOrWhiteSpace(symbol)) ?? string.Empty;
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

    /// <summary>
    /// What the market has of a shipyard (slice 6.32, D109, D110): none, a shipyard, or one that sells SHIP_EXPLORER. A probe
    /// parks at a shipyard, those that sell explorers first.
    /// </summary>
    public ShipyardKind Shipyard { get; init; }
}

/// <summary>What a market has of a shipyard, which decides where the probes park first (PLAN.md slice 6.32, D109, D110).</summary>
public enum ShipyardKind
{
    /// <summary>No shipyard: a market the probes roam to (D29).</summary>
    None = 0,

    /// <summary>A shipyard: a probe parks there, so a purchase there needs no probe called (D30, D110).</summary>
    Shipyard = 1,

    /// <summary>A shipyard that sells SHIP_EXPLORER: it gets its probe before every other shipyard (D109, D111).</summary>
    Explorer = 2,
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
