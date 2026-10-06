using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Exploring;

/// <summary>How a step of a way between systems goes (PLAN.md slice 6.31, D101).</summary>
public enum WayStepKind
{
    /// <summary>Not judged.</summary>
    None = 0,

    /// <summary>A jump from the gate of the system the ship is in to a gate that gate connects to.</summary>
    Jump = 1,

    /// <summary>A warp from the system the ship is in to a waypoint of another.</summary>
    Warp = 2,
}

/// <summary>One step of a way between systems (PLAN.md slice 6.31, D101).</summary>
public sealed record WayStep
{
    /// <summary>Creates a step.</summary>
    /// <param name="Kind">A jump or a warp.</param>
    /// <param name="FromWaypointSymbol">For a jump, the gate it leaves from; for a warp, where it leaves from.</param>
    /// <param name="ToWaypointSymbol">For a jump, the gate it jumps to; for a warp, the waypoint it lands at.</param>
    /// <param name="FlightMode">For a warp, BURN or CRUISE (D104); empty for a jump.</param>
    /// <param name="Fuel">For a warp, the fuel it burns; 0 for a jump.</param>
    /// <param name="Seconds">The seconds from the end of the step before to the end of this one, as reckoned.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public WayStep(WayStepKind Kind, string FromWaypointSymbol, string ToWaypointSymbol, string FlightMode, int Fuel, double Seconds)
    {
        this.Kind = Kind;
        this.FromWaypointSymbol = FromWaypointSymbol;
        this.ToWaypointSymbol = ToWaypointSymbol;
        this.FlightMode = FlightMode;
        this.Fuel = Fuel;
        this.Seconds = Seconds;
    }

    /// <summary>A jump or a warp.</summary>
    public required WayStepKind Kind { get; init; }

    /// <summary>For a jump, the gate it leaves from; for a warp, where it leaves from: where the ship is, or a market it refuels at first.</summary>
    public required string FromWaypointSymbol { get; init; }

    /// <summary>For a jump, the gate it jumps to; for a warp, the waypoint it lands at.</summary>
    public required string ToWaypointSymbol { get; init; }

    /// <summary>For a warp, BURN or CRUISE (D104); empty for a jump.</summary>
    public required string FlightMode { get; init; }

    /// <summary>For a warp, the fuel it burns; 0 for a jump.</summary>
    public required int Fuel { get; init; }

    /// <summary>The seconds from the end of the step before to the end of this one, as reckoned.</summary>
    public required double Seconds { get; init; }

    /// <summary>The system the step leaves.</summary>
    public string FromSystemSymbol => WaypointSymbols.SystemOf(FromWaypointSymbol);

    /// <summary>The system the step ends in.</summary>
    public string ToSystemSymbol => WaypointSymbols.SystemOf(ToWaypointSymbol);
}

/// <summary>A way between systems: its steps, in order, and the seconds they take, as reckoned (PLAN.md slice 6.31, D101).</summary>
public sealed record SystemWay
{
    /// <summary>Creates a way.</summary>
    /// <param name="Steps">The steps, in order.</param>
    /// <param name="Seconds">The seconds they take, as reckoned.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SystemWay(IReadOnlyList<WayStep> Steps, double Seconds)
    {
        this.Steps = Steps;
        this.Seconds = Seconds;
    }

    /// <summary>No way: no steps.</summary>
    public static SystemWay None { get; } = new([], 0);

    /// <summary>The steps, in order.</summary>
    public required IReadOnlyList<WayStep> Steps { get; init; }

    /// <summary>The seconds they take, as reckoned.</summary>
    public required double Seconds { get; init; }

    /// <summary>How many of its steps are jumps.</summary>
    public int Jumps => Steps.Count(step => step.Kind == WayStepKind.Jump);

    /// <summary>How many of its steps are warps.</summary>
    public int Warps => Steps.Count(step => step.Kind == WayStepKind.Warp);
}

/// <summary>A ship as the ways between systems see it (PLAN.md slice 6.31).</summary>
public sealed record WayShip
{
    /// <summary>Creates the ship's view.</summary>
    /// <param name="SystemSymbol">The system it is in.</param>
    /// <param name="WaypointSymbol">The waypoint it is at.</param>
    /// <param name="Fuel">The fuel aboard.</param>
    /// <param name="FuelCapacity">What its tank holds.</param>
    /// <param name="Speed">Its engine's speed.</param>
    /// <param name="WarpRange">The farthest its warp drive warps; 0 without one.</param>
    /// <param name="CooldownSeconds">The seconds left of its cooldown, which holds back its next jump.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public WayShip(string SystemSymbol, string WaypointSymbol, int Fuel, int FuelCapacity, int Speed, int WarpRange, double CooldownSeconds)
    {
        this.SystemSymbol = SystemSymbol;
        this.WaypointSymbol = WaypointSymbol;
        this.Fuel = Fuel;
        this.FuelCapacity = FuelCapacity;
        this.Speed = Speed;
        this.WarpRange = WarpRange;
        this.CooldownSeconds = CooldownSeconds;
    }

    /// <summary>The system it is in.</summary>
    public required string SystemSymbol { get; init; }

    /// <summary>The waypoint it is at.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>The fuel aboard.</summary>
    public required int Fuel { get; init; }

    /// <summary>What its tank holds.</summary>
    public required int FuelCapacity { get; init; }

    /// <summary>Its engine's speed.</summary>
    public required int Speed { get; init; }

    /// <summary>The farthest its warp drive warps (<see cref="Exploring.Warps.Range"/>); 0 without one.</summary>
    public required int WarpRange { get; init; }

    /// <summary>The seconds left of its cooldown, which holds back its next jump.</summary>
    public required double CooldownSeconds { get; init; }

    /// <summary>The farthest a single warp takes it on a full tank (D104: no drifts): the tank or the drive's range, the least.</summary>
    public int MaxWarp => Math.Min(WarpRange, FuelCapacity);

    /// <summary>A cached ship as the ways see it.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="now">The time its cooldown is judged by.</param>
    /// <returns>The ship's view.</returns>
    public static WayShip Of(ShipModel ship, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return new WayShip(
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol ?? string.Empty,
            ship.FuelCurrent,
            ship.FuelCapacity,
            FleetRoles.EngineSpeed(ship, TripTime.DefaultEngineSpeed),
            Exploring.Warps.Range(ship),
            ship.CooldownExpiresAt is { } cooldown && cooldown > now ? (cooldown - now).TotalSeconds : 0);
    }
}

/// <summary>
/// The fastest ways from a ship to the other systems (PLAN.md slice 6.31, D101: "One planner for every way ... Whether to jump
/// or use a warp drive if the ship has one, based on distance and fuel"): through the gates the explore plan knows, and, for a
/// ship with a warp drive, by warps, weighed by the seconds they take as the API reckons them:
/// <list type="bullet">
///   <item>a jump (<see cref="ExploreAtlas.IsUsable"/>: both gates built, and not refused within the hour) takes no time itself,
///   but the ship first flies to its gate, and a jump waits out the cooldown the jump before it started (17 seconds plus 0.311 a
///   unit of the systems' distance, <see cref="TradeGates.CooldownBaseSeconds"/>); a flight or a warp meanwhile doesn't;</item>
///   <item>a warp (<see cref="Exploring.Warps"/>) goes from wherever the ship is in its system, within the drive's range, in BURN
///   where the fuel pays for it and in CRUISE otherwise (D104: never a drift). It is fuel-safe (D100): it lands where the ship
///   can refuel (<see cref="WayPoint.CanRefuel"/>), or, into a system with nowhere to refuel, it keeps the fuel to warp back
///   and goes no further. A ship fills its tank where it leaves a market, and one short of the fuel elsewhere flies to its
///   system's nearest market first. A system the API refused a warp into lately gets none (<see cref="WarpRefusals"/>).</item>
/// </list>
/// Dijkstra over the systems and where the ship is in each, by the seconds.
/// </summary>
public static class SystemWays
{
    /// <summary>A unit of distance in a system takes 25 seconds over the engine's speed in CRUISE.</summary>
    private const double FlightMultiplier = 25;

    /// <summary>What the API adds to every flight.</summary>
    private const double SecondsPerFlight = 15;

    /// <summary>
    /// The fastest way from the ship to each system it can get to, but the one it is in: by jumps alone for a ship without a warp
    /// drive.
    /// </summary>
    /// <param name="chart">The gates, the systems' positions and their waypoints.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <returns>The ways, by system.</returns>
    public static IReadOnlyDictionary<string, SystemWay> From(WayChart chart, WayShip ship)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(ship);

        var search = new Search(chart, ship);
        return search.Run();
    }

    /// <summary>
    /// The fastest way from the ship to a waypoint of another system (<see cref="From"/>). A last warp lands at that waypoint
    /// where the ship can refuel there: the warp costs the same wherever in the system it lands.
    /// </summary>
    /// <param name="chart">The gates, the systems' positions and their waypoints.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="destination">The waypoint it is going to.</param>
    /// <param name="way">The way; <see cref="SystemWay.None"/> when there is none.</param>
    /// <returns>False when no way is known, or the ship is in that system already.</returns>
    public static bool TryFind(WayChart chart, WayShip ship, string destination, out SystemWay way)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!From(chart, ship).TryGetValue(WaypointSymbols.SystemOf(destination), out var found) || found.Steps.Count == 0)
        {
            way = SystemWay.None;
            return false;
        }

        var last = found.Steps[^1];
        way = last.Kind == WayStepKind.Warp && chart.TryGetWaypoint(destination, out var landing) && landing.Refuels
            ? found with { Steps = [.. found.Steps.Take(found.Steps.Count - 1), last with { ToWaypointSymbol = landing.Symbol }] }
            : found;
        return true;
    }

    /// <summary>
    /// Where a warp into a system lands: the market nearest its gate where it has both, else its first market; a system with
    /// nowhere to refuel, at its gate or its first waypoint. None while its waypoints were never fetched.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="systemSymbol">The system.</param>
    /// <param name="landing">The waypoint.</param>
    /// <returns>False when the chart has none of the system's waypoints.</returns>
    public static bool TryFindLanding(WayChart chart, string systemSymbol, out WayPoint landing)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(systemSymbol);

        var waypoints = chart.WaypointsOf(systemSymbol);
        landing = new WayPoint(string.Empty, systemSymbol, 0, 0, Refuels: false);
        if (waypoints.Count == 0)
        {
            return false;
        }

        var gate = chart.Network.Systems
            .FirstOrDefault(system => system.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))?
            .GateWaypointSymbol ?? string.Empty;
        var (x, y) = chart.TryGetWaypoint(gate, out var at) ? (at.X, at.Y) : (0, 0);
        var markets = waypoints.Where(waypoint => waypoint.Refuels).ToList();
        landing = markets.Count > 0
            ? markets.OrderBy(waypoint => Squared(waypoint.X - x, waypoint.Y - y)).ThenBy(waypoint => waypoint.Symbol, StringComparer.Ordinal).First()
            : chart.TryGetWaypoint(gate, out var gateAt) && gateAt.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase) ? gateAt : waypoints[0];
        return true;
    }

    private static long Squared(long dx, long dy) => (dx * dx) + (dy * dy);

    /// <summary>Where the ship is, as one state of the search: a waypoint of a system, and whether it goes no further from there.</summary>
    private sealed record Node(string SystemSymbol, string WaypointSymbol, bool End);

    /// <summary>
    /// How the ship got to a node the fastest way known: the seconds, the fuel aboard, the cooldown left, the node before and the
    /// step from it.
    /// </summary>
    private sealed record Label(double Seconds, int Fuel, double Cooldown, string Previous, WayStep Step);

    /// <summary>One run of the search from a ship.</summary>
    private sealed class Search
    {
        private readonly WayChart _chart;
        private readonly WayShip _ship;
        private readonly Dictionary<string, KnownSystem> _bySystem;
        private readonly Dictionary<string, KnownSystem> _byGate;
        private readonly Dictionary<string, Node> _nodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Label> _labels = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _settled = new(StringComparer.OrdinalIgnoreCase);
        private readonly PriorityQueue<string, double> _open = new();
        private readonly Dictionary<string, (bool Found, WayPoint Landing)> _landings = new(StringComparer.OrdinalIgnoreCase);

        public Search(WayChart chart, WayShip ship)
        {
            _chart = chart;
            _ship = ship;
            _bySystem = chart.Network.Systems
                .GroupBy(system => system.SystemSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            _byGate = _bySystem.Values
                .Where(system => system.GateWaypointSymbol.Length > 0)
                .GroupBy(system => system.GateWaypointSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, SystemWay> Run()
        {
            if (_ship.SystemSymbol.Length == 0)
            {
                return new Dictionary<string, SystemWay>(StringComparer.OrdinalIgnoreCase);
            }

            var start = Key(_ship.SystemSymbol, _ship.WaypointSymbol);
            _nodes[start] = new Node(_ship.SystemSymbol, _ship.WaypointSymbol, End: false);
            _labels[start] = new Label(0, _ship.Fuel, _ship.CooldownSeconds, string.Empty, new WayStep(WayStepKind.None, string.Empty, string.Empty, string.Empty, 0, 0));
            _open.Enqueue(start, 0);
            while (_open.TryDequeue(out var key, out _))
            {
                if (!_settled.Add(key))
                {
                    continue;
                }

                var node = _nodes[key];
                if (node.End)
                {
                    continue;
                }

                JumpsFrom(key, node, _labels[key]);
                WarpsFrom(key, node, _labels[key]);
            }

            // The fastest node in each system, but the ship's own; on a tie, the first by waypoint.
            return _settled
                .Select(key => (Key: key, Node: _nodes[key], Label: _labels[key]))
                .Where(entry => !entry.Node.SystemSymbol.Equals(_ship.SystemSymbol, StringComparison.OrdinalIgnoreCase))
                .GroupBy(entry => entry.Node.SystemSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => Way(group.OrderBy(entry => entry.Label.Seconds).ThenBy(entry => entry.Key, StringComparer.Ordinal).First().Key),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static string Key(string system, string waypoint) => $"{system}|{waypoint}";

        /// <summary>The seconds a CRUISE flight between two waypoints of a system takes; 0 to stay put, or where a position isn't known.</summary>
        private static double FlightSeconds(WayChart chart, string from, string to, int speed)
        {
            if (from.Equals(to, StringComparison.OrdinalIgnoreCase) || !chart.TryGetWaypoint(from, out var a) || !chart.TryGetWaypoint(to, out var b))
            {
                return 0;
            }

            var units = Math.Round(Math.Max(1, Math.Sqrt(Squared(b.X - a.X, b.Y - a.Y))), MidpointRounding.AwayFromZero);
            return SecondsPerFlight + (units * FlightMultiplier / Math.Max(1, speed));
        }

        /// <summary>The cooldown a jump between two systems starts (<see cref="TradeGates.CooldownBaseSeconds"/>).</summary>
        private static double CooldownSeconds(WayChart chart, string fromSystem, string toSystem)
            => chart.TryGetPosition(fromSystem, out var from) && chart.TryGetPosition(toSystem, out var to)
                ? TradeGates.CooldownBaseSeconds + (TradeGates.CooldownSecondsPerUnit * Exploring.Warps.Distance(from, to))
                : TradeGates.CooldownBaseSeconds;

        /// <summary>The jumps from the node's system: to the gates its built gate connects to, once it is explored.</summary>
        private void JumpsFrom(string key, Node node, Label label)
        {
            if (!_bySystem.TryGetValue(node.SystemSymbol, out var from)
                || from.ExploredAt is null
                || !ExploreAtlas.IsUsable(from, _chart.Now))
            {
                return;
            }

            var flight = FlightSeconds(_chart, node.WaypointSymbol, from.GateWaypointSymbol, _ship.Speed);
            var seconds = Math.Max(label.Cooldown, flight) + TripTime.StopSeconds;
            foreach (var gate in from.Connections ?? [])
            {
                if (!_byGate.TryGetValue(gate, out var to)
                    || to.SystemSymbol.Equals(from.SystemSymbol, StringComparison.OrdinalIgnoreCase)
                    || !ExploreAtlas.IsUsable(to, _chart.Now))
                {
                    continue;
                }

                Relax(
                    key,
                    new Node(to.SystemSymbol, to.GateWaypointSymbol, End: false),
                    new Label(
                        label.Seconds + seconds,
                        label.Fuel,
                        CooldownSeconds(_chart, from.SystemSymbol, to.SystemSymbol),
                        key,
                        new WayStep(WayStepKind.Jump, from.GateWaypointSymbol, to.GateWaypointSymbol, string.Empty, 0, seconds)));
            }
        }

        /// <summary>
        /// The warps from the node's system, for a ship with a warp drive: from where it is, its tank filled first where it can
        /// refuel there; or from its system's nearest market, flown to and refuelled at first, where it can't.
        /// </summary>
        private void WarpsFrom(string key, Node node, Label label)
        {
            if (_ship.WarpRange <= 0 || _ship.FuelCapacity <= 0 || !_chart.TryGetPosition(node.SystemSymbol, out var here))
            {
                return;
            }

            var departures = new List<(string Waypoint, int Fuel, double Seconds, bool Refuels)>();
            if (_chart.TryGetWaypoint(node.WaypointSymbol, out var at) && at.Refuels)
            {
                departures.Add((node.WaypointSymbol, _ship.FuelCapacity, label.Fuel < _ship.FuelCapacity ? TripTime.StopSeconds : 0, true));
            }
            else
            {
                departures.Add((node.WaypointSymbol, label.Fuel, 0, false));
                var market = _chart.WaypointsOf(node.SystemSymbol)
                    .Where(waypoint => waypoint.Refuels)
                    .OrderBy(waypoint => FlightSeconds(_chart, node.WaypointSymbol, waypoint.Symbol, _ship.Speed))
                    .ThenBy(waypoint => waypoint.Symbol, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (market is not null)
                {
                    departures.Add((market.Symbol, _ship.FuelCapacity, FlightSeconds(_chart, node.WaypointSymbol, market.Symbol, _ship.Speed) + TripTime.StopSeconds, true));
                }
            }

            foreach (var system in _chart.Systems)
            {
                if (system.Equals(node.SystemSymbol, StringComparison.OrdinalIgnoreCase)
                    || _chart.WarpRefused(system)
                    || !_chart.TryGetPosition(system, out var there)
                    || !Landing(system, out var landing))
                {
                    continue;
                }

                var distance = Exploring.Warps.Distance(here, there);
                if (distance > _ship.WarpRange)
                {
                    continue;
                }

                // Into a system with nowhere to refuel, it keeps the fuel to warp back to a market it left from (D100).
                var keep = landing.Refuels ? 0 : Exploring.Warps.Fuel("CRUISE", distance);
                foreach (var departure in departures.Where(departure => landing.Refuels || departure.Refuels))
                {
                    if (!Exploring.Warps.TryChooseMode(distance, departure.Fuel, keep, out var mode))
                    {
                        continue;
                    }

                    var fuel = Exploring.Warps.Fuel(mode, distance);
                    var seconds = departure.Seconds + Exploring.Warps.Seconds(mode, distance, _ship.Speed);
                    Relax(
                        key,
                        new Node(system, landing.Symbol, End: !landing.Refuels),
                        new Label(
                            label.Seconds + seconds,
                            departure.Fuel - fuel,
                            Math.Max(0, label.Cooldown - seconds),
                            key,
                            new WayStep(WayStepKind.Warp, departure.Waypoint, landing.Symbol, mode, fuel, seconds)));
                }
            }
        }

        /// <summary>Where a warp into the system lands (<see cref="TryFindLanding"/>), worked out once a search.</summary>
        private bool Landing(string system, out WayPoint landing)
        {
            if (!_landings.TryGetValue(system, out var known))
            {
                known = (TryFindLanding(_chart, system, out var found), found);
                _landings[system] = known;
            }

            landing = known.Landing;
            return known.Found;
        }

        private void Relax(string from, Node node, Label label)
        {
            var key = Key(node.SystemSymbol, node.WaypointSymbol);
            if (_settled.Contains(key) || (_labels.TryGetValue(key, out var known) && known.Seconds <= label.Seconds))
            {
                return;
            }

            _nodes[key] = node;
            _labels[key] = label with { Previous = from };
            _open.Enqueue(key, label.Seconds);
        }

        /// <summary>The steps from the ship to a node, in order.</summary>
        private SystemWay Way(string key)
        {
            var steps = new List<WayStep>();
            for (var at = key; _labels[at].Previous.Length > 0; at = _labels[at].Previous)
            {
                steps.Add(_labels[at].Step);
            }

            steps.Reverse();
            return new SystemWay(steps, _labels[key].Seconds);
        }
    }
}
