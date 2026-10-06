using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Exploring;

/// <summary>A waypoint a way between systems flies from, lands at or refuels at (PLAN.md slice 6.31).</summary>
public sealed record WayPoint
{
    /// <summary>Creates a waypoint.</summary>
    /// <param name="Symbol">The waypoint.</param>
    /// <param name="SystemSymbol">Its system.</param>
    /// <param name="X">Where it lies in its system, across.</param>
    /// <param name="Y">Where it lies in its system, up.</param>
    /// <param name="Refuels">Whether a ship can refuel there (<see cref="CanRefuel"/>).</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public WayPoint(string Symbol, string SystemSymbol, int X, int Y, bool Refuels)
    {
        this.Symbol = Symbol;
        this.SystemSymbol = SystemSymbol;
        this.X = X;
        this.Y = Y;
        this.Refuels = Refuels;
    }

    /// <summary>The waypoint.</summary>
    public required string Symbol { get; init; }

    /// <summary>Its system.</summary>
    public required string SystemSymbol { get; init; }

    /// <summary>Where it lies in its system, across.</summary>
    public required int X { get; init; }

    /// <summary>Where it lies in its system, up.</summary>
    public required int Y { get; init; }

    /// <summary>Whether a ship can refuel there (<see cref="CanRefuel"/>).</summary>
    public required bool Refuels { get; init; }

    /// <summary>
    /// Whether a ship can refuel at a waypoint, as cached: it has a market, and every market seen sells FUEL (248 of 248 on
    /// 2026-10-06, the gates' and the fuel stations' among them); or it is a FUEL_STATION, whose type shows even where nobody
    /// has charted it and its market is hidden.
    /// </summary>
    /// <param name="waypoint">The waypoint, as cached.</param>
    /// <returns>True where a ship can refuel.</returns>
    public static bool CanRefuel(WaypointCacheModel waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        return waypoint.HasMarket || waypoint.Type.Equals("FUEL_STATION", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A cached waypoint as a way reads it.</summary>
    /// <param name="waypoint">The waypoint, as cached.</param>
    /// <returns>The way's waypoint.</returns>
    public static WayPoint Of(WaypointCacheModel waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        return new WayPoint(waypoint.Symbol, waypoint.SystemSymbol, waypoint.X, waypoint.Y, CanRefuel(waypoint));
    }
}

/// <summary>
/// What the ways between systems are planned on (PLAN.md slice 6.31, D101: "One planner for every way"): the explore plan's
/// gates, with the jumps refused lately (<see cref="IGateNetwork"/>); where each system lies, from the system cache, which keeps
/// the systems fetched and those a scan found; the cached waypoints of those systems, with where a ship can refuel; and the
/// systems the API refused a warp into lately (<see cref="WarpRefusals"/>). <see cref="SystemWays"/> finds the ways on it.
/// </summary>
public sealed class WayChart
{
    private readonly IReadOnlyDictionary<string, (int X, int Y)> _systems;
    private readonly Dictionary<string, WayPoint> _waypoints;
    private readonly Dictionary<string, IReadOnlyList<WayPoint>> _bySystem;
    private readonly IReadOnlySet<string> _warpsRefused;

    /// <summary>Creates a chart.</summary>
    /// <param name="network">The explore plan's gates, with the jumps refused lately.</param>
    /// <param name="now">The time to judge the gates and the refusals by.</param>
    /// <param name="systems">Where each system lies, by symbol.</param>
    /// <param name="waypoints">The cached waypoints of those systems.</param>
    /// <param name="warpsRefused">The systems the API refused a warp into lately.</param>
    public WayChart(
        ExplorePlanState network,
        DateTimeOffset now,
        IReadOnlyDictionary<string, (int X, int Y)> systems,
        IEnumerable<WayPoint> waypoints,
        IReadOnlySet<string> warpsRefused)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentNullException.ThrowIfNull(warpsRefused);

        Network = network;
        Now = now;
        _systems = systems;
        _waypoints = waypoints
            .GroupBy(waypoint => waypoint.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _bySystem = _waypoints.Values
            .GroupBy(waypoint => waypoint.SystemSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<WayPoint>)[.. group.OrderBy(waypoint => waypoint.Symbol, StringComparer.Ordinal)],
                StringComparer.OrdinalIgnoreCase);
        _warpsRefused = warpsRefused;
    }

    /// <summary>The explore plan's gates, with the jumps refused lately.</summary>
    public ExplorePlanState Network { get; }

    /// <summary>The time the gates and the refusals are judged by.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>The systems whose position is known, by symbol.</summary>
    public IEnumerable<string> Systems => _systems.Keys.Order(StringComparer.Ordinal);

    /// <summary>Reads the chart from the cache.</summary>
    /// <param name="network">The explore plan's gates, with the jumps refused lately.</param>
    /// <param name="systems">The system cache: where each system lies.</param>
    /// <param name="waypoints">The waypoint cache.</param>
    /// <param name="refusals">The warps the API refused lately.</param>
    /// <param name="now">The time to judge by.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The chart.</returns>
    public static async Task<WayChart> ReadAsync(
        ExplorePlanState network,
        ISystemRepository systems,
        IWaypointRepository waypoints,
        WarpRefusals refusals,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var positions = (await systems.GetAllAsync(cancellationToken))
            .GroupBy(system => system.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (group.First().X, group.First().Y), StringComparer.OrdinalIgnoreCase);
        var cached = (await waypoints.GetVisitedSystemSymbolsAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var points = new List<WayPoint>();
        foreach (var system in positions.Keys.Where(cached.Contains).Order(StringComparer.Ordinal))
        {
            points.AddRange((await waypoints.GetBySystemAsync(system, cancellationToken)).Select(WayPoint.Of));
        }

        return new WayChart(network, now, positions, points, refusals.Refused(now));
    }

    /// <summary>Where a system lies.</summary>
    /// <param name="systemSymbol">The system.</param>
    /// <param name="position">Its position.</param>
    /// <returns>False when it isn't known.</returns>
    public bool TryGetPosition(string systemSymbol, out (int X, int Y) position)
        => _systems.TryGetValue(systemSymbol, out position);

    /// <summary>A cached waypoint of a system whose position is known.</summary>
    /// <param name="waypointSymbol">The waypoint.</param>
    /// <param name="waypoint">The waypoint, as the chart has it.</param>
    /// <returns>False when the chart doesn't have it.</returns>
    public bool TryGetWaypoint(string waypointSymbol, out WayPoint waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypointSymbol);
        if (_waypoints.TryGetValue(waypointSymbol, out var found))
        {
            waypoint = found;
            return true;
        }

        waypoint = new WayPoint(waypointSymbol, string.Empty, 0, 0, Refuels: false);
        return false;
    }

    /// <summary>The cached waypoints of a system, by symbol; none while its waypoints were never fetched.</summary>
    /// <param name="systemSymbol">The system.</param>
    /// <returns>The waypoints.</returns>
    public IReadOnlyList<WayPoint> WaypointsOf(string systemSymbol)
        => _bySystem.TryGetValue(systemSymbol, out var waypoints) ? waypoints : [];

    /// <summary>Whether the API refused a warp into the system lately (<see cref="WarpRefusals"/>).</summary>
    /// <param name="systemSymbol">The system.</param>
    /// <returns>True for a system no warp goes to for now.</returns>
    public bool WarpRefused(string systemSymbol) => _warpsRefused.Contains(systemSymbol);
}
