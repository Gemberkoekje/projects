using SpaceTraders.Application.Interfaces;
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
///   <item>among lucrative trips, first those that feed a market making a material the jump gate still needs (D89), then the
///   most profitable, an end product's (a good nothing is made from) counted at half its profit (D15, D82, D85).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// A trip carries as many units as each earn <c>Trade.MinProfitPerUnit</c> (D79, asked on 2026-10-05: "A ship should buy as
/// much as is profitable per trip, and sell as much as is profitable per trip"), up to the ship's free hold and what the
/// credits pay for. A market's trade volume is the most a single purchase or sale takes, not its stock, so more goes in
/// batches, and each purchase raises the next quote and each sale lowers it: a unit's price is estimated by its batch
/// (<see cref="PriceSteps"/>), and as each further unit earns less, the units stop at the first that wouldn't earn the
/// minimum. The trade executor checks each batch against the price it is quoted then. Cargo may use the credit reserve
/// (D17); the trip's fuel is kept back.
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

    /// <summary>The API's flight modes this planner flies in (D84).</summary>
    public const string CruiseMode = "CRUISE";

    /// <summary>Twice as fast as CRUISE, on twice the fuel (D84).</summary>
    public const string BurnMode = "BURN";

    /// <summary>1 fuel whatever the distance, ten times slower than CRUISE (D45).</summary>
    public const string DriftMode = "DRIFT";

    /// <summary>Seconds a unit of distance takes at engine speed 10: the API's multipliers, 25 in CRUISE and 250 in DRIFT, over 10.</summary>
    private const double CruiseSecondsPerUnit = 2.5;

    /// <summary>See <see cref="CruiseSecondsPerUnit"/>.</summary>
    private const double DriftSecondsPerUnit = 25;

    /// <summary>What the API adds to every flight.</summary>
    private const double SecondsPerFlight = 15;

    /// <summary>A refuelling stop's dock, refuel and orbit.</summary>
    private const double SecondsToRefuel = 10;

    /// <summary>The most states the fastest-way search weighs before it gives up (a 600-unit tank and a hundred waypoints).</summary>
    private const int MaxMixedStates = 200_000;

    /// <summary>A sliver of floating-point error: an exact sum must not round a credit the wrong way.</summary>
    private const double Sliver = 1e-6;

    /// <summary>The key that identifies a route: good, buy market and sell market.</summary>
    /// <param name="tradeSymbol">The good.</param>
    /// <param name="buyWaypointSymbol">The buy market.</param>
    /// <param name="sellWaypointSymbol">The sell market.</param>
    /// <returns>The key, in upper case.</returns>
    public static string RouteKey(string tradeSymbol, string buyWaypointSymbol, string sellWaypointSymbol)
        => $"{buyWaypointSymbol}|{sellWaypointSymbol}|{tradeSymbol}".ToUpperInvariant();

    /// <summary>
    /// The lucrative routes for a ship, best first: those that feed the jump gate's materials (D89,
    /// <see cref="TradeRoute.ConstructionMaterial"/>), then by profit, an end product's counted at half
    /// (<see cref="RankingProfit"/>, D82, D85). Routes in <paramref name="heldRouteKeys"/> belong to other traders and are left
    /// out: two traders never share a route.
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
        => Rank(map, ship, credits, minProfitPerUnit, heldRouteKeys, HeldBuys.None);

    /// <summary>
    /// The lucrative routes for a ship, best first, as <see cref="Rank(TradeMarketMap, ShipModel, long, int, IReadOnlySet{string})"/>
    /// gives them, less those of a good at a market where another trip is on its way to buy it: one buyer at a time (D80).
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="credits">The credits on hand.</param>
    /// <param name="minProfitPerUnit">The profit per unit, after fuel, a trip must earn.</param>
    /// <param name="heldRouteKeys">The routes other traders hold (<see cref="RouteKey"/>).</param>
    /// <param name="heldBuys">The goods the trips on their way to buy hold at their buy markets.</param>
    /// <returns>The lucrative routes, best first; empty when there is none.</returns>
    public static IReadOnlyList<TradeRoute> Rank(
        TradeMarketMap map,
        ShipModel ship,
        long credits,
        int minProfitPerUnit,
        IReadOnlySet<string> heldRouteKeys,
        HeldBuys heldBuys)
    {
        var routes = new List<TradeRoute>();
        CheckRoutes(map, ship, credits, minProfitPerUnit, heldRouteKeys, heldBuys, routes, judgements: null);
        return [.. routes
            .OrderByDescending(route => route.FeedsConstruction)
            .ThenByDescending(RankingProfit)
            .ThenByDescending(route => route.FeedsProduction)
            .ThenBy(route => route.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Every route with a price gap that no other trader holds, as <see cref="Rank(TradeMarketMap, ShipModel, long, int, IReadOnlySet{string})"/>
    /// checks it for a ship: a market sells the good for less than another pays for it. Each says the first check it fails, in
    /// the order Rank runs them, or <see cref="TradeRouteCheck.Lucrative"/>: those are Rank's routes (slice 2.18, D76).
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
        => Judge(map, ship, credits, minProfitPerUnit, heldRouteKeys, HeldBuys.None);

    /// <summary>
    /// Every route with a price gap that no other trip keeps out, as <see cref="Rank(TradeMarketMap, ShipModel, long, int, IReadOnlySet{string}, HeldBuys)"/>
    /// checks it for a ship, each with the first check it fails (slice 2.18, D76). A route of a good at a market where another
    /// trip is on its way to buy it (D80) is left out, as a held route is.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="credits">The credits on hand.</param>
    /// <param name="minProfitPerUnit">The profit per unit, after fuel, a trip must earn.</param>
    /// <param name="heldRouteKeys">The routes other traders hold (<see cref="RouteKey"/>).</param>
    /// <param name="heldBuys">The goods the trips on their way to buy hold at their buy markets.</param>
    /// <returns>A judgement per route; empty when no market pays more for a good than another charges.</returns>
    public static IReadOnlyList<TradeRouteJudgement> Judge(
        TradeMarketMap map,
        ShipModel ship,
        long credits,
        int minProfitPerUnit,
        IReadOnlySet<string> heldRouteKeys,
        HeldBuys heldBuys)
    {
        var judgements = new List<TradeRouteJudgement>();
        CheckRoutes(map, ship, credits, minProfitPerUnit, heldRouteKeys, heldBuys, [], judgements);
        return judgements;
    }

    /// <summary>
    /// What a route counts for when routes are ranked (D82, D85): its profit, or half of it for an end product, a good
    /// nothing is made from (<see cref="TradeRoute.FeedsProduction"/>). So an end product goes first only when it earns more
    /// than twice as much. Asked on 2026-10-05, when no trader took FOOD at about 75,000 a load while trips of 302 to 3,864
    /// went first, D82 having put every end product after every other route: "Half weight".
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>The profit the route ranks by.</returns>
    public static double RankingProfit(TradeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        return route.FeedsProduction ? route.Profit : route.Profit / 2.0;
    }

    /// <summary>
    /// Orders two routes as Rank does (D15, D82, D85, D89): one that feeds the jump gate's materials first, then by
    /// <see cref="RankingProfit"/>, a good something is made from first on a tie, then by key.
    /// </summary>
    /// <param name="x">One route.</param>
    /// <param name="y">The other route.</param>
    /// <returns>Less than 0 when <paramref name="x"/> is the better route, more than 0 when <paramref name="y"/> is.</returns>
    public static int CompareBestFirst(TradeRoute x, TradeRoute y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        var gate = y.FeedsConstruction.CompareTo(x.FeedsConstruction);
        if (gate != 0)
        {
            return gate;
        }

        var profit = RankingProfit(y).CompareTo(RankingProfit(x));
        if (profit != 0)
        {
            return profit;
        }

        var feeds = y.FeedsProduction.CompareTo(x.FeedsProduction);
        return feeds != 0 ? feeds : string.CompareOrdinal(x.Key, y.Key);
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
    /// <param name="minProfitPerUnit">The profit per unit each further unit must earn (D14, D79).</param>
    /// <param name="route">The route's figures.</param>
    /// <returns>
    /// False when the ship can't fly it (a market, a price or a position unknown, no way to get there within its tank) or
    /// can't buy a unit worth it (no free hold, too few credits, or not even the first unit earns
    /// <paramref name="minProfitPerUnit"/>, or for a delivery that feeds the jump gate's material sells for what it cost, D90).
    /// </returns>
    public static bool TryEvaluate(
        TradeMarketMap map,
        ShipModel ship,
        string tradeSymbol,
        string buyWaypointSymbol,
        string sellWaypointSymbol,
        long credits,
        int minProfitPerUnit,
        out TradeRoute route)
        => TryEvaluate(map, ship, tradeSymbol, buyWaypointSymbol, sellWaypointSymbol, credits, minProfitPerUnit, out route, out _);

    /// <summary>
    /// Works out one route for a ship where it is now, as <see cref="TryEvaluate(TradeMarketMap, ShipModel, string, string, string, long, int, out TradeRoute)"/>
    /// does, and says the first check it fails.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <param name="buyWaypointSymbol">The buy market.</param>
    /// <param name="sellWaypointSymbol">The sell market.</param>
    /// <param name="credits">The credits on hand.</param>
    /// <param name="minProfitPerUnit">The profit per unit each further unit must earn (D14, D79).</param>
    /// <param name="route">The route's figures; as far as the checks got when it fails.</param>
    /// <param name="failed">The first check it fails; <see cref="TradeRouteCheck.Lucrative"/> when it has units, lucrative or not.</param>
    /// <returns>False when the ship can't fly it or can't buy a unit worth it.</returns>
    public static bool TryEvaluate(
        TradeMarketMap map,
        ShipModel ship,
        string tradeSymbol,
        string buyWaypointSymbol,
        string sellWaypointSymbol,
        long credits,
        int minProfitPerUnit,
        out TradeRoute route,
        out TradeRouteCheck failed)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);

        if (!TryPlanFlight(map, ship.WaypointSymbol ?? string.Empty, buyWaypointSymbol, FuelAtDeparture(map, ship), ship.FuelCapacity, out var approach))
        {
            route = new TradeRoute(tradeSymbol, buyWaypointSymbol, sellWaypointSymbol, 0, 0, 0, 0, 0, string.Empty);
            failed = TradeRouteCheck.BuyMarketOutOfReach;
            return false;
        }

        if (!TryEvaluateFrom(map, ship, tradeSymbol, buyWaypointSymbol, sellWaypointSymbol, credits, minProfitPerUnit, approach, out route, out failed))
        {
            return false;
        }

        failed = TradeRouteCheck.Lucrative;
        return true;
    }

    /// <summary>
    /// How many of the next units are worth buying in one purchase at the price quoted now (D79): those whose expected sale,
    /// each batch sold a step cheaper (<see cref="PriceSteps.SalePriceOf"/>), still earns <paramref name="minProfitPerUnit"/>
    /// over that price, up to <paramref name="most"/>. Each further unit earns less, so they stop at the first that doesn't.
    /// </summary>
    /// <param name="quote">What a unit costs at the buy market now.</param>
    /// <param name="atSell">The good at the sell market, as last seen.</param>
    /// <param name="bought">The units the trip has bought so far: where in the sales the next ones come.</param>
    /// <param name="most">The most the purchase may take: the trade volume, the free hold and the credits.</param>
    /// <param name="minProfitPerUnit">The profit each unit must earn; 0 or less means any.</param>
    /// <param name="feedsGate">
    /// Whether the trip feeds a material the jump gate still needs (D89): then a unit is worth buying while its sale fetches at
    /// least what it costs, the trip's fuel being the price of feeding the gate (D90).
    /// </param>
    /// <returns>The units to buy, from 0.</returns>
    public static int UnitsWorthBuying(long quote, TradeGoodSnapshot atSell, int bought, int most, int minProfitPerUnit, bool feedsGate = false)
    {
        ArgumentNullException.ThrowIfNull(atSell);

        var units = 0;
        while (units < most && Earns(PriceSteps.SalePriceOf(atSell.SellPrice, bought + units, atSell.TradeVolume) - quote, minProfitPerUnit, feedsGate))
        {
            units++;
        }

        return units;
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
    /// The next leg of a ship's way to a waypoint, and the flight mode to fly it in (PLAN.md slice 6.19, D84, asked on
    /// 2026-10-05: "I'd like a ship to burn if they can reach the destination with double fuel consumption, but cruise if
    /// they cannot."):
    /// <list type="bullet">
    ///   <item>within reach of a chain of fuel markets, the next stop of that flight (<see cref="TryPlanFlight(TradeMarketMap, ShipModel, string, out TradeFlight)"/>),
    ///   in BURN when the ship has twice the leg's fuel and burning strands nothing (<see cref="Burns"/>), otherwise in
    ///   CRUISE, unless that flight would land it with an empty tank where no fuel is sold (<see cref="Strands"/>);</item>
    ///   <item>out of that reach, the first leg of the fastest way there that drifts as little as it can
    ///   (<see cref="TryPlanMixedFlight"/>): asked on 2026-10-05, "can we optimize the routing for a location where a
    ///   combination of cruising and drifting is faster than just drifting?" A cruise leg into a market that sells fuel
    ///   burns there too, when the fuel allows.</item>
    /// </list>
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="destination">Where it is going.</param>
    /// <param name="onward">
    /// Where it must get to from <paramref name="destination"/> next, in CRUISE, when the caller knows (a mining trip's
    /// market); empty for any market that sells fuel.
    /// </param>
    /// <param name="leg">The waypoint to fly to now, and how.</param>
    /// <returns>False when the ship is there already, or no way there is known.</returns>
    public static bool TryPlanNextLeg(TradeMarketMap map, ShipModel ship, string destination, string onward, out FlightLeg leg)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(onward);

        var here = ship.WaypointSymbol ?? string.Empty;
        var fuel = FuelAtDeparture(map, ship);
        leg = new FlightLeg(destination, CruiseMode);
        if (here.Equals(destination, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryPlanFlight(map, here, destination, fuel, ship.FuelCapacity, out var flight)
            && flight.Stops.Count > 0
            && !Strands(map.SellsFuel(destination), flight.FuelLeft))
        {
            var stop = flight.Stops[0];
            leg = new FlightLeg(stop, Burns(map, here, stop, fuel, ship.FuelCapacity, stop.Equals(destination, StringComparison.OrdinalIgnoreCase) ? onward : string.Empty) ? BurnMode : CruiseMode);
            return true;
        }

        if (!TryPlanMixedFlight(map, here, destination, fuel, ship.FuelCapacity, out var legs) || legs.Count == 0)
        {
            return false;
        }

        // The tank fills where a cruise leg ends at a market that sells fuel, so burning there takes nothing from what follows.
        leg = legs[0].FlightMode == CruiseMode && map.SellsFuel(legs[0].WaypointSymbol) && Burns(map, here, legs[0].WaypointSymbol, fuel, ship.FuelCapacity, string.Empty)
            ? legs[0] with { FlightMode = BurnMode }
            : legs[0];
        return true;
    }

    /// <summary>
    /// Whether a leg flies in BURN (D84): the ship has twice its CRUISE fuel, and burning strands nothing. Where the leg ends
    /// at a market that sells fuel the tank fills there. Elsewhere (an asteroid, a gas giant) what burning leaves must still
    /// take the ship on as CRUISE would have: to <paramref name="onward"/>, with no refuelling stop more than after a CRUISE
    /// leg, or, without one, straight to a market that sells fuel (B58). So a drone never burns its way into an asteroid it
    /// can't carry its ore back from.
    /// </summary>
    private static bool Burns(TradeMarketMap map, string from, string to, int fuel, int fuelCapacity, string onward)
    {
        if (!map.TryGetDistance(from, to, out var distance))
        {
            return false;
        }

        var cruise = CruiseFuel(from, to, distance);
        if (cruise == 0 || 2 * cruise > fuel)
        {
            return false;
        }

        if (map.SellsFuel(to))
        {
            return true;
        }

        var left = fuel - (2 * cruise);
        if (onward.Length > 0 && !onward.Equals(to, StringComparison.OrdinalIgnoreCase))
        {
            return TryPlanFlight(map, to, onward, left, fuelCapacity, out var burnt)
                && TryPlanFlight(map, to, onward, fuel - cruise, fuelCapacity, out var cruised)
                && burnt.Stops.Count <= cruised.Stops.Count;
        }

        return map.MarketWaypoints.Any(market => map.SellsFuel(market)
            && map.TryGetDistance(to, market, out var away)
            && CruiseFuel(to, market, away) <= left);
    }

    /// <summary>
    /// Whether a ship that lands with this much fuel aboard is stuck there for good: an empty tank where no fuel is sold,
    /// since even a drift takes 1. With 1 left it can always drift on to a market that sells fuel, and such a market fills an
    /// empty tank (asked on 2026-10-05: "A ship can technically land anywhere with 1 fuel and then drift to a fuel station").
    /// </summary>
    private static bool Strands(bool sellsFuel, int fuelLeft) => fuelLeft < 1 && !sellsFuel;

    /// <summary>
    /// The fastest way to a waypoint that no chain of fuel markets reaches in CRUISE (D84): CRUISE legs between any waypoints
    /// on the fuel aboard, the tank filled at each market that sells fuel, and DRIFT legs (1 fuel whatever the distance, ten
    /// times slower) where nothing else gets on, but none that lands the ship with an empty tank where no fuel is sold
    /// (<see cref="Strands"/>). A* over the waypoints and the fuel aboard, by the API's flight times at one engine speed: 25
    /// per unit of distance in CRUISE, 250 in DRIFT, 15 seconds a flight and a few seconds to refuel at a stop, guided by the
    /// straight distance still to go in CRUISE, which no way beats. In X1-FJ91 on 2026-10-05, a drone from H60 to B44
    /// cruises to F57 by way of A1 and drifts the 244 from there: 2.0 hours, against 2.9 drifting the 368 to B7 and cruising
    /// on, and it lands with 79 fuel instead of 26.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="from">Where the ship is.</param>
    /// <param name="to">Where it is going.</param>
    /// <param name="fuelAtStart">The fuel aboard on leaving <paramref name="from"/>.</param>
    /// <param name="fuelCapacity">What the tank holds, as filled at each market that sells fuel.</param>
    /// <param name="legs">The legs, in order, each with its flight mode.</param>
    /// <returns>False when a position is unknown, or the ship has no fuel to drift with.</returns>
    public static bool TryPlanMixedFlight(TradeMarketMap map, string from, string to, int fuelAtStart, int fuelCapacity, out IReadOnlyList<FlightLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(map);

        legs = [];
        string[] waypoints =
        [
            .. map.Waypoints.Select(waypoint => waypoint.Symbol)
                .Append(from)
                .Append(to)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(waypoint => map.TryGetDistance(waypoint, to, out _))
                .Order(StringComparer.Ordinal),
        ];
        var count = waypoints.Length;
        var start = Array.FindIndex(waypoints, waypoint => waypoint.Equals(from, StringComparison.OrdinalIgnoreCase));
        var goal = Array.FindIndex(waypoints, waypoint => waypoint.Equals(to, StringComparison.OrdinalIgnoreCase));
        if (start < 0 || goal < 0 || fuelAtStart < 0)
        {
            return false;
        }

        // Worked out once: each pair's CRUISE fuel, the markets that sell fuel, and the straight distance still to go.
        var cruise = new int[count, count];
        var sellsFuel = new bool[count];
        var toGo = new double[count];
        for (var a = 0; a < count; a++)
        {
            sellsFuel[a] = map.SellsFuel(waypoints[a]);
            map.TryGetDistance(waypoints[a], to, out toGo[a]);
            for (var b = 0; b < count; b++)
            {
                map.TryGetDistance(waypoints[a], waypoints[b], out var distance);
                cruise[a, b] = CruiseFuel(waypoints[a], waypoints[b], distance);
            }
        }

        // A state is a waypoint and the fuel aboard on landing there; at a market that sells fuel, but for the destination, a
        // full tank, whatever was left. Landing somewhere no later and with more fuel never does worse, so a state with no
        // more fuel than the most the ship has landed there with is passed over.
        var levels = Math.Max(fuelCapacity, fuelAtStart) + 1;
        var seconds = new double[count * levels];
        Array.Fill(seconds, double.PositiveInfinity);
        var previous = new int[count * levels];
        var drifted = new bool[count * levels];
        var mostFuel = new int[count];
        Array.Fill(mostFuel, -1);
        var first = (start * levels) + fuelAtStart;
        seconds[first] = 0;
        previous[first] = -1;
        var open = new PriorityQueue<int, double>();
        open.Enqueue(first, toGo[start] * CruiseSecondsPerUnit);
        var arrived = -1;
        var weighed = 0;
        while (open.TryDequeue(out var state, out _))
        {
            var here = state / levels;
            var fuel = state % levels;
            if (fuel <= mostFuel[here])
            {
                continue;
            }

            mostFuel[here] = fuel;
            if (here == goal)
            {
                arrived = state;
                break;
            }

            if (++weighed > MaxMixedStates)
            {
                return false;
            }

            for (var next = 0; next < count; next++)
            {
                if (next == here)
                {
                    continue;
                }

                var leg = cruise[here, next];
                if (leg <= fuel)
                {
                    Relax(next, fuel - leg, FlightSeconds(leg, CruiseSecondsPerUnit), drift: false);
                }

                if (fuel >= 1)
                {
                    Relax(next, fuel - 1, FlightSeconds(Math.Max(1, leg), DriftSecondsPerUnit), drift: true);
                }
            }

            void Relax(int next, int left, double flight, bool drift)
            {
                if (Strands(sellsFuel[next], left))
                {
                    return;
                }

                var refills = next != goal && sellsFuel[next];
                var kept = refills ? Math.Max(left, fuelCapacity) : left;
                var reached = (next * levels) + kept;
                var at = seconds[state] + flight + (refills && left < fuelCapacity ? SecondsToRefuel : 0);
                if (kept > mostFuel[next] && at < seconds[reached])
                {
                    seconds[reached] = at;
                    previous[reached] = state;
                    drifted[reached] = drift;
                    open.Enqueue(reached, at + (toGo[next] * CruiseSecondsPerUnit));
                }
            }
        }

        if (arrived < 0)
        {
            return false;
        }

        var path = new List<FlightLeg>();
        for (var state = arrived; previous[state] >= 0; state = previous[state])
        {
            path.Add(new FlightLeg(waypoints[state / levels], drifted[state] ? DriftMode : CruiseMode));
        }

        path.Reverse();
        legs = path;
        return true;
    }

    /// <summary>
    /// Runs Rank's checks on every route with a price gap that no other trader holds, of a good no other trip is on its way to
    /// buy at that market (D80): the lucrative routes go to <paramref name="routes"/>, and with <paramref name="judgements"/>
    /// every route goes there too, with the first check it fails (Judge). A delivery that feeds a material the jump gate still
    /// needs counts at no gap too: its goods only have to sell for what they cost (D90).
    /// </summary>
    private static void CheckRoutes(
        TradeMarketMap map,
        ShipModel ship,
        long credits,
        int minProfitPerUnit,
        IReadOnlySet<string> heldRouteKeys,
        HeldBuys heldBuys,
        List<TradeRoute> routes,
        List<TradeRouteJudgement>? judgements)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(heldRouteKeys);
        ArgumentNullException.ThrowIfNull(heldBuys);

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
                        || atSell.SellPrice < good.PurchasePrice
                        || (atSell.SellPrice == good.PurchasePrice && map.ConstructionMaterialMadeFrom(sell, good.Symbol).Length == 0)
                        || heldRouteKeys.Contains(RouteKey(good.Symbol, buy, sell))
                        || heldBuys.Holds(good.Symbol, buy))
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
                    else if (TryEvaluateFrom(map, ship, good.Symbol, buy, sell, credits, minProfitPerUnit, approach, out route, out check))
                    {
                        check = route.IsWorthIt(minProfitPerUnit) ? TradeRouteCheck.Lucrative : TradeRouteCheck.NotLucrative;
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
    /// Works out a route from its buy market on, the flight there being <paramref name="approach"/>: as many units as each earn
    /// <paramref name="minProfitPerUnit"/>, each batch bought a step dearer and each sold a step cheaper than the one before
    /// (D79, <see cref="PriceSteps"/>), up to the free hold and what the credits pay for once the trip's fuel is kept back.
    /// </summary>
    /// <param name="failed">
    /// When it can't be flown or traded, the first check it fails: a market, a price or a trade volume unknown, or no room in
    /// the hold, counts as <see cref="TradeRouteCheck.NoRoom"/>; no unit the credits pay for as
    /// <see cref="TradeRouteCheck.TooFewCredits"/>; not even the first unit earning the minimum as
    /// <see cref="TradeRouteCheck.NotLucrative"/>.
    /// </param>
    /// <param name="route">
    /// The route's figures; when it fails, as far as the checks got: the prices once both markets are known, the fuel once the
    /// flight is.
    /// </param>
    private static bool TryEvaluateFrom(
        TradeMarketMap map,
        ShipModel ship,
        string tradeSymbol,
        string buyWaypointSymbol,
        string sellWaypointSymbol,
        long credits,
        int minProfitPerUnit,
        TradeFlight approach,
        out TradeRoute route,
        out TradeRouteCheck failed)
    {
        route = new TradeRoute(tradeSymbol, buyWaypointSymbol, sellWaypointSymbol, 0, 0, 0, 0, 0, string.Empty);
        failed = TradeRouteCheck.NoRoom;
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

        // D89, D90: the material the delivery feeds, when the jump gate still needs it; such a unit only has to sell for what it
        // cost.
        var material = map.ConstructionMaterialMadeFrom(sellWaypointSymbol, tradeSymbol);
        route = route with { BuyPrice = atBuy.PurchasePrice, SellPrice = atSell.SellPrice, ConstructionMaterial = material };

        // The ship docks at the buy market to buy, and fills its tank there when it sells fuel.
        var fuelAtBuy = map.SellsFuel(buyWaypointSymbol) ? ship.FuelCapacity : approach.FuelLeft;
        if (!TryPlanFlight(map, buyWaypointSymbol, sellWaypointSymbol, fuelAtBuy, ship.FuelCapacity, out var haul))
        {
            failed = TradeRouteCheck.SellMarketOutOfReach;
            return false;
        }

        var fuelCost = approach.FuelCost + haul.FuelCost;
        route = route with { FuelCost = fuelCost };
        var free = ship.CargoCapacity - ship.CargoCurrent;
        if (free <= 0)
        {
            return false;
        }

        // D79: unit by unit, while the next earns the minimum and the credits left after the trip's fuel pay for it.
        var budget = Math.Max(0, credits - fuelCost);
        var units = 0;
        var cost = 0.0;
        var revenue = 0.0;
        while (units < free)
        {
            var buy = PriceSteps.PurchasePriceOf(atBuy.PurchasePrice, units, atBuy.TradeVolume);
            var sell = PriceSteps.SalePriceOf(atSell.SellPrice, units, atSell.TradeVolume);
            if (cost + buy > budget + Sliver)
            {
                failed = units == 0 ? TradeRouteCheck.TooFewCredits : failed;
                break;
            }

            if (!Earns(sell - buy, minProfitPerUnit, material.Length > 0))
            {
                failed = units == 0 ? TradeRouteCheck.NotLucrative : failed;
                break;
            }

            cost += buy;
            revenue += sell;
            units++;
        }

        if (units == 0)
        {
            return false;
        }

        var cargoCost = (long)Math.Ceiling(cost - Sliver);
        route = new TradeRoute(
            tradeSymbol,
            buyWaypointSymbol,
            sellWaypointSymbol,
            units,
            atBuy.PurchasePrice,
            atSell.SellPrice,
            fuelCost,
            (long)Math.Floor(revenue + Sliver) - cargoCost - fuelCost,
            map.PricierGoodMadeFrom(sellWaypointSymbol, tradeSymbol))
        {
            CargoCost = cargoCost,
            FeedsProduction = !map.IsEndProduct(tradeSymbol),
            ConstructionMaterial = material,
        };
        return true;
    }

    /// <summary>
    /// Whether a unit's margin earns the minimum: something, and at least <paramref name="minProfitPerUnit"/> (D14); for a unit
    /// that feeds the jump gate's material, nothing lost on it (D90).
    /// </summary>
    private static bool Earns(double margin, int minProfitPerUnit, bool feedsGate = false)
        => feedsGate ? margin + Sliver >= 0 : margin > 0 && margin + Sliver >= Math.Max(0, minProfitPerUnit);

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

    /// <summary>A flight's seconds at engine speed 10, for comparing ways (<see cref="TryPlanMixedFlight"/>).</summary>
    private static double FlightSeconds(int distance, double secondsPerUnit) => (distance * secondsPerUnit) + SecondsPerFlight;

    /// <summary>The fuel aboard when the ship leaves: a ship docked where fuel is sold fills its tank first.</summary>
    internal static int FuelAtDeparture(TradeMarketMap map, ShipModel ship)
        => ship.LocalStatus == ShipLocalStatus.Docked && map.SellsFuel(ship.WaypointSymbol ?? string.Empty)
            ? ship.FuelCapacity
            : ship.FuelCurrent;

    /// <summary>The best known way to a stop: its fuel cost, its stops, where it came from, and its last leg's fuel.</summary>
    private sealed record Hop(long Cost, int Stops, string Previous, int LastLeg);
}

/// <summary>One leg of a flight and the flight mode it flies in (PLAN.md slice 6.19, D84, <see cref="TradeRoutePlanner.TryPlanNextLeg"/>).</summary>
public sealed record FlightLeg
{
    /// <summary>Creates a leg.</summary>
    /// <param name="WaypointSymbol">Where the leg ends.</param>
    /// <param name="FlightMode">How it flies: <c>BURN</c>, <c>CRUISE</c> or <c>DRIFT</c>.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public FlightLeg(string WaypointSymbol, string FlightMode)
    {
        this.WaypointSymbol = WaypointSymbol;
        this.FlightMode = FlightMode;
    }

    /// <summary>Where the leg ends.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>How it flies: <c>BURN</c>, <c>CRUISE</c> or <c>DRIFT</c>.</summary>
    public required string FlightMode { get; init; }
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
    /// <param name="Units">The units the trip carries, in batches of each market's trade volume (D79).</param>
    /// <param name="BuyPrice">What a unit costs at the buy market now: the first batch's price.</param>
    /// <param name="SellPrice">What a unit fetches at the sell market now: the first batch's price.</param>
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
        CargoCost = Units * BuyPrice;
    }

    /// <summary>The good.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>Where it is bought.</summary>
    public required string BuyWaypointSymbol { get; init; }

    /// <summary>Where it is sold.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>The units the trip carries, in batches of each market's trade volume (D79).</summary>
    public required int Units { get; init; }

    /// <summary>What a unit costs at the buy market now: the first batch's price.</summary>
    public required long BuyPrice { get; init; }

    /// <summary>What a unit fetches at the sell market now: the first batch's price.</summary>
    public required long SellPrice { get; init; }

    /// <summary>The fuel for the trip: to the buy market, then on to the sell market.</summary>
    public required long FuelCost { get; init; }

    /// <summary>What the trip earns after fuel.</summary>
    public required long Profit { get; init; }

    /// <summary>The pricier good the sell market makes from the good, or empty.</summary>
    public required string FeedsTradeSymbol { get; init; }

    /// <summary>
    /// What the units are expected to cost, each batch a step dearer than the one before (<see cref="PriceSteps"/>); the units
    /// at <see cref="BuyPrice"/> unless set. What the trip holds back until it buys (D57).
    /// </summary>
    public long CargoCost { get; init; }

    /// <summary>The route's key (<see cref="TradeRoutePlanner.RouteKey"/>).</summary>
    public string Key => TradeRoutePlanner.RouteKey(TradeSymbol, BuyWaypointSymbol, SellWaypointSymbol);

    /// <summary>
    /// Whether something is made from the good (D15, D82): an end product, which nothing is made from
    /// (<see cref="TradeMarketMap.IsEndProduct"/>), ranks at half its profit, wherever it is sold (D85). False unless set.
    /// </summary>
    public bool FeedsProduction { get; init; }

    /// <summary>
    /// The jump gate's material the sell market makes from the good, while the gate needs it (D89,
    /// <see cref="TradeMarketMap.ConstructionMaterialMadeFrom"/>): such a route comes before every other. Empty for none, and
    /// unless set.
    /// </summary>
    public string ConstructionMaterial { get; init; } = string.Empty;

    /// <summary>Whether the route feeds a material the jump gate still needs (<see cref="ConstructionMaterial"/>, D89).</summary>
    public bool FeedsConstruction => ConstructionMaterial.Length > 0;

    /// <summary>
    /// Whether the trip is worth it (D14): it earns something, and at least
    /// <paramref name="minProfitPerUnit"/> per unit after fuel.
    /// </summary>
    /// <param name="minProfitPerUnit">The minimum profit per unit; 0 or less means any profit.</param>
    /// <returns>True when the trip is lucrative.</returns>
    public bool IsLucrative(int minProfitPerUnit)
        => Profit > 0 && Profit >= (long)Math.Max(0, minProfitPerUnit) * Units;

    /// <summary>
    /// Whether the trip is worth taking: lucrative (D14), or, when it feeds a material the jump gate still needs (D89), its
    /// goods sell for at least what they cost, its fuel being the price of feeding the gate (D90, asked on 2026-10-05: "Up to
    /// its fuel"; IRON from H60 at 150 to D52 and F58 at 150 to 155 earned less than the minimum).
    /// </summary>
    /// <param name="minProfitPerUnit">The minimum profit per unit; 0 or less means any profit.</param>
    /// <returns>True when the trip is worth taking.</returns>
    public bool IsWorthIt(int minProfitPerUnit)
        => FeedsConstruction ? Units > 0 && Profit + FuelCost >= 0 : IsLucrative(minProfitPerUnit);
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
