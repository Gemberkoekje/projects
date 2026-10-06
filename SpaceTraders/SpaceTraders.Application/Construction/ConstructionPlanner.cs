using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Construction;

/// <summary>
/// The construction arithmetic (PLAN.md slice 6.6), without any I/O, so the construction plan and its executor decide
/// alike. Asked on 2026-10-04: "Finishing this jump node should be top priority, as it opens up the rest of the game."
/// Supplying a construction site pays nothing (the API's supply call answers with the site and the ship's cargo, without
/// credits), so every load is spent for good:
/// <list type="bullet">
///   <item>a load is one material: a full hold, or what the site still needs when that is less, bought at one market whose
///   supply of it isn't SCARCE or LIMITED (D66), in batches of the market's trade volume, the most one purchase takes (D81,
///   which replaced D67's one purchase: "Full hold in batches"); then carried to the site through refuelling stops, in
///   CRUISE. Each batch raises the price, so its cost is estimated batch by batch (<see cref="PriceSteps"/>);</item>
///   <item>of the materials the site still needs, the one it has the smallest share of comes first, so the markets that
///   sell them get to recover in turn; of the markets, the one where the load costs least with its fuel;</item>
///   <item>whether the money allows it is the plan's to judge: a load keeps the credit reserve, as a ship purchase does,
///   and comes after the cargo ships in the order ships are bought in (D64).</item>
/// </list>
/// </summary>
public static class ConstructionPlanner
{
    /// <summary>The waypoint type of a jump gate, the construction site the plan builds.</summary>
    public const string JumpGateType = "JUMP_GATE";

    /// <summary>No market the ship can reach sells a material the site needs.</summary>
    public const string NoMarket = "no_market";

    /// <summary>Every market that sells a material the site needs has it SCARCE or LIMITED (D66).</summary>
    public const string LowSupply = "low_supply";

    /// <summary>Every market that would sell a load has another trip on its way to buy the material there (D80).</summary>
    public const string MarketBusy = "market_busy";

    /// <summary>The system of a waypoint: its symbol up to the last dash.</summary>
    /// <param name="waypointSymbol">The waypoint, such as <c>X1-DC53-I55</c>.</param>
    /// <returns>The system, such as <c>X1-DC53</c>.</returns>
    public static string SystemOf(string waypointSymbol)
    {
        ArgumentNullException.ThrowIfNull(waypointSymbol);
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }

    /// <summary>Whether a site still needs materials: it isn't complete, and a material has fewer units than it needs.</summary>
    /// <param name="site">The site as last seen.</param>
    /// <returns>True while there is something to supply.</returns>
    public static bool NeedsMaterials(ConstructionSiteModel site)
    {
        ArgumentNullException.ThrowIfNull(site);
        return !site.IsComplete && site.Materials.Any(material => material.Fulfilled < material.Required);
    }

    /// <summary>
    /// The materials a site still needs, with what our construction trips to it carry or go to buy: those units are on
    /// their way, and no other trip should buy them again.
    /// </summary>
    /// <param name="site">The site as last seen.</param>
    /// <param name="trips">The fleet's construction trips; blocked and done ones carry nothing.</param>
    /// <returns>One need per material short of what the site needs, in the site's order.</returns>
    public static IReadOnlyList<MaterialNeed> Needs(ConstructionSiteModel site, IEnumerable<SupplyConstructionGoal> trips)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(trips);

        var onTheWay = trips
            .Where(trip => trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed
                && trip.ConstructionSiteWaypointSymbol.Equals(site.WaypointSymbol, StringComparison.OrdinalIgnoreCase))
            .GroupBy(trip => trip.TradeSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(trip => Math.Max(0, trip.Units)), StringComparer.OrdinalIgnoreCase);
        return [.. site.Materials
            .Where(material => material.Fulfilled < material.Required)
            .Select(material => new MaterialNeed(material.TradeSymbol, material.Required, material.Fulfilled, onTheWay.GetValueOrDefault(material.TradeSymbol)))];
    }

    /// <summary>
    /// For a ship that holds materials the site still needs: the one it can supply most of, and how many units, at most
    /// what the site still needs.
    /// </summary>
    /// <param name="ship">The ship, with its hold.</param>
    /// <param name="needs">What the site still needs (<see cref="Needs"/>).</param>
    /// <param name="tradeSymbol">The material to supply.</param>
    /// <param name="units">The units to supply.</param>
    /// <returns>False when the ship holds nothing the site still needs.</returns>
    public static bool TryFindDelivery(ShipModel ship, IReadOnlyList<MaterialNeed> needs, out string tradeSymbol, out int units)
    {
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(needs);

        var deliveries = (ship.CargoInventory ?? [])
            .Where(item => item.Units > 0)
            .Select(item => (item.Symbol, Units: Math.Min(item.Units, needs.FirstOrDefault(need => need.TradeSymbol.Equals(item.Symbol, StringComparison.OrdinalIgnoreCase))?.Remaining ?? 0)))
            .Where(candidate => candidate.Units > 0)
            .OrderByDescending(candidate => candidate.Units)
            .ThenBy(candidate => candidate.Symbol, StringComparer.Ordinal)
            .ToList();
        tradeSymbol = deliveries.Count > 0 ? deliveries[0].Symbol : string.Empty;
        units = deliveries.Count > 0 ? deliveries[0].Units : 0;
        return deliveries.Count > 0;
    }

    /// <summary>
    /// The loads a ship could take for a site, the first material first: for each material the site still needs, the
    /// smallest share of what it needs first, the market where a load costs least with its fuel (to the market, and on to
    /// the site). A load is the ship's free hold, or what the site still needs when that is less, bought in batches of the
    /// market's trade volume (D81).
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now, with its hold.</param>
    /// <param name="siteWaypointSymbol">The construction site.</param>
    /// <param name="needs">What the site still needs (<see cref="Needs"/>).</param>
    /// <param name="strict">
    /// True for the loads the ship may buy now: the market's supply isn't SCARCE or LIMITED (D66). False for what it would buy
    /// once it isn't: what the credits are saved up for.
    /// </param>
    /// <returns>At most one load per material; none when the ship has no free hold.</returns>
    public static IReadOnlyList<ConstructionLoad> Loads(
        TradeMarketMap map,
        ShipModel ship,
        string siteWaypointSymbol,
        IReadOnlyList<MaterialNeed> needs,
        bool strict = true)
        => Loads(map, ship, siteWaypointSymbol, needs, HeldBuys.None, strict);

    /// <summary>
    /// The loads a ship could take for a site, as <see cref="Loads(TradeMarketMap, ShipModel, string, IReadOnlyList{MaterialNeed}, bool)"/>
    /// gives them; when strict, not at a market where another trip is on its way to buy the material: one buyer at a time
    /// (D80).
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now, with its hold.</param>
    /// <param name="siteWaypointSymbol">The construction site.</param>
    /// <param name="needs">What the site still needs (<see cref="Needs"/>).</param>
    /// <param name="heldBuys">The goods the trade and construction trips on their way to buy hold at their markets.</param>
    /// <param name="strict">True for the loads the ship may buy now; false for what the credits are saved up for.</param>
    /// <returns>At most one load per material; none when the ship has no free hold.</returns>
    public static IReadOnlyList<ConstructionLoad> Loads(
        TradeMarketMap map,
        ShipModel ship,
        string siteWaypointSymbol,
        IReadOnlyList<MaterialNeed> needs,
        HeldBuys heldBuys,
        bool strict = true)
    {
        ArgumentNullException.ThrowIfNull(heldBuys);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(needs);

        var free = ship.CargoCapacity - ship.CargoCurrent;
        var loads = new List<ConstructionLoad>();
        if (free <= 0)
        {
            return loads;
        }

        foreach (var need in needs.Where(need => need.Remaining > 0).OrderBy(need => need.Share).ThenBy(need => need.TradeSymbol, StringComparer.Ordinal))
        {
            var units = Math.Min(free, need.Remaining);
            ConstructionLoad? best = null;
            foreach (var market in map.MarketWaypoints.Order(StringComparer.Ordinal))
            {
                if (TryLoad(map, ship, siteWaypointSymbol, need.TradeSymbol, market, units, !heldBuys.Holds(need.TradeSymbol, market), strict, out var load)
                    && (best is null || load.Cost < best.Cost))
                {
                    best = load;
                }
            }

            if (best is not null)
            {
                loads.Add(best);
            }
        }

        return loads;
    }

    /// <summary>
    /// Why a ship has no load it may buy now, though it has room: no market it can reach sells what the site needs
    /// (<see cref="NoMarket"/>), or every one has it SCARCE or LIMITED (<see cref="LowSupply"/>, D66). Empty when it has a load.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now, with its hold.</param>
    /// <param name="siteWaypointSymbol">The construction site.</param>
    /// <param name="needs">What the site still needs (<see cref="Needs"/>).</param>
    /// <returns>The reason, or an empty string.</returns>
    public static string WhyNoLoad(TradeMarketMap map, ShipModel ship, string siteWaypointSymbol, IReadOnlyList<MaterialNeed> needs)
        => WhyNoLoad(map, ship, siteWaypointSymbol, needs, HeldBuys.None);

    /// <summary>
    /// Why a ship has no load it may buy now, as <see cref="WhyNoLoad(TradeMarketMap, ShipModel, string, IReadOnlyList{MaterialNeed})"/>
    /// says, or <see cref="MarketBusy"/> when every market that would sell one has another trip on its way to buy the material
    /// there (D80).
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now, with its hold.</param>
    /// <param name="siteWaypointSymbol">The construction site.</param>
    /// <param name="needs">What the site still needs (<see cref="Needs"/>).</param>
    /// <param name="heldBuys">The goods the trade and construction trips on their way to buy hold at their markets.</param>
    /// <returns>The reason, or an empty string.</returns>
    public static string WhyNoLoad(TradeMarketMap map, ShipModel ship, string siteWaypointSymbol, IReadOnlyList<MaterialNeed> needs, HeldBuys heldBuys)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(needs);

        if (Loads(map, ship, siteWaypointSymbol, needs, heldBuys).Count > 0)
        {
            return string.Empty;
        }

        if (Loads(map, ship, siteWaypointSymbol, needs, strict: false).Count == 0)
        {
            return NoMarket;
        }

        return Loads(map, ship, siteWaypointSymbol, needs).Count > 0 ? MarketBusy : LowSupply;
    }

    /// <summary>
    /// The ships that build when the role board is off (D65's rule, D93): every ship that can (<see cref="FleetRoles.CanConstruct"/>),
    /// the largest holds first, then the one that can do least else, then by symbol; only the first <paramref name="count"/>
    /// when it is above 0.
    /// </summary>
    /// <param name="candidates">The ships that may build: the caller leaves out those that survey.</param>
    /// <param name="count">The most that build (<c>Construction.Ships</c>); 0 for all of them.</param>
    /// <returns>The builders.</returns>
    public static IReadOnlyList<ShipModel> PickBuilders(IEnumerable<ShipModel> candidates, int count)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var largestFirst = candidates
            .Where(FleetRoles.CanConstruct)
            .OrderByDescending(ship => ship.CargoCapacity)
            .ThenBy(ship => FleetRoles.PotentialRoles(ship).Count)
            .ThenBy(ship => ship.Symbol, StringComparer.Ordinal);
        return [.. count > 0 ? largestFirst.Take(count) : largestFirst];
    }

    /// <summary>
    /// One load at one market, when the ship can fly it and, when strict, the market's supply allows it and no other trip is on
    /// its way to buy the material there.
    /// </summary>
    private static bool TryLoad(
        TradeMarketMap map,
        ShipModel ship,
        string siteWaypointSymbol,
        string tradeSymbol,
        string market,
        int units,
        bool unheld,
        bool strict,
        out ConstructionLoad load)
    {
        load = new ConstructionLoad(tradeSymbol, market, siteWaypointSymbol, units, 0, 0);
        if (!map.TryGetGood(market, tradeSymbol, out var good)
            || good.PurchasePrice <= 0
            || (strict && (!unheld || MiningPlanner.IsLowSupply(good.Supply)))
            || !TradeRoutePlanner.TryPlanFlight(map, ship, market, out var approach))
        {
            return false;
        }

        // The ship docks at the market to buy, and fills its tank there when it sells fuel.
        var fuelAtMarket = map.SellsFuel(market) ? ship.FuelCapacity : approach.FuelLeft;
        if (!TradeRoutePlanner.TryPlanFlight(map, market, siteWaypointSymbol, fuelAtMarket, ship.FuelCapacity, out var haul))
        {
            return false;
        }

        load = new ConstructionLoad(tradeSymbol, market, siteWaypointSymbol, units, good.PurchasePrice, approach.FuelCost + haul.FuelCost)
        {
            CargoCost = PriceSteps.CostInBatches(good.PurchasePrice, units, good.TradeVolume),
        };
        return true;
    }
}

/// <summary>One material a construction site still needs (<see cref="ConstructionPlanner.Needs"/>).</summary>
public sealed record MaterialNeed
{
    /// <summary>Creates a need.</summary>
    /// <param name="TradeSymbol">The material.</param>
    /// <param name="Required">The units the site needs in all.</param>
    /// <param name="Fulfilled">The units supplied so far.</param>
    /// <param name="OnTheWay">The units our construction trips carry or go to buy.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MaterialNeed(string TradeSymbol, int Required, int Fulfilled, int OnTheWay)
    {
        this.TradeSymbol = TradeSymbol;
        this.Required = Required;
        this.Fulfilled = Fulfilled;
        this.OnTheWay = OnTheWay;
    }

    /// <summary>The material.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>The units the site needs in all.</summary>
    public required int Required { get; init; }

    /// <summary>The units supplied so far.</summary>
    public required int Fulfilled { get; init; }

    /// <summary>The units our construction trips carry or go to buy.</summary>
    public required int OnTheWay { get; init; }

    /// <summary>The units no trip carries yet: what is left to buy.</summary>
    public int Remaining => Math.Max(0, Required - Fulfilled - OnTheWay);

    /// <summary>The share of what the site needs that is supplied or on its way, 0 to 1.</summary>
    public double Share => Required <= 0 ? 1 : Math.Min(1, (double)(Fulfilled + OnTheWay) / Required);
}

/// <summary>One load of materials: bought at a market, carried to the construction site (<see cref="ConstructionPlanner.Loads"/>).</summary>
public sealed record ConstructionLoad
{
    /// <summary>Creates a load.</summary>
    /// <param name="TradeSymbol">The material.</param>
    /// <param name="BuyWaypointSymbol">The market it is bought at.</param>
    /// <param name="SiteWaypointSymbol">The construction site it is carried to.</param>
    /// <param name="Units">The units, bought in batches of the market's trade volume (D81).</param>
    /// <param name="UnitPrice">What a unit costs at the market, as last seen: the first batch's price.</param>
    /// <param name="FuelCost">The fuel to the market and on to the site.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ConstructionLoad(string TradeSymbol, string BuyWaypointSymbol, string SiteWaypointSymbol, int Units, long UnitPrice, long FuelCost)
    {
        this.TradeSymbol = TradeSymbol;
        this.BuyWaypointSymbol = BuyWaypointSymbol;
        this.SiteWaypointSymbol = SiteWaypointSymbol;
        this.Units = Units;
        this.UnitPrice = UnitPrice;
        this.FuelCost = FuelCost;
        CargoCost = Units * UnitPrice;
    }

    /// <summary>The material.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>The market it is bought at.</summary>
    public required string BuyWaypointSymbol { get; init; }

    /// <summary>The construction site it is carried to.</summary>
    public required string SiteWaypointSymbol { get; init; }

    /// <summary>The units, bought in batches of the market's trade volume (D81).</summary>
    public required int Units { get; init; }

    /// <summary>What a unit costs at the market, as last seen: the first batch's price.</summary>
    public required long UnitPrice { get; init; }

    /// <summary>The fuel to the market and on to the site.</summary>
    public required long FuelCost { get; init; }

    /// <summary>
    /// What the cargo is expected to cost, each batch a step dearer than the last (<see cref="PriceSteps"/>); the units at the
    /// first batch's price unless set. The credits the trip holds back until it buys (D57, D64).
    /// </summary>
    public long CargoCost { get; init; }

    /// <summary>What the load costs with its fuel.</summary>
    public long Cost => CargoCost + FuelCost;
}
