namespace SpaceTraders.Application.Exploring;

/// <summary>What the explore plan's next step for the command ship is.</summary>
public enum ExploreStepKind
{
    /// <summary>Something that could change the choice isn't known yet (a gate's state, a gate's connections): wait for it.</summary>
    Wait = 0,

    /// <summary>Jump towards the nearest system not explored yet.</summary>
    Explore = 1,

    /// <summary>Nothing reachable is left to explore: jump towards home.</summary>
    ReturnHome = 2,

    /// <summary>Nothing reachable is left to explore, and the ship is home.</summary>
    Home = 3,

    /// <summary>Nothing reachable is left to explore, and no active gate leads home from here.</summary>
    NoWayHome = 4,

    /// <summary>
    /// Nothing is left for a ship with a warp drive to explore, and it is in a system the gates don't reach from home: it goes
    /// to the nearest one they do (PLAN.md slice 6.31), by a warp, so that it isn't left where the traders can't go.
    /// </summary>
    Rejoin = 5,

    /// <summary>Nothing is left for a ship with a warp drive to explore, and it is in a system the gates reach from home (slice 6.31).</summary>
    NothingLeft = 6,
}

/// <summary>
/// The explore plan's next step: a jump from the gate the ship is in front of, or, for a ship with a warp drive, a warp
/// (PLAN.md slice 6.31); or why there is none.
/// </summary>
public sealed record ExploreStep
{
    /// <summary>What to do.</summary>
    public required ExploreStepKind Kind { get; init; }

    /// <summary>For a jump: the gate of the system the ship is in.</summary>
    public string GateWaypointSymbol { get; init; } = string.Empty;

    /// <summary>For a jump: the gate to jump to.</summary>
    public string DestinationGateWaypointSymbol { get; init; } = string.Empty;

    /// <summary>For a warp (slice 6.31): the waypoint of the next system it lands at; empty for a jump.</summary>
    public string WarpWaypointSymbol { get; init; } = string.Empty;

    /// <summary>The system the jumps lead to: the one to explore, or home.</summary>
    public string TargetSystemSymbol { get; init; } = string.Empty;

    /// <summary>How many jumps away that system is.</summary>
    public int Jumps { get; init; }

    /// <summary>For a ship with a warp drive (slice 6.31): how many warps its way to that system takes.</summary>
    public int Warps { get; init; }

    /// <summary>For a ship with a warp drive (slice 6.31): the seconds its way to that system takes, as reckoned.</summary>
    public double Seconds { get; init; }
}

/// <summary>One jump of a way between systems (PLAN.md slice 6.28, D101): from a system's gate to a gate it connects to.</summary>
public sealed record GateJump
{
    /// <summary>Creates a jump.</summary>
    /// <param name="GateWaypointSymbol">The gate the ship jumps from, in the system it is in.</param>
    /// <param name="DestinationGateWaypointSymbol">The gate it jumps to, in the next system.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GateJump(string GateWaypointSymbol, string DestinationGateWaypointSymbol)
    {
        this.GateWaypointSymbol = GateWaypointSymbol;
        this.DestinationGateWaypointSymbol = DestinationGateWaypointSymbol;
    }

    /// <summary>The gate the ship jumps from, in the system it is in.</summary>
    public required string GateWaypointSymbol { get; init; }

    /// <summary>The gate it jumps to, in the next system.</summary>
    public required string DestinationGateWaypointSymbol { get; init; }
}

/// <summary>
/// Where the explore plan goes next (asked on 2026-10-04), from what it knows of the gates: the nearest system not explored
/// yet, by jumps through active gates, ring by ring around home (slice 6.33, D114: the trade reach's width each, <see cref="Ring"/>),
/// and home once none is left. A jump needs both gates built, and the plan leaves a gate the API refused a jump to alone for an
/// hour (<see cref="RecheckAfter"/>).
/// </summary>
public static class ExploreAtlas
{
    /// <summary>How long a gate under construction, or one the API refused a jump to, waits before it is looked at again.</summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromHours(1);

    /// <summary>How long a look that failed waits before it is tried again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    /// <summary>Whether ships can jump to and from the system's gate now: it is built, and wasn't refused within the hour.</summary>
    /// <param name="system">The system.</param>
    /// <param name="now">The time to judge by.</param>
    /// <returns>True for a usable gate.</returns>
    public static bool IsUsable(KnownSystem system, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(system);
        return system.Gate == GateState.Active
            && system.GateWaypointSymbol.Length > 0
            && (system.JumpRefusedAt is null || now - system.JumpRefusedAt.Value >= RecheckAfter);
    }

    /// <summary>
    /// An exploring ship's next step from <paramref name="hereSystem"/>, which the plan has explored already: the nearest system
    /// not explored yet that no other exploring ship has taken (slice 6.30), else home. Asked on 2026-10-06: "first the systems
    /// within 5 jumps are explored before going further", with "Reach first, then nearest" and "The trade reach" (D103); then on
    /// 2026-10-07 (slice 6.33, D114): "I'd like exploring done in concentric circles based on trade distance. So first the first 5
    /// systems as is currently the case, then 6-10, then 11-15 etc.": a system in a nearer ring around home
    /// (<paramref name="reach"/> jumps wide, <see cref="Ring"/>) comes before every one in a farther ring, the nearest to the
    /// ship first in each.
    /// </summary>
    /// <param name="state">What the plan knows.</param>
    /// <param name="hereSystem">The system the ship is in.</param>
    /// <param name="now">The time to judge by.</param>
    /// <param name="taken">The systems the other exploring ships explore or are on their way to; none when null.</param>
    /// <param name="reach">How many jumps from home each ring spans (<c>Trade.MaxHaulDistance</c>); 0 for no rings, the nearest first.</param>
    /// <returns>The step.</returns>
    public static ExploreStep Next(ExplorePlanState state, string hereSystem, DateTimeOffset now, IReadOnlySet<string>? taken = null, int reach = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(hereSystem);

        var atlas = new Atlas(state, now);
        if (!atlas.BySystem.ContainsKey(hereSystem))
        {
            return new ExploreStep { Kind = ExploreStepKind.Wait };
        }

        var (parents, order) = atlas.Search(hereSystem);
        if (order.Any(system => atlas.IsCharting(system)))
        {
            return new ExploreStep { Kind = ExploreStepKind.Wait };
        }

        // The ring first; within a ring the search's order, the nearest to the ship first (OrderBy keeps it).
        var open = order.Where(system => system.ExploredAt is null && taken?.Contains(system.SystemSymbol) != true).ToList();
        var fromHome = reach > 0 && open.Count > 0 ? JumpsFromHome(state, now) : new Dictionary<string, int>();
        var target = open.OrderBy(system => Ring(fromHome, system.SystemSymbol, reach)).FirstOrDefault();
        if (target is not null)
        {
            return Towards(atlas, parents, hereSystem, target.SystemSymbol, ExploreStepKind.Explore);
        }

        return Home(atlas, parents, state.HomeSystemSymbol, hereSystem);
    }

    /// <summary>
    /// The command ship's way home from <paramref name="hereSystem"/> (slice 6.30, D98): once an explorer explores, it comes
    /// home, whatever is left to explore.
    /// </summary>
    /// <param name="state">What the plan knows.</param>
    /// <param name="hereSystem">The system the ship is in.</param>
    /// <param name="now">The time to judge the gates by.</param>
    /// <returns><see cref="ExploreStepKind.Home"/>, a jump towards home, or why there is none.</returns>
    public static ExploreStep HomeFrom(ExplorePlanState state, string hereSystem, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(hereSystem);

        var atlas = new Atlas(state, now);
        if (hereSystem.Equals(state.HomeSystemSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return new ExploreStep { Kind = ExploreStepKind.Home, TargetSystemSymbol = state.HomeSystemSymbol };
        }

        if (!atlas.BySystem.ContainsKey(hereSystem))
        {
            return new ExploreStep { Kind = ExploreStepKind.Wait };
        }

        var (parents, _) = atlas.Search(hereSystem);
        return Home(atlas, parents, state.HomeSystemSymbol, hereSystem);
    }

    /// <summary>
    /// The next step of an exploring ship with a warp drive (PLAN.md slice 6.31, D100, D101, D106): the nearest system not
    /// explored yet that no other exploring ship has taken, by the fastest way, through the gates or by warps
    /// (<see cref="SystemWays"/>). Asked on 2026-10-06, "Reach first, then nearest" (D106), and on 2026-10-07 (slice 6.33, D114)
    /// in rings of the trade reach around home (<see cref="Ring"/>), with "Ring of their gate": a system behind a gate still under
    /// construction counts the jumps through that gate, and one no known gate leads to, found by a scan, comes after every ring;
    /// within a ring the nearest by the seconds its way takes, whether jumps or warps get there. It waits while something that
    /// could change the choice isn't
    /// known yet: a gate it reaches that was never looked at (as <see cref="Next"/>), or the waypoints of a system only a warp
    /// reaches, within a warp of an explored system, never fetched (<see cref="WarpLooks"/>): until they are, nobody knows where
    /// a ship could refuel there. With nothing left, a ship in a system the gates don't reach from home goes to the nearest one
    /// they do (<see cref="ExploreStepKind.Rejoin"/>); one where they do has <see cref="ExploreStepKind.NothingLeft"/>.
    /// </summary>
    /// <param name="state">What the plan knows.</param>
    /// <param name="chart">The gates, the systems' positions and their waypoints.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="now">The time to judge by.</param>
    /// <param name="taken">The systems the other exploring ships explore or are on their way to; none when null.</param>
    /// <param name="reach">How many jumps from home each ring spans (<c>Trade.MaxHaulDistance</c>); 0 for no rings, the nearest first.</param>
    /// <returns>The step.</returns>
    public static ExploreStep NextByWays(ExplorePlanState state, WayChart chart, WayShip ship, DateTimeOffset now, IReadOnlySet<string>? taken = null, int reach = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(ship);

        var atlas = new Atlas(state, now);
        var here = ship.SystemSymbol;
        if (atlas.BySystem.ContainsKey(here) && atlas.Search(here).Order.Any(atlas.IsCharting))
        {
            return new ExploreStep { Kind = ExploreStepKind.Wait };
        }

        var positions = chart.Systems.ToDictionary(system => system, system => chart.TryGetPosition(system, out var at) ? at : default, StringComparer.OrdinalIgnoreCase);
        var fetched = chart.Systems.Where(system => chart.WaypointsOf(system).Count > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (WarpLooks(state, positions, fetched, [(here, ship.MaxWarp)], now).Any(system => atlas.BySystem.TryGetValue(system, out var known) && known.WaypointsCheckedAt is null))
        {
            return new ExploreStep { Kind = ExploreStepKind.Wait };
        }

        var ways = SystemWays.From(chart, ship);
        var fromHome = Reachable(state, state.HomeSystemSymbol, now);
        var rings = reach > 0 ? JumpsFromHome(state, now) : new Dictionary<string, int>();
        var target = state.Systems
            .Where(system => system.ExploredAt is null && taken?.Contains(system.SystemSymbol) != true && ways.ContainsKey(system.SystemSymbol))
            .OrderBy(system => Ring(rings, system.SystemSymbol, reach))
            .ThenBy(system => ways[system.SystemSymbol].Seconds)
            .ThenBy(system => system.SystemSymbol, StringComparer.Ordinal)
            .FirstOrDefault();
        if (target is not null)
        {
            return Along(ways[target.SystemSymbol], target.SystemSymbol, ExploreStepKind.Explore);
        }

        if (fromHome.ContainsKey(here))
        {
            return new ExploreStep { Kind = ExploreStepKind.NothingLeft, TargetSystemSymbol = here };
        }

        var back = ways
            .Where(way => fromHome.ContainsKey(way.Key))
            .OrderBy(way => way.Value.Seconds)
            .ThenBy(way => way.Key, StringComparer.Ordinal)
            .Select(way => way.Key)
            .FirstOrDefault();
        return back is null
            ? new ExploreStep { Kind = ExploreStepKind.NoWayHome, TargetSystemSymbol = state.HomeSystemSymbol }
            : Along(ways[back], back, ExploreStepKind.Rejoin);
    }

    /// <summary>
    /// The systems only a warp reaches whose waypoints the explore plan is still to fetch (PLAN.md slice 6.31), the most needed
    /// first: known, not explored, not reached from home through usable gates, no waypoint cached and never fetched, and never
    /// asked for, or asked for at least <see cref="RetryAfter"/> ago without an answer; within the farthest warp of the ships
    /// with a warp drive from an explored system
    /// or from where such a ship is. A system whose position isn't known comes first: the plan knows it only from a gate's
    /// connections (behind a gate under construction), so it lies next to a system the gates reach.
    /// </summary>
    /// <param name="state">What the plan knows.</param>
    /// <param name="positions">Where each system lies, by symbol, as cached.</param>
    /// <param name="fetched">The systems whose waypoints are cached.</param>
    /// <param name="ships">The ships with a warp drive: the system each is in, and the farthest it warps (<see cref="WayShip.MaxWarp"/>).</param>
    /// <param name="now">The time to judge by.</param>
    /// <returns>The systems, the nearest first.</returns>
    public static IReadOnlyList<string> WarpLooks(
        ExplorePlanState state,
        IReadOnlyDictionary<string, (int X, int Y)> positions,
        IReadOnlySet<string> fetched,
        IReadOnlyCollection<(string SystemSymbol, int MaxWarp)> ships,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(fetched);
        ArgumentNullException.ThrowIfNull(ships);

        var maxWarp = ships.Count == 0 ? 0 : ships.Max(ship => ship.MaxWarp);
        if (maxWarp <= 0)
        {
            return [];
        }

        var fromHome = Reachable(state, state.HomeSystemSymbol, now);
        List<(int X, int Y)> origins =
        [
            .. state.Systems.Where(system => system.ExploredAt is not null).Select(system => system.SystemSymbol)
                .Concat(ships.Select(ship => ship.SystemSymbol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(positions.ContainsKey)
                .Select(system => positions[system]),
        ];
        return
        [
            .. state.Systems
                .Where(system => system.ExploredAt is null
                    && !fromHome.ContainsKey(system.SystemSymbol)
                    && !fetched.Contains(system.SystemSymbol)
                    && system.WaypointsFetchedAt is null
                    && (system.WaypointsCheckedAt is null || now - system.WaypointsCheckedAt.Value >= RetryAfter))
                .Select(system => (system.SystemSymbol, Distance: !positions.TryGetValue(system.SystemSymbol, out var at)
                    ? 0
                    : origins.Count == 0 ? double.PositiveInfinity : origins.Min(origin => Warps.Distance(origin, at))))
                .Where(entry => entry.Distance <= maxWarp)
                .OrderBy(entry => entry.Distance)
                .ThenBy(entry => entry.SystemSymbol, StringComparer.Ordinal)
                .Select(entry => entry.SystemSymbol),
        ];
    }

    /// <summary>
    /// How many systems are left to explore (slice 6.30, D102): those the plan knows, hasn't explored and reaches from home
    /// through usable gates (<see cref="IsUsable"/>). A system whose gate is under construction, or was refused within the
    /// hour, doesn't count until a ship can jump there. Asked on 2026-10-06 (slice 6.31, D107), "No, gates only": a system only
    /// a warp reaches doesn't count either.
    /// </summary>
    /// <param name="state">What the plan knows.</param>
    /// <param name="now">The time to judge the gates by.</param>
    /// <returns>The systems left.</returns>
    public static int SystemsLeft(ExplorePlanState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        var atlas = new Atlas(state, now);
        if (!atlas.BySystem.ContainsKey(state.HomeSystemSymbol))
        {
            return 0;
        }

        var (_, order) = atlas.Search(state.HomeSystemSymbol);
        return order.Count(system => system.ExploredAt is null);
    }

    /// <summary>
    /// The ring around home a system lies in (slice 6.33, D114), asked on 2026-10-07: "I'd like exploring done in concentric
    /// circles based on trade distance. So first the first 5 systems as is currently the case, then 6-10, then 11-15 etc.": ring
    /// 1 for 1 to <paramref name="reach"/> jumps from home, ring 2 for the next <paramref name="reach"/>, and so on; home is ring
    /// 0. The jumps are those <see cref="JumpsFromHome"/> counts, with "Ring of their gate": one more to a system whose gate is
    /// still under construction, which only a warp reaches for now. A system no known gate leads to, found by a scan, lies beyond
    /// every ring.
    /// </summary>
    /// <param name="jumpsFromHome">The jumps from home, by system (<see cref="JumpsFromHome"/>).</param>
    /// <param name="systemSymbol">The system.</param>
    /// <param name="reach">How many jumps each ring spans (<c>Trade.MaxHaulDistance</c>); 0 or less puts every system in ring 0.</param>
    /// <returns>The ring; <see cref="int.MaxValue"/> beyond every ring.</returns>
    public static int Ring(IReadOnlyDictionary<string, int> jumpsFromHome, string systemSymbol, int reach)
    {
        ArgumentNullException.ThrowIfNull(jumpsFromHome);
        ArgumentNullException.ThrowIfNull(systemSymbol);

        if (reach <= 0)
        {
            return 0;
        }

        return jumpsFromHome.TryGetValue(systemSymbol, out var jumps) ? (jumps + reach - 1) / reach : int.MaxValue;
    }

    private static ExploreStep Home(Atlas atlas, IReadOnlyDictionary<string, string> parents, string home, string hereSystem)
    {
        if (hereSystem.Equals(home, StringComparison.OrdinalIgnoreCase))
        {
            return new ExploreStep { Kind = ExploreStepKind.Home, TargetSystemSymbol = home };
        }

        return parents.ContainsKey(home)
            ? Towards(atlas, parents, hereSystem, home, ExploreStepKind.ReturnHome)
            : new ExploreStep { Kind = ExploreStepKind.NoWayHome, TargetSystemSymbol = home };
    }

    /// <summary>
    /// How many jumps each known system is from home: through built gates, and one more to a system whose gate is still
    /// under construction, or not known yet. A system no known gate leads to isn't listed.
    /// </summary>
    /// <param name="state">What the plan knows.</param>
    /// <param name="now">The time to judge by.</param>
    /// <returns>The jumps, by system.</returns>
    public static IReadOnlyDictionary<string, int> JumpsFromHome(ExplorePlanState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        var atlas = new Atlas(state, now);
        var jumps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!atlas.BySystem.TryGetValue(state.HomeSystemSymbol, out var home))
        {
            return jumps;
        }

        jumps[home.SystemSymbol] = 0;
        var queue = new Queue<KnownSystem>([home]);
        while (queue.Count > 0)
        {
            var from = queue.Dequeue();
            if (!IsUsable(from, now))
            {
                continue;
            }

            foreach (var to in atlas.Connected(from))
            {
                if (jumps.TryAdd(to.SystemSymbol, jumps[from.SystemSymbol] + 1))
                {
                    queue.Enqueue(to);
                }
            }
        }

        return jumps;
    }

    /// <summary>
    /// The systems a ship can get to from <paramref name="fromSystem"/> by jumps (PLAN.md slice 6.28, D101), with the fewest
    /// jumps to each: through usable gates only (<see cref="IsUsable"/>: built at both ends, and not refused within the hour),
    /// and on through explored systems only, whose connections are known. <paramref name="fromSystem"/> itself is at 0; a
    /// system the plan doesn't know reaches nothing.
    /// </summary>
    /// <param name="state">What the plan knows, with the refusals of the other ships' jumps (<see cref="JumpRefusals"/>).</param>
    /// <param name="fromSystem">The system to start from.</param>
    /// <param name="now">The time to judge the gates by.</param>
    /// <returns>The jumps, by system.</returns>
    public static IReadOnlyDictionary<string, int> Reachable(ExplorePlanState state, string fromSystem, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fromSystem);

        var jumps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [fromSystem] = 0 };
        var atlas = new Atlas(state, now);
        if (!atlas.BySystem.ContainsKey(fromSystem))
        {
            return jumps;
        }

        // Breadth first, so a system's predecessor is counted before it.
        var (parents, order) = atlas.Search(fromSystem);
        foreach (var system in order.Where(system => !system.SystemSymbol.Equals(fromSystem, StringComparison.OrdinalIgnoreCase)))
        {
            jumps[system.SystemSymbol] = jumps[parents[system.SystemSymbol]] + 1;
        }

        return jumps;
    }

    /// <summary>
    /// The way from one system to another by jumps (PLAN.md slice 6.28, D101): the fewest, through the gates
    /// <see cref="Reachable"/> goes through, the nearest first and by symbol within a distance, as the explore plan goes. Each
    /// jump is from the gate of the system the ship is in to a gate that gate connects to.
    /// </summary>
    /// <param name="state">What the plan knows, with the refusals of the other ships' jumps (<see cref="JumpRefusals"/>).</param>
    /// <param name="fromSystem">The system the ship is in.</param>
    /// <param name="toSystem">The system it is going to.</param>
    /// <param name="now">The time to judge the gates by.</param>
    /// <param name="jumps">The jumps, in order; none when the ship is in that system already.</param>
    /// <returns>False when no way through usable gates is known.</returns>
    public static bool TryFindJumps(ExplorePlanState state, string fromSystem, string toSystem, DateTimeOffset now, out IReadOnlyList<GateJump> jumps)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fromSystem);
        ArgumentNullException.ThrowIfNull(toSystem);

        jumps = [];
        if (fromSystem.Equals(toSystem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var atlas = new Atlas(state, now);
        if (!atlas.BySystem.ContainsKey(fromSystem))
        {
            return false;
        }

        var (parents, _) = atlas.Search(fromSystem);
        if (!parents.ContainsKey(toSystem))
        {
            return false;
        }

        var way = new List<GateJump>();
        for (var system = atlas.BySystem[toSystem].SystemSymbol; !system.Equals(fromSystem, StringComparison.OrdinalIgnoreCase); system = parents[system])
        {
            way.Add(new GateJump(atlas.BySystem[parents[system]].GateWaypointSymbol, atlas.BySystem[system].GateWaypointSymbol));
        }

        way.Reverse();
        jumps = way;
        return true;
    }

    /// <summary>The first step of a way between systems (slice 6.31), towards <paramref name="target"/>: a jump or a warp.</summary>
    private static ExploreStep Along(SystemWay way, string target, ExploreStepKind kind)
    {
        var first = way.Steps[0];
        return first.Kind == WayStepKind.Warp
            ? new ExploreStep { Kind = kind, WarpWaypointSymbol = first.ToWaypointSymbol, TargetSystemSymbol = target, Jumps = way.Jumps, Warps = way.Warps, Seconds = way.Seconds }
            : new ExploreStep
            {
                Kind = kind,
                GateWaypointSymbol = first.FromWaypointSymbol,
                DestinationGateWaypointSymbol = first.ToWaypointSymbol,
                TargetSystemSymbol = target,
                Jumps = way.Jumps,
                Warps = way.Warps,
                Seconds = way.Seconds,
            };
    }

    private static ExploreStep Towards(
        Atlas atlas,
        IReadOnlyDictionary<string, string> parents,
        string hereSystem,
        string targetSystem,
        ExploreStepKind kind)
    {
        // Walk back from the target to the system after here: that is the first jump.
        var jumps = 0;
        var next = targetSystem;
        while (!parents[next].Equals(hereSystem, StringComparison.OrdinalIgnoreCase))
        {
            next = parents[next];
            jumps++;
        }

        return new ExploreStep
        {
            Kind = kind,
            GateWaypointSymbol = atlas.BySystem[hereSystem].GateWaypointSymbol,
            DestinationGateWaypointSymbol = atlas.BySystem[next].GateWaypointSymbol,
            TargetSystemSymbol = targetSystem,
            Jumps = jumps + 1,
        };
    }

    /// <summary>The known systems, by system and by gate, as of one moment.</summary>
    private sealed class Atlas
    {
        private readonly DateTimeOffset _now;
        private readonly Dictionary<string, KnownSystem> _byGate;

        public Atlas(ExplorePlanState state, DateTimeOffset now)
        {
            _now = now;
            BySystem = state.Systems
                .GroupBy(system => system.SystemSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            _byGate = BySystem.Values
                .Where(system => system.GateWaypointSymbol.Length > 0)
                .GroupBy(system => system.GateWaypointSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, KnownSystem> BySystem { get; }

        /// <summary>The known systems a system's gate connects to, by symbol.</summary>
        public IEnumerable<KnownSystem> Connected(KnownSystem from)
            => (from.Connections ?? [])
                .Select(gate => _byGate.GetValueOrDefault(gate))
                .OfType<KnownSystem>()
                .OrderBy(system => system.SystemSymbol, StringComparer.Ordinal);

        /// <summary>
        /// Breadth first from a system, through usable gates only, nearest first and by symbol within a distance: each
        /// reached system's predecessor, and the order they were reached in.
        /// </summary>
        public (Dictionary<string, string> Parents, List<KnownSystem> Order) Search(string fromSystem)
        {
            var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [fromSystem] = string.Empty };
            var order = new List<KnownSystem>();
            var queue = new Queue<KnownSystem>([BySystem[fromSystem]]);
            while (queue.Count > 0)
            {
                var from = queue.Dequeue();
                order.Add(from);

                // An unexplored system is a destination: what lies beyond it is looked at once it is explored.
                if (from.ExploredAt is null || !IsUsable(from, _now))
                {
                    continue;
                }

                foreach (var to in Connected(from).Where(to => IsUsable(to, _now)))
                {
                    if (parents.TryAdd(to.SystemSymbol, from.SystemSymbol))
                    {
                        queue.Enqueue(to);
                    }
                }
            }

            return (parents, order);
        }

        /// <summary>
        /// Whether something about a reached, explored system that could change the choice was never looked at: its gate,
        /// its connections, or the gate of a system it connects to. A look that failed doesn't count: it is tried again later.
        /// </summary>
        public bool IsCharting(KnownSystem system)
        {
            if (system.ExploredAt is null)
            {
                return false;
            }

            if (system.Gate == GateState.Unknown && system.GateCheckedAt is null)
            {
                return true;
            }

            if (!IsUsable(system, _now))
            {
                return false;
            }

            return system.ConnectionsCheckedAt is null
                || (system.Connections ?? []).Any(gate => !_byGate.TryGetValue(gate, out var to)
                    || (to.Gate == GateState.Unknown && to.GateCheckedAt is null));
        }
    }
}
