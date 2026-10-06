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
}

/// <summary>The explore plan's next step: a jump from the gate the ship is in front of, or why there is none.</summary>
public sealed record ExploreStep
{
    /// <summary>What to do.</summary>
    public required ExploreStepKind Kind { get; init; }

    /// <summary>For a jump: the gate of the system the ship is in.</summary>
    public string GateWaypointSymbol { get; init; } = string.Empty;

    /// <summary>For a jump: the gate to jump to.</summary>
    public string DestinationGateWaypointSymbol { get; init; } = string.Empty;

    /// <summary>The system the jumps lead to: the one to explore, or home.</summary>
    public string TargetSystemSymbol { get; init; } = string.Empty;

    /// <summary>How many jumps away that system is.</summary>
    public int Jumps { get; init; }
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
/// yet, by jumps through active gates, those within the trade reach of home first (slice 6.30, D103), and home once none is
/// left. A jump needs both gates built, and the plan leaves a gate the API refused a jump to alone for an hour
/// (<see cref="RecheckAfter"/>).
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
    /// within 5 jumps are explored before going further", with "Reach first, then nearest" and "The trade reach" (D103): a
    /// system within <paramref name="reach"/> jumps of home comes before every one beyond it, the nearest to the ship first in
    /// each.
    /// </summary>
    /// <param name="state">What the plan knows.</param>
    /// <param name="hereSystem">The system the ship is in.</param>
    /// <param name="now">The time to judge by.</param>
    /// <param name="taken">The systems the other exploring ships explore or are on their way to; none when null.</param>
    /// <param name="reach">The jumps from home within which a system comes first (<c>Trade.MaxHaulDistance</c>); 0 for none.</param>
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

        var open = order.Where(system => system.ExploredAt is null && taken?.Contains(system.SystemSymbol) != true).ToList();
        var fromHome = reach > 0 && open.Count > 0 ? Reachable(state, state.HomeSystemSymbol, now) : new Dictionary<string, int>();
        var target = open.FirstOrDefault(system => fromHome.TryGetValue(system.SystemSymbol, out var jumps) && jumps <= reach)
            ?? open.FirstOrDefault();
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
    /// How many systems are left to explore (slice 6.30, D102): those the plan knows, hasn't explored and reaches from home
    /// through usable gates (<see cref="IsUsable"/>). A system whose gate is under construction, or was refused within the
    /// hour, doesn't count until a ship can jump there.
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
