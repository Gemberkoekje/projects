using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Siphoning;

/// <summary>
/// The siphon choices (PLAN.md slice 6.7): the miners' rules (<see cref="MiningPlanner"/>) for gases, without
/// any I/O, so the plan and its tests decide alike:
/// <list type="bullet">
///   <item>a siphoner serves the market shortest of a gas first (D28): SCARCE, then LIMITED (low supply, D22),
///   and once no market is short, the lowest supply there is. It siphons at the gas giant nearest the market and
///   sells there. Within a supply level, the targets in CRUISE reach first, then the most a single siphon is
///   expected to fetch, then the nearest gas giant. No survey comes first: a siphon takes none;</item>
///   <item>only trips a ship can make in CRUISE count, through refuelling stops: to the gas giant, and on to the
///   market with the fuel left there;</item>
///   <item>a market out of that reach that sells fuel counts too (slice 6.10c, D45): the ship drifts there first and
///   siphons from there, at a gas giant within a CRUISE round trip of it.</item>
/// </list>
/// </summary>
public static class SiphonPlanner
{
    /// <summary>
    /// What a siphoner can siphon, best first (D28 for gases): for every market that buys a gas, siphoned at the
    /// gas giant nearest the market and sold there. The markets shortest of their gas come first; within a supply
    /// level, the targets in CRUISE reach first, then the most a single siphon is expected to fetch (the gas's share of
    /// the giant's gases times its price), then the nearest gas giant. A market out of the siphoner's CRUISE reach that
    /// sells fuel is a far target (D45, <see cref="SiphonTarget.Far"/>): the siphoner drifts there first, and siphons at
    /// the gas giant nearest it within a CRUISE round trip. Openings other siphoners hold are left out: one siphoner per
    /// sell market and gas (<see cref="MiningPlanner.OpportunityKey"/>). The siphon plan buys a drone only when its first
    /// trip here would serve a market short of its gas.
    /// </summary>
    /// <param name="map">The siphoner's system.</param>
    /// <param name="siphoner">The siphoner.</param>
    /// <param name="heldKeys">The openings other siphoners hold.</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<SiphonTarget> SiphonTargets(TradeMarketMap map, ShipModel siphoner, IReadOnlySet<string> heldKeys)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(siphoner);
        ArgumentNullException.ThrowIfNull(heldKeys);

        var arrivals = new Dictionary<string, (bool Reached, int Fuel)>(StringComparer.OrdinalIgnoreCase);
        (bool Reached, int Fuel) ArrivalAt(string gasGiant)
        {
            if (!arrivals.TryGetValue(gasGiant, out var arrival))
            {
                arrival = MiningPlanner.Arrival(map, siphoner, gasGiant);
                arrivals[gasGiant] = arrival;
            }

            return arrival;
        }

        // The trip in CRUISE: to the gas giant, and on to the market with the fuel left there (slice 6.10c).
        bool Gathers(string gasGiant, string market)
            => ArrivalAt(gasGiant) is { Reached: true } arrival
                && TradeRoutePlanner.TryPlanFlight(map, gasGiant, market, arrival.Fuel, siphoner.FuelCapacity, out _);

        var targets = new List<SiphonTarget>();
        foreach (var market in map.MarketWaypoints.Order(StringComparer.Ordinal))
        {
            var gases = map.GoodsAt(market)
                .Where(good => GasGiants.Gases.Contains(good.Symbol)
                    && MiningPlanner.IsDemanded(good)
                    && !heldKeys.Contains(MiningPlanner.OpportunityKey(market, good.Symbol)))
                .ToList();
            var driftsThere = gases.Count > 0 && MiningPlanner.CanDriftTo(map, siphoner, market);
            foreach (var good in gases)
            {
                if (TryFindNearestGasGiant(map, good.Symbol, market, gasGiant => Gathers(gasGiant, market), out var nearest))
                {
                    targets.Add(new SiphonTarget(good.Symbol, nearest, market, good.SellPrice, Share(map, nearest), good.Supply));
                }
                else if (driftsThere
                    && TryFindNearestGasGiant(map, good.Symbol, market, gasGiant => MiningPlanner.IsWithinRoundTrip(map, siphoner, market, gasGiant), out var far))
                {
                    // Out of CRUISE reach (D45): the siphoner drifts to the market, which sells fuel, and siphons from there.
                    targets.Add(new SiphonTarget(good.Symbol, far, market, good.SellPrice, Share(map, far), good.Supply, Far: true));
                }
            }
        }

        var position = MiningPlanner.Position(siphoner);
        return [.. targets
            .OrderBy(target => MiningPlanner.SupplyRank(target.Supply))
            .ThenBy(target => target.Far)
            .ThenByDescending(target => target.ExpectedValue)
            .ThenBy(target => map.TryGetDistance(position, target.GasGiantSymbol, out var distance) ? distance : double.MaxValue)
            .ThenBy(target => target.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// What a siphoner can siphon, best first, with the gases no siphoner works on first (D48): of its SCARCE or LIMITED
    /// targets (D22) whose gas isn't in <paramref name="coveredGases"/>, the nearest gas giant first ("near before far");
    /// then the rest, in <see cref="SiphonTargets(TradeMarketMap, ShipModel, IReadOnlySet{string})"/>'s order (D28).
    /// </summary>
    /// <param name="map">The siphoner's system.</param>
    /// <param name="siphoner">The siphoner.</param>
    /// <param name="heldKeys">The openings other siphoners hold.</param>
    /// <param name="coveredGases">The gases a siphoner's trip works on.</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<SiphonTarget> SiphonTargets(TradeMarketMap map, ShipModel siphoner, IReadOnlySet<string> heldKeys, IReadOnlySet<string> coveredGases)
        => UncoveredFirst(map, siphoner, SiphonTargets(map, siphoner, heldKeys), coveredGases);

    /// <summary>
    /// Puts the targets whose gas no siphoner works on first (D48): the SCARCE or LIMITED ones (D22) whose gas isn't in
    /// <paramref name="coveredGases"/>, those in CRUISE reach before those a drift away (D45), the nearest gas giant first;
    /// then the rest, each group in the order given.
    /// </summary>
    /// <param name="map">The siphoner's system.</param>
    /// <param name="siphoner">The siphoner.</param>
    /// <param name="targets">Its targets, in D28's order (<see cref="SiphonTargets(TradeMarketMap, ShipModel, IReadOnlySet{string})"/>).</param>
    /// <param name="coveredGases">The gases a siphoner's trip works on.</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<SiphonTarget> UncoveredFirst(TradeMarketMap map, ShipModel siphoner, IReadOnlyList<SiphonTarget> targets, IReadOnlySet<string> coveredGases)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(coveredGases);

        var position = MiningPlanner.Position(siphoner);
        return [.. targets
            .Select((target, rank) => (Target: target, Rank: rank, Uncovered: target.LowSupply && !coveredGases.Contains(target.Gas)))
            .OrderByDescending(entry => entry.Uncovered)
            .ThenBy(entry => entry.Uncovered && entry.Target.Far)
            .ThenBy(entry => !entry.Uncovered ? 0 : map.TryGetDistance(position, entry.Target.GasGiantSymbol, out var distance) ? distance : double.MaxValue)
            .ThenBy(entry => entry.Rank)
            .Select(entry => entry.Target)];
    }

    /// <summary>
    /// The SCARCE or LIMITED gases a ship could serve (D48): the gases of its low-supply targets (D22), whichever siphoner
    /// holds them. A gas that no gas giant it can reach yields, or that no market it can carry it to is short of, isn't one.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, as it is or as it would be bought.</param>
    /// <returns>The gases, by symbol.</returns>
    public static IReadOnlySet<string> ScarceGases(TradeMarketMap map, ShipModel ship)
        => SiphonTargets(map, ship, new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            .Where(target => target.LowSupply)
            .Select(target => target.Gas)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The low-supply openings of a system (D22): each market with a gas in low supply, and the gas giant nearest
    /// it. A siphoner that can reach the gas giant can take the opening.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <returns>The openings, by market and gas.</returns>
    public static IReadOnlyList<SiphonOpportunity> LowSupplyOpportunities(TradeMarketMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var opportunities = new List<SiphonOpportunity>();
        foreach (var market in map.MarketWaypoints.Order(StringComparer.Ordinal))
        {
            foreach (var good in map.GoodsAt(market)
                .Where(good => GasGiants.Gases.Contains(good.Symbol) && MiningPlanner.IsLowSupply(good))
                .OrderBy(good => good.Symbol, StringComparer.Ordinal))
            {
                if (TryFindNearestGasGiant(map, good.Symbol, market, _ => true, out var gasGiant))
                {
                    opportunities.Add(new SiphonOpportunity(good.Symbol, gasGiant, market, good.SellPrice));
                }
            }
        }

        return opportunities;
    }

    /// <summary>The gas giant nearest a waypoint that yields the gas, among those <paramref name="allowed"/> lets through.</summary>
    private static bool TryFindNearestGasGiant(TradeMarketMap map, string gas, string near, Func<string, bool> allowed, out string gasGiant)
    {
        gasGiant = map.Waypoints
            .Where(waypoint => GasGiants.CanYield(waypoint, gas))
            .Select(waypoint => (waypoint.Symbol, Distance: map.TryGetDistance(near, waypoint.Symbol, out var distance) ? distance : double.MaxValue))
            .Where(candidate => candidate.Distance < double.MaxValue)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Symbol, StringComparer.Ordinal)
            .Select(candidate => candidate.Symbol)
            .FirstOrDefault(allowed) ?? string.Empty;
        return gasGiant.Length > 0;
    }

    /// <summary>A siphon yields any of the gas giant's gases, about equally often.</summary>
    private static double Share(TradeMarketMap map, string gasGiant)
    {
        var waypoint = map.Waypoints.FirstOrDefault(candidate => candidate.Symbol.Equals(gasGiant, StringComparison.OrdinalIgnoreCase));
        var gases = waypoint is null ? 0 : GasGiants.GasesAt(waypoint).Count;
        return gases == 0 ? 0 : 1.0 / gases;
    }
}

/// <summary>A siphon trip a siphoner could take: where it siphons which gas, and where it sells it.</summary>
public sealed record SiphonTarget
{
    /// <summary>Creates a siphon target.</summary>
    /// <param name="Gas">The gas.</param>
    /// <param name="GasGiantSymbol">Where it is siphoned.</param>
    /// <param name="SellWaypointSymbol">Where it is sold.</param>
    /// <param name="SellPrice">What that market pays per unit, as last seen.</param>
    /// <param name="Share">The share of siphons expected to yield the gas: one gas of the giant's.</param>
    /// <param name="Supply">The sell market's supply of the gas, as last seen (SCARCE to ABUNDANT).</param>
    /// <param name="Far">Whether the market is out of the siphoner's CRUISE reach, so the siphoner drifts there first (D45).</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SiphonTarget(string Gas, string GasGiantSymbol, string SellWaypointSymbol, long SellPrice, double Share, string Supply, bool Far = false)
    {
        this.Gas = Gas;
        this.GasGiantSymbol = GasGiantSymbol;
        this.SellWaypointSymbol = SellWaypointSymbol;
        this.SellPrice = SellPrice;
        this.Share = Share;
        this.Supply = Supply;
        this.Far = Far;
    }

    /// <summary>The gas.</summary>
    public required string Gas { get; init; }

    /// <summary>Where it is siphoned.</summary>
    public required string GasGiantSymbol { get; init; }

    /// <summary>Where it is sold.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>What that market pays per unit, as last seen.</summary>
    public required long SellPrice { get; init; }

    /// <summary>The share of siphons expected to yield the gas.</summary>
    public required double Share { get; init; }

    /// <summary>The sell market's supply of the gas, as last seen (SCARCE to ABUNDANT).</summary>
    public required string Supply { get; init; }

    /// <summary>
    /// Whether the market is out of the siphoner's CRUISE reach (D45): the siphoner drifts there first, 1 fuel whatever the
    /// distance and about ten times slower, refuels, and siphons from there in CRUISE.
    /// </summary>
    public bool Far { get; init; }

    /// <summary>Whether the sell market has the gas in low supply (D22): SCARCE or LIMITED.</summary>
    public bool LowSupply => MiningPlanner.IsLowSupply(Supply);

    /// <summary>What one siphon is expected to fetch per unit: the share times the price.</summary>
    public double ExpectedValue => Share * SellPrice;

    /// <summary>The opening's key: one siphoner per sell market and gas (<see cref="MiningPlanner.OpportunityKey"/>).</summary>
    public string Key => MiningPlanner.OpportunityKey(SellWaypointSymbol, Gas);
}

/// <summary>A market with a gas in low supply (D22), and the gas giant nearest it.</summary>
public sealed record SiphonOpportunity
{
    /// <summary>Creates an opening.</summary>
    /// <param name="Gas">The gas.</param>
    /// <param name="GasGiantSymbol">The gas giant nearest the market.</param>
    /// <param name="SellWaypointSymbol">The market.</param>
    /// <param name="SellPrice">What the market pays per unit, as last seen.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SiphonOpportunity(string Gas, string GasGiantSymbol, string SellWaypointSymbol, long SellPrice)
    {
        this.Gas = Gas;
        this.GasGiantSymbol = GasGiantSymbol;
        this.SellWaypointSymbol = SellWaypointSymbol;
        this.SellPrice = SellPrice;
    }

    /// <summary>The gas.</summary>
    public required string Gas { get; init; }

    /// <summary>The gas giant nearest the market.</summary>
    public required string GasGiantSymbol { get; init; }

    /// <summary>The market.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>What the market pays per unit, as last seen.</summary>
    public required long SellPrice { get; init; }

    /// <summary>The opening's key (<see cref="MiningPlanner.OpportunityKey"/>).</summary>
    public string Key => MiningPlanner.OpportunityKey(SellWaypointSymbol, Gas);
}
