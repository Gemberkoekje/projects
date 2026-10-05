using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// The trade arithmetic (PLAN.md slice 6.5), without any I/O, so the trading plan and the trade
/// executor decide alike:
/// <list type="bullet">
///   <item>a trip's profit is what the sell market pays minus what the buy market charges, per unit,
///   times the units, minus the fuel for the trip: from where the ship is to the buy market, and on to
///   the sell market;</item>
///   <item>a trip is lucrative when it earns at least <c>Trade.MinProfitPerUnit</c> per unit (D14);</item>
///   <item>among lucrative trips, one whose sell market makes a pricier good from the cargo comes first
///   (D15), then the most profitable.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// A trip is a full hold, bought in one purchase and sold in one sale (D56): a route counts only when both
/// markets' trade volumes, the most a single trade takes, are at least the ship's free hold, and the credits
/// pay for all of it. Each trade moves the price, so a hold bought in several purchases costs more per unit
/// than the first one. Where the buy market's supply of the good is ABUNDANT, a trip may fill less: what both
/// markets trade at once, still in one purchase and one sale (D74). Cargo may use the credit reserve (D17); the
/// trip's fuel is kept back.
/// </para>
/// <para>
/// Ships fly CRUISE, which burns one unit of fuel per unit of distance (at least 1). A ship docked at
/// a market that sells fuel fills its tank before it leaves (<c>NavigateToWaypointCommand</c>), in
/// whole market units of FUEL of 100 each, so the fuel of a leg is paid for at the market it ends at.
/// A flight longer than the tank holds stops to refuel at markets on the way
/// (<see cref="TryPlanFlight(TradeMarketMap, string, string, int, int, out TradeFlight)"/>): never DRIFT, which is ten times slower, and which the navigation
/// would keep using after its fallback.
/// </para>
/// </remarks>
public static class TradeRoutePlanner
{
    private const int FuelPerMarketUnit = 100;

    /// <summary>The supply at which a seller's trade volume may fill less than the hold (D74).</summary>
    private const string AbundantSupply = "ABUNDANT";

    /// <summary>The key that identifies a route: good, buy market and sell market.</summary>
    /// <param name="tradeSymbol">The good.</param>
    /// <param name="buyWaypointSymbol">The buy market.</param>
    /// <param name="sellWaypointSymbol">The sell market.</param>
    /// <returns>The key, in upper case.</returns>
    public static string RouteKey(string tradeSymbol, string buyWaypointSymbol, string sellWaypointSymbol)
        => $"{buyWaypointSymbol}|{sellWaypointSymbol}|{tradeSymbol}".ToUpperInvariant();

    /// <summary>
    /// The lucrative routes for a ship, best first: those that feed a pricier good's production, then
    /// by profit. Routes in <paramref name="heldRouteKeys"/> belong to other traders and are left out:
    /// two traders never share a route.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="credits">The credits on hand.</param>
    /// <param name="minProfitPerUnit">The profit per unit, after fuel, a trip must earn.</param>
    /// <param name="heldRouteKeys">The routes other traders hold (<see cref="RouteKey"/>).</param>
    /// <returns>The lucrative routes, best first; empty when there is none.</returns>
    public static IReadOnlyList<TradeRoute> Rank(
        TradeMarketMap map,
        ShipModel ship,
        long credits,
        int minProfitPerUnit,
        IReadOnlySet<string> heldRouteKeys)
    {
        var routes = new List<TradeRoute>();
        CheckRoutes(map, ship, credits, minProfitPerUnit, heldRouteKeys, routes, judgements: null);
        return [.. routes
            .OrderByDescending(route => route.FeedsProduction)
            .ThenByDescending(route => route.Profit)
            .ThenBy(route => route.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Every route with a price gap that no other trader holds, as <see cref="Rank"/> checks it for a ship: a market sells the
    /// good for less than another pays for it. Each says the first check it fails, in the order Rank runs them, or
    /// <see cref="TradeRouteCheck.Lucrative"/>: those are Rank's routes (slice 2.18, D76).
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="credits">The credits on hand.</param>
    /// <param name="minProfitPerUnit">The profit per unit, after fuel, a trip must earn.</param>
    /// <param name="heldRouteKeys">The routes other traders hold (<see cref="RouteKey"/>).</param>
    /// <returns>A judgement per route; empty when no market pays more for a good than another charges.</returns>
    public static IReadOnlyList<TradeRouteJudgement> Judge(
        TradeMarketMap map,
        ShipModel ship,
        long credits,
        int minProfitPerUnit,
        IReadOnlySet<string> heldRouteKeys)
    {
        var judgements = new List<TradeRouteJudgement>();
        CheckRoutes(map, ship, credits, minProfitPerUnit, heldRouteKeys, [], judgements);
        return judgements;
    }

    /// <summary>
    /// Orders two routes as <see cref="Rank"/> does (D15): one that feeds a pricier good's production
    /// first, then the more profitable, then by key.
    /// </summary>
    /// <param name="x">One route.</param>
    /// <param name="y">The other route.</param>
    /// <returns>Less than 0 when <paramref name="x"/> is the better route, more than 0 when <paramref name="y"/> is.</returns>
    public static int CompareBestFirst(TradeRoute x, TradeRoute y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        var feeds = y.FeedsProduction.CompareTo(x.FeedsProduction);
        if (feeds != 0)
        {
            return feeds;
        }

        var profit = y.Profit.CompareTo(x.Profit);
        return profit != 0 ? profit : string.CompareOrdinal(x.Key, y.Key);
    }

    /// <summary>
    /// Works out one route for a ship where it is now: the units, the fuel and the profit, at the
    /// prices last seen at both markets.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <param name="buyWaypointSymbol">The buy market.</param>
    /// <param name="sellWaypointSymbol">The sell market.</param>
    /// <param name="credits">The credits on hand.</param>
    /// <param name="route">The route's figures.</param>
    /// <returns>
    /// False when the ship can't fly it (a market, a price or a position unknown, no way to get there
    /// within its tank) or can't trade its units in one go (<see cref="UnitsAtOnce"/>: no free hold, a trade volume
    /// below it at a seller whose supply isn't ABUNDANT, too few credits for them).
    /// </returns>
    public static bool TryEvaluate(
        TradeMarketMap map,
        ShipModel ship,
        string tradeSymbol,
        string buyWaypointSymbol,
        string sellWaypointSymbol,
        long credits,
        out TradeRoute route)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);

        if (TryPlanFlight(map, ship.WaypointSymbol ?? string.Empty, buyWaypointSymbol, FuelAtDeparture(map, ship), ship.FuelCapacity, out var approach)
            && TryEvaluateFrom(map, ship, tradeSymbol, buyWaypointSymbol, sellWaypointSymbol, credits, approach, out route, out _))
        {
            return true;
        }

        route = new TradeRoute(tradeSymbol, buyWaypointSymbol, sellWaypointSymbol, 0, 0, 0, 0, 0, string.Empty);
        return false;
    }

    /// <summary>
    /// The units a trip of a good carries, as the markets were last seen, bought in one purchase and sold in one sale:
    /// <list type="bullet">
    ///   <item>the ship's whole free hold, when both markets' trade volumes take it at once (D56). Asked on 2026-10-03: "The
    ///   entire goal is to buy full holds in one go, because it makes no sense to buy more times than one.";</item>
    ///   <item>otherwise, where the buy market's supply of the good is ABUNDANT, what both markets trade at once (D74). Asked
    ///   on 2026-10-04: "either a full hold needs to be obtained, or the supply of the seller needs to be ABUNDANT, in which
    ///   case a full hold is not necessary";</item>
    ///   <item>otherwise none.</item>
    /// </list>
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <param name="buyWaypointSymbol">The buy market.</param>
    /// <param name="sellWaypointSymbol">The sell market.</param>
    /// <returns>The units; 0 when the trip takes none.</returns>
    public static int UnitsAtOnce(TradeMarketMap map, ShipModel ship, string tradeSymbol, string buyWaypointSymbol, string sellWaypointSymbol)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);

        var free = ship.CargoCapacity - ship.CargoCurrent;
        if (free <= 0
            || !map.TryGetGood(buyWaypointSymbol, tradeSymbol, out var atBuy)
            || !map.TryGetGood(sellWaypointSymbol, tradeSymbol, out var atSell))
        {
            return 0;
        }

        var atOnce = Math.Max(0, Math.Min(free, Math.Min(atBuy.TradeVolume, atSell.TradeVolume)));
        return atOnce == free || string.Equals(atBuy.Supply, AbundantSupply, StringComparison.OrdinalIgnoreCase) ? atOnce : 0;
    }

    /// <summary>
    /// How to fly from one waypoint to another in CRUISE: straight there when the fuel aboard will do,
    /// otherwise through markets that sell fuel, refuelling at each, every hop within a full tank. The
    /// fewest stops win, then the cheapest fuel: a stop costs a dock, a refuel, an orbit and a market
    /// refresh, more than the few credits another way could save. The cost is the fuel bought on
    /// arrival at each stop, the destination included.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="from">Where the flight starts.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="fuelAtStart">The fuel aboard on leaving <paramref name="from"/>.</param>
    /// <param name="fuelCapacity">What the tank holds, as filled at each stop.</param>
    /// <param name="flight">The stops, the destination last, and the fuel they cost.</param>
    /// <returns>False when a position is unknown, or no chain of fuel markets reaches the destination.</returns>
    public static bool TryPlanFlight(
        TradeMarketMap map,
        string from,
        string to,
        int fuelAtStart,
        int fuelCapacity,
        out TradeFlight flight)
    {
        ArgumentNullException.ThrowIfNull(map);

        flight = new TradeFlight([], 0, fuelAtStart);
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!map.TryGetDistance(from, to, out var straight))
        {
            return false;
        }

        var direct = CruiseFuel(from, to, straight);
        if (direct <= fuelAtStart)
        {
            flight = new TradeFlight([to], RefuelCost(map, direct, to), fuelAtStart - direct);
            return true;
        }

        // Dijkstra over the markets that sell fuel: fewest stops first, then the cheapest fuel.
        List<string> stations =
        [
            .. map.MarketWaypoints
                .Where(market => map.SellsFuel(market) && !market.Equals(from, StringComparison.OrdinalIgnoreCase))
                .Append(to)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal),
        ];
        var best = new Dictionary<string, Hop>(StringComparer.OrdinalIgnoreCase) { [from] = new Hop(0, 0, string.Empty, 0) };
        var settled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var open = best
                .Where(entry => !settled.Contains(entry.Key))
                .OrderBy(entry => entry.Value.Stops)
                .ThenBy(entry => entry.Value.Cost)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => entry.Key)
                .ToList();
            if (open.Count == 0)
            {
                return false;
            }

            var current = open[0];
            if (current.Equals(to, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            settled.Add(current);
            var range = current.Equals(from, StringComparison.OrdinalIgnoreCase) ? fuelAtStart : fuelCapacity;
            foreach (var next in stations.Where(station => !settled.Contains(station)))
            {
                if (!map.TryGetDistance(current, next, out var distance))
                {
                    continue;
                }

                var leg = CruiseFuel(current, next, distance);
                if (leg > range)
                {
                    continue;
                }

                var hop = new Hop(best[current].Cost + RefuelCost(map, leg, next), best[current].Stops + 1, current, leg);
                if (!best.TryGetValue(next, out var known)
                    || hop.Stops < known.Stops
                    || (hop.Stops == known.Stops && hop.Cost < known.Cost))
                {
                    best[next] = hop;
                }
            }
        }

        var stops = new List<string>();
        for (var stop = to; !stop.Equals(from, StringComparison.OrdinalIgnoreCase); stop = best[stop].Previous)
        {
            stops.Add(stop);
        }

        stops.Reverse();
        flight = new TradeFlight(stops, best[to].Cost, fuelCapacity - best[to].LastLeg);
        return true;
    }

    /// <summary>
    /// How a ship flies from where it is to a waypoint (<see cref="TryPlanFlight(TradeMarketMap, string, string, int, int, out TradeFlight)"/>):
    /// a ship docked where fuel is sold fills its tank before it leaves.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="destination">Where it is going.</param>
    /// <param name="flight">The stops, the destination last, and the fuel they cost.</param>
    /// <returns>False when a position is unknown, or no chain of fuel markets reaches the destination.</returns>
    public static bool TryPlanFlight(TradeMarketMap map, ShipModel ship, string destination, out TradeFlight flight)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);

        return TryPlanFlight(map, ship.WaypointSymbol ?? string.Empty, destination, FuelAtDeparture(map, ship), ship.FuelCapacity, out flight);
    }

    /// <summary>
    /// Where a ship flies next on its way to a waypoint: straight there when its fuel will do,
    /// otherwise the first refuelling stop (<see cref="TryPlanFlight(TradeMarketMap, string, string, int, int, out TradeFlight)"/>).
    /// When no flight is found, the waypoint itself, and the navigation does what it can.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="destination">Where it is going.</param>
    /// <returns>The waypoint to navigate to now.</returns>
    public static string NextStop(TradeMarketMap map, ShipModel ship, string destination)
        => TryPlanFlight(map, ship, destination, out var flight) && flight.Stops.Count > 0
            ? flight.Stops[0]
            : destination;

    /// <summary>
    /// Runs <see cref="Rank"/>'s checks on every route with a price gap that no other trader holds: the lucrative routes go to
    /// <paramref name="routes"/>, and with <paramref name="judgements"/> every route goes there too, with the first check it
    /// fails (<see cref="Judge"/>).
    /// </summary>
    private static void CheckRoutes(
        TradeMarketMap map,
        ShipModel ship,
        long credits,
        int minProfitPerUnit,
        IReadOnlySet<string> heldRouteKeys,
        List<TradeRoute> routes,
        List<TradeRouteJudgement>? judgements)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(heldRouteKeys);

        var here = ship.WaypointSymbol ?? string.Empty;
        var fuelAtStart = FuelAtDeparture(map, ship);
        foreach (var buy in map.MarketWaypoints)
        {
            var goods = map.GoodsAt(buy).Where(good => good.PurchasePrice > 0 && good.TradeVolume > 0).ToList();
            if (goods.Count == 0)
            {
                continue;
            }

            var reachable = TryPlanFlight(map, here, buy, fuelAtStart, ship.FuelCapacity, out var approach);
            if (!reachable && judgements is null)
            {
                continue;
            }

            foreach (var good in goods)
            {
                foreach (var sell in map.MarketWaypoints)
                {
                    if (sell.Equals(buy, StringComparison.OrdinalIgnoreCase)
                        || !map.TryGetGood(sell, good.Symbol, out var atSell)
                        || atSell.SellPrice <= good.PurchasePrice
                        || heldRouteKeys.Contains(RouteKey(good.Symbol, buy, sell)))
                    {
                        continue;
                    }

                    TradeRoute route;
                    TradeRouteCheck check;
                    if (!reachable)
                    {
                        route = new TradeRoute(good.Symbol, buy, sell, 0, good.PurchasePrice, atSell.SellPrice, 0, 0, string.Empty);
                        check = TradeRouteCheck.BuyMarketOutOfReach;
                    }
                    else if (TryEvaluateFrom(map, ship, good.Symbol, buy, sell, credits, approach, out route, out check))
                    {
                        check = route.IsLucrative(minProfitPerUnit) ? TradeRouteCheck.Lucrative : TradeRouteCheck.NotLucrative;
                        if (check == TradeRouteCheck.Lucrative)
                        {
                            routes.Add(route);
                        }
                    }

                    judgements?.Add(new TradeRouteJudgement(ship, check, route, credits, minProfitPerUnit));
                }
            }
        }
    }

    /// <summary>
    /// Works out a route from its buy market on, the flight there being <paramref name="approach"/>.
    /// </summary>
    /// <param name="failed">
    /// When it can't be flown or traded, the first check it fails; a market, a price or a trade volume unknown counts as
    /// <see cref="TradeRouteCheck.NotFullHold"/>, which one sale of none would be.
    /// </param>
    /// <param name="route">
    /// The route's figures; when it fails, as far as the checks got: the prices once both markets are known, the fuel once the
    /// flight is, and the units and the profit once the hold is.
    /// </param>
    private static bool TryEvaluateFrom(
        TradeMarketMap map,
        ShipModel ship,
        string tradeSymbol,
        string buyWaypointSymbol,
        string sellWaypointSymbol,
        long credits,
        TradeFlight approach,
        out TradeRoute route,
        out TradeRouteCheck failed)
    {
        route = new TradeRoute(tradeSymbol, buyWaypointSymbol, sellWaypointSymbol, 0, 0, 0, 0, 0, string.Empty);
        failed = TradeRouteCheck.NotFullHold;
        if (buyWaypointSymbol.Equals(sellWaypointSymbol, StringComparison.OrdinalIgnoreCase)
            || !map.TryGetGood(buyWaypointSymbol, tradeSymbol, out var atBuy)
            || atBuy.PurchasePrice <= 0
            || atBuy.TradeVolume <= 0
            || !map.TryGetGood(sellWaypointSymbol, tradeSymbol, out var atSell)
            || atSell.SellPrice <= 0
            || atSell.TradeVolume <= 0)
        {
            return false;
        }

        route = route with { BuyPrice = atBuy.PurchasePrice, SellPrice = atSell.SellPrice };

        // The ship docks at the buy market to buy, and fills its tank there when it sells fuel.
        var fuelAtBuy = map.SellsFuel(buyWaypointSymbol) ? ship.FuelCapacity : approach.FuelLeft;
        if (!TryPlanFlight(map, buyWaypointSymbol, sellWaypointSymbol, fuelAtBuy, ship.FuelCapacity, out var haul))
        {
            failed = TradeRouteCheck.SellMarketOutOfReach;
            return false;
        }

        // D56: the whole free hold, or at an ABUNDANT seller what both markets trade at once (D74); in one purchase and one
        // sale, and paid for, or no trip.
        var fuelCost = approach.FuelCost + haul.FuelCost;
        var affordable = Math.Max(0, credits - fuelCost) / atBuy.PurchasePrice;
        var units = UnitsAtOnce(map, ship, tradeSymbol, buyWaypointSymbol, sellWaypointSymbol);
        if (units == 0)
        {
            route = route with { FuelCost = fuelCost };
            return false;
        }

        var profit = ((long)(atSell.SellPrice - atBuy.PurchasePrice) * units) - fuelCost;
        if (affordable < units)
        {
            route = route with { Units = units, FuelCost = fuelCost, Profit = profit };
            failed = TradeRouteCheck.TooFewCredits;
            return false;
        }

        route = new TradeRoute(
            tradeSymbol,
            buyWaypointSymbol,
            sellWaypointSymbol,
            units,
            atBuy.PurchasePrice,
            atSell.SellPrice,
            fuelCost,
            profit,
            map.PricierGoodMadeFrom(sellWaypointSymbol, tradeSymbol));
        return true;
    }

    /// <summary>
    /// Where a ship gets the most for cargo it holds, after the fuel to get there: any market it can
    /// reach that buys the good, the one it is at included. A tie goes to where it is.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="tradeSymbol">The good it holds.</param>
    /// <param name="units">The units it holds.</param>
    /// <param name="sale">The best place to sell, and what it fetches there.</param>
    /// <returns>False when no market it can reach buys the good.</returns>
    public static bool TryFindBestSale(
        TradeMarketMap map,
        ShipModel ship,
        string tradeSymbol,
        int units,
        out TradeSale sale)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);

        sale = new TradeSale(string.Empty, 0, 0, 0);
        var here = ship.WaypointSymbol ?? string.Empty;
        var fuelAtStart = FuelAtDeparture(map, ship);
        var found = false;
        foreach (var market in map.MarketWaypoints.OrderBy(market => market, StringComparer.Ordinal))
        {
            if (!map.TryGetGood(market, tradeSymbol, out var good)
                || good.SellPrice <= 0
                || !TryPlanFlight(map, here, market, fuelAtStart, ship.FuelCapacity, out var flight))
            {
                continue;
            }

            var candidate = new TradeSale(market, good.SellPrice, flight.FuelCost, ((long)good.SellPrice * units) - flight.FuelCost);
            var isHere = market.Equals(here, StringComparison.OrdinalIgnoreCase);
            if (!found || candidate.NetRevenue > sale.NetRevenue || (candidate.NetRevenue == sale.NetRevenue && isHere))
            {
                sale = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// For a ship that holds cargo: the good that fetches the most where it sells best
    /// (<see cref="TryFindBestSale"/>), after the fuel to get there, when that is anything at all, or whatever it
    /// fetches when <paramref name="mustSell"/>. The trading plan sells held cargo by this rule, one good a trip, and
    /// the spare-time trip sells its hold by it (D36), so the trading plan can foresee where that leaves the ship.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now, with its hold.</param>
    /// <param name="mustSell">Whether to sell even where the sale doesn't pay for its fuel.</param>
    /// <param name="cargo">The good to sell, and the units aboard.</param>
    /// <param name="sale">Where to sell it, and what it fetches there.</param>
    /// <returns>False when nothing aboard is worth selling, or no market the ship can reach buys it.</returns>
    public static bool TryFindBestCargoSale(TradeMarketMap map, ShipModel ship, bool mustSell, out CargoItemModel cargo, out TradeSale sale)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);

        cargo = new CargoItemModel(string.Empty, 0);
        sale = new TradeSale(string.Empty, 0, 0, 0);
        var found = false;
        foreach (var item in (ship.CargoInventory ?? []).Where(item => item.Units > 0))
        {
            if (TryFindBestSale(map, ship, item.Symbol, item.Units, out var candidate)
                && (candidate.NetRevenue > 0 || mustSell)
                && (!found || candidate.NetRevenue > sale.NetRevenue))
            {
                cargo = item;
                sale = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>The fuel a CRUISE flight burns: the distance rounded, at least 1; none to stay put.</summary>
    private static int CruiseFuel(string from, string to, double distance)
        => from.Equals(to, StringComparison.OrdinalIgnoreCase)
            ? 0
            : Math.Max(1, (int)Math.Round(distance, MidpointRounding.AwayFromZero));

    /// <summary>What topping the tank up after a leg costs: whole market units of FUEL, at the market the leg ends at.</summary>
    private static long RefuelCost(TradeMarketMap map, int fuel, string refuelAt)
        => fuel == 0 ? 0 : (long)Math.Ceiling(fuel / (double)FuelPerMarketUnit) * map.FuelPrice(refuelAt);

    /// <summary>The fuel aboard when the ship leaves: a ship docked where fuel is sold fills its tank first.</summary>
    internal static int FuelAtDeparture(TradeMarketMap map, ShipModel ship)
        => ship.LocalStatus == ShipLocalStatus.Docked && map.SellsFuel(ship.WaypointSymbol ?? string.Empty)
            ? ship.FuelCapacity
            : ship.FuelCurrent;

    /// <summary>The best known way to a stop: its fuel cost, its stops, where it came from, and its last leg's fuel.</summary>
    private sealed record Hop(long Cost, int Stops, string Previous, int LastLeg);
}

/// <summary>A flight through refuelling stops (<see cref="TradeRoutePlanner.TryPlanFlight(TradeMarketMap, string, string, int, int, out TradeFlight)"/>).</summary>
public sealed record TradeFlight
{
    /// <summary>Creates a flight.</summary>
    /// <param name="Stops">The waypoints it lands at, in order, the destination last; empty to stay put.</param>
    /// <param name="FuelCost">The fuel bought on arrival at each stop.</param>
    /// <param name="FuelLeft">The fuel aboard on arrival at the destination.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TradeFlight(IReadOnlyList<string> Stops, long FuelCost, int FuelLeft)
    {
        this.Stops = Stops;
        this.FuelCost = FuelCost;
        this.FuelLeft = FuelLeft;
    }

    /// <summary>The waypoints it lands at, in order, the destination last; empty to stay put.</summary>
    public required IReadOnlyList<string> Stops { get; init; }

    /// <summary>The fuel bought on arrival at each stop.</summary>
    public required long FuelCost { get; init; }

    /// <summary>The fuel aboard on arrival at the destination.</summary>
    public required int FuelLeft { get; init; }
}

/// <summary>One trip of a trade route, as the planner works it out.</summary>
public sealed record TradeRoute
{
    /// <summary>Creates a trip.</summary>
    /// <param name="TradeSymbol">The good.</param>
    /// <param name="BuyWaypointSymbol">Where it is bought.</param>
    /// <param name="SellWaypointSymbol">Where it is sold.</param>
    /// <param name="Units">The units one purchase can carry.</param>
    /// <param name="BuyPrice">What a unit costs at the buy market.</param>
    /// <param name="SellPrice">What a unit fetches at the sell market.</param>
    /// <param name="FuelCost">The fuel for the trip: to the buy market, then on to the sell market.</param>
    /// <param name="Profit">What the trip earns after fuel.</param>
    /// <param name="FeedsTradeSymbol">The pricier good the sell market makes from the good, or empty.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TradeRoute(
        string TradeSymbol,
        string BuyWaypointSymbol,
        string SellWaypointSymbol,
        int Units,
        long BuyPrice,
        long SellPrice,
        long FuelCost,
        long Profit,
        string FeedsTradeSymbol)
    {
        this.TradeSymbol = TradeSymbol;
        this.BuyWaypointSymbol = BuyWaypointSymbol;
        this.SellWaypointSymbol = SellWaypointSymbol;
        this.Units = Units;
        this.BuyPrice = BuyPrice;
        this.SellPrice = SellPrice;
        this.FuelCost = FuelCost;
        this.Profit = Profit;
        this.FeedsTradeSymbol = FeedsTradeSymbol;
    }

    /// <summary>The good.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>Where it is bought.</summary>
    public required string BuyWaypointSymbol { get; init; }

    /// <summary>Where it is sold.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>The units one purchase can carry.</summary>
    public required int Units { get; init; }

    /// <summary>What a unit costs at the buy market.</summary>
    public required long BuyPrice { get; init; }

    /// <summary>What a unit fetches at the sell market.</summary>
    public required long SellPrice { get; init; }

    /// <summary>The fuel for the trip: to the buy market, then on to the sell market.</summary>
    public required long FuelCost { get; init; }

    /// <summary>What the trip earns after fuel.</summary>
    public required long Profit { get; init; }

    /// <summary>The pricier good the sell market makes from the good, or empty.</summary>
    public required string FeedsTradeSymbol { get; init; }

    /// <summary>The route's key (<see cref="TradeRoutePlanner.RouteKey"/>).</summary>
    public string Key => TradeRoutePlanner.RouteKey(TradeSymbol, BuyWaypointSymbol, SellWaypointSymbol);

    /// <summary>Whether the sell market makes a pricier good from the cargo (D15).</summary>
    public bool FeedsProduction => FeedsTradeSymbol.Length > 0;

    /// <summary>
    /// Whether the trip is worth it (D14): it earns something, and at least
    /// <paramref name="minProfitPerUnit"/> per unit after fuel.
    /// </summary>
    /// <param name="minProfitPerUnit">The minimum profit per unit; 0 or less means any profit.</param>
    /// <returns>True when the trip is lucrative.</returns>
    public bool IsLucrative(int minProfitPerUnit)
        => Profit > 0 && Profit >= (long)Math.Max(0, minProfitPerUnit) * Units;
}

/// <summary>Where cargo is sold, and what it fetches there.</summary>
public sealed record TradeSale
{
    /// <summary>Creates a sale.</summary>
    /// <param name="WaypointSymbol">The market.</param>
    /// <param name="SellPrice">What a unit fetches there.</param>
    /// <param name="FuelCost">The fuel to get there; 0 where the ship is.</param>
    /// <param name="NetRevenue">What the cargo fetches, minus that fuel.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TradeSale(string WaypointSymbol, long SellPrice, long FuelCost, long NetRevenue)
    {
        this.WaypointSymbol = WaypointSymbol;
        this.SellPrice = SellPrice;
        this.FuelCost = FuelCost;
        this.NetRevenue = NetRevenue;
    }

    /// <summary>The market.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>What a unit fetches there.</summary>
    public required long SellPrice { get; init; }

    /// <summary>The fuel to get there; 0 where the ship is.</summary>
    public required long FuelCost { get; init; }

    /// <summary>What the cargo fetches, minus that fuel.</summary>
    public required long NetRevenue { get; init; }
}
