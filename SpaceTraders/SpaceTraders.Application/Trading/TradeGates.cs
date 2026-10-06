using System.Collections.Concurrent;
using SpaceTraders.Application.Exploring;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// The ways between the systems of a trade map (PLAN.md slice 6.29, D96, D101): the jumps through built gates the explore plan
/// knows, the fewest first (<see cref="ExploreAtlas.TryFindJumps"/>), at most <see cref="MaxJumps"/> of them; what a jump costs,
/// one ANTIMATTER at the market of the gate it leaves from (D63); how long its cooldown holds the ship before the next jump; and
/// the credits every jump must leave (D63). <see cref="None"/> has no ways: a map of one system.
/// </summary>
/// <remarks>
/// Immutable once built, like the map it belongs to; the ways it finds are kept, as one pass asks for the same few many times.
/// </remarks>
public sealed class TradeGates
{
    /// <summary>
    /// The cooldown a jump starts at its least, in seconds. Fitted on 2026-10-06 to SPECTER-1's twelve jumps of that day, from
    /// 400 to 2,221 apart: each next flight came 17 seconds plus 0.311 a unit of the systems' distance after the jump, within six
    /// seconds, and the explore plan waits out the cooldown before that flight.
    /// </summary>
    public const double CooldownBaseSeconds = 17;

    /// <summary>The cooldown's seconds for each unit of distance between the two systems (<see cref="CooldownBaseSeconds"/>).</summary>
    public const double CooldownSecondsPerUnit = 0.311;

    private static readonly ExplorePlanState NoNetwork = new()
    {
        ShipSymbol = string.Empty,
        HomeSystemSymbol = string.Empty,
        Status = ExploreStatus.Waiting,
        UpdatedAt = DateTimeOffset.MinValue,
    };

    private readonly ExplorePlanState _network;
    private readonly DateTimeOffset _now;
    private readonly IReadOnlyDictionary<string, long> _antimatter;
    private readonly IReadOnlyDictionary<string, (int X, int Y)> _systems;
    private readonly long _averageAntimatter;
    private readonly ConcurrentDictionary<string, Way> _ways = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the ways between systems.</summary>
    /// <param name="network">The explore plan's gates, with the jumps refused lately (<see cref="IGateNetwork"/>).</param>
    /// <param name="now">The time to judge the gates by.</param>
    /// <param name="maxJumps">The most jumps a way may take (<c>Trade.MaxHaulDistance</c>, D96).</param>
    /// <param name="antimatterPrices">What a unit of ANTIMATTER costs at each gate's market, as last seen, by the gate's waypoint.</param>
    /// <param name="systemPositions">Where each system lies, by symbol: a jump's cooldown grows with the distance.</param>
    /// <param name="creditFloor">The credits a jump must leave (<c>FleetExpansion.MinCreditReserve</c>, D63).</param>
    public TradeGates(
        ExplorePlanState network,
        DateTimeOffset now,
        int maxJumps,
        IReadOnlyDictionary<string, long> antimatterPrices,
        IReadOnlyDictionary<string, (int X, int Y)> systemPositions,
        long creditFloor)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(antimatterPrices);
        ArgumentNullException.ThrowIfNull(systemPositions);

        _network = network;
        _now = now;
        MaxJumps = Math.Max(0, maxJumps);
        _antimatter = antimatterPrices;
        _systems = systemPositions;
        CreditFloor = Math.Max(0, creditFloor);
        var known = antimatterPrices.Values.Where(price => price > 0).ToList();
        _averageAntimatter = known.Count == 0 ? 0 : (long)Math.Ceiling(known.Average());
    }

    /// <summary>No ways between systems: a trade map of one system, as every plan but trading reads it (D60, D96).</summary>
    public static TradeGates None { get; } = new(
        NoNetwork,
        DateTimeOffset.MinValue,
        0,
        new Dictionary<string, long>(),
        new Dictionary<string, (int X, int Y)>(),
        0);

    /// <summary>The most jumps a way may take (<c>Trade.MaxHaulDistance</c>, D96); 0 for none.</summary>
    public int MaxJumps { get; }

    /// <summary>The credits a jump must leave (D63): a trip that jumps keeps them besides its antimatter.</summary>
    public long CreditFloor { get; }

    /// <summary>When the gates were judged: a ship's cooldown is counted from then.</summary>
    public DateTimeOffset Now => _now;

    /// <summary>
    /// The way from one system to another by jumps (D101): the fewest, through usable gates (<see cref="ExploreAtlas.TryFindJumps"/>),
    /// and no more than <see cref="MaxJumps"/>.
    /// </summary>
    /// <param name="fromSystem">The system the ship is in.</param>
    /// <param name="toSystem">The system it is going to.</param>
    /// <param name="jumps">The jumps, in order; none when it is in that system already.</param>
    /// <returns>False when no such way is known.</returns>
    public bool TryFindWay(string fromSystem, string toSystem, out IReadOnlyList<GateJump> jumps)
    {
        ArgumentNullException.ThrowIfNull(fromSystem);
        ArgumentNullException.ThrowIfNull(toSystem);

        var way = _ways.GetOrAdd($"{fromSystem}>{toSystem}", _ =>
            ExploreAtlas.TryFindJumps(_network, fromSystem, toSystem, _now, out var found) && found.Count <= MaxJumps
                ? new Way(true, found)
                : new Way(false, []));
        jumps = way.Jumps;
        return way.Found;
    }

    /// <summary>
    /// What a jump from a gate costs: one ANTIMATTER at its market, as last seen; where that was never seen, the average of the
    /// gates whose price is known, else 0.
    /// </summary>
    /// <param name="gateWaypointSymbol">The gate the ship jumps from.</param>
    /// <returns>The credits.</returns>
    public long AntimatterAt(string gateWaypointSymbol)
        => _antimatter.TryGetValue(gateWaypointSymbol, out var price) && price > 0 ? price : _averageAntimatter;

    /// <summary>
    /// How long a jump between two systems holds the ship before it can jump again: its cooldown, estimated from the systems'
    /// distance (<see cref="CooldownBaseSeconds"/>). The ship can fly on meanwhile; only a jump waits for it.
    /// </summary>
    /// <param name="fromSystem">The system it jumps from.</param>
    /// <param name="toSystem">The system it jumps to.</param>
    /// <returns>The seconds; the least a cooldown takes where a system's position isn't known.</returns>
    public double CooldownSeconds(string fromSystem, string toSystem)
    {
        ArgumentNullException.ThrowIfNull(fromSystem);
        ArgumentNullException.ThrowIfNull(toSystem);

        if (!_systems.TryGetValue(fromSystem, out var from) || !_systems.TryGetValue(toSystem, out var to))
        {
            return CooldownBaseSeconds;
        }

        var dx = (double)to.X - from.X;
        var dy = (double)to.Y - from.Y;
        return CooldownBaseSeconds + (CooldownSecondsPerUnit * Math.Sqrt((dx * dx) + (dy * dy)));
    }

    /// <summary>A way between two systems, or none.</summary>
    private sealed record Way(bool Found, IReadOnlyList<GateJump> Jumps);
}
