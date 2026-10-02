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
///   sells there. Within a supply level, the most a single siphon is expected to fetch, then the nearest gas
///   giant. No survey comes first: a siphon takes none;</item>
///   <item>only gas giants a ship can reach count, through refuelling stops, and only the markets it can carry
///   its hold to from there.</item>
/// </list>
/// </summary>
public static class SiphonPlanner
{
    /// <summary>
    /// What a siphoner can siphon, best first (D28 for gases): for every market that buys a gas, siphoned at the
    /// gas giant nearest the market and sold there. The markets shortest of their gas come first; within a supply
    /// level, the most a single siphon is expected to fetch (the gas's share of the giant's gases times its price),
    /// then the nearest gas giant. Openings other siphoners hold are left out: one siphoner per sell market and
    /// gas (<see cref="MiningPlanner.OpportunityKey"/>). The siphon plan buys a drone only when its first trip here
    /// would serve a market short of its gas.
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

        var reaches = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool Reaches(string gasGiant)
        {
            if (!reaches.TryGetValue(gasGiant, out var reachable))
            {
                reachable = MiningPlanner.CanReach(map, siphoner, gasGiant);
                reaches[gasGiant] = reachable;
            }

            return reachable;
        }

        var targets = new List<SiphonTarget>();
        foreach (var market in map.MarketWaypoints.Order(StringComparer.Ordinal))
        {
            foreach (var good in map.GoodsAt(market).Where(good => GasGiants.Gases.Contains(good.Symbol) && MiningPlanner.IsDemanded(good)))
            {
                if (!heldKeys.Contains(MiningPlanner.OpportunityKey(market, good.Symbol))
                    && TryFindNearestGasGiant(map, good.Symbol, market, gasGiant => Reaches(gasGiant) && MiningPlanner.CanSellFrom(map, siphoner, gasGiant, market), out var nearest))
                {
                    targets.Add(new SiphonTarget(good.Symbol, nearest, market, good.SellPrice, Share(map, nearest), good.Supply));
                }
            }
        }

        var position = MiningPlanner.Position(siphoner);
        return [.. targets
            .OrderBy(target => MiningPlanner.SupplyRank(target.Supply))
            .ThenByDescending(target => target.ExpectedValue)
            .ThenBy(target => map.TryGetDistance(position, target.GasGiantSymbol, out var distance) ? distance : double.MaxValue)
            .ThenBy(target => target.Key, StringComparer.Ordinal)];
    }

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
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SiphonTarget(string Gas, string GasGiantSymbol, string SellWaypointSymbol, long SellPrice, double Share, string Supply)
    {
        this.Gas = Gas;
        this.GasGiantSymbol = GasGiantSymbol;
        this.SellWaypointSymbol = SellWaypointSymbol;
        this.SellPrice = SellPrice;
        this.Share = Share;
        this.Supply = Supply;
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
