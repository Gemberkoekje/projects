using System.Globalization;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// How far a route got through the checks <see cref="TradeRoutePlanner.Rank"/> runs on it, in the order they run: a route that
/// fails one isn't checked further (slice 2.18, D76).
/// </summary>
public enum TradeRouteCheck
{
    /// <summary>Not checked.</summary>
    None = 0,

    /// <summary>No flight in CRUISE takes the ship from where it is to the buy market, refuelling at markets on the way.</summary>
    BuyMarketOutOfReach = 1,

    /// <summary>No such flight takes it on from the buy market to the sell market.</summary>
    SellMarketOutOfReach = 2,

    /// <summary>
    /// One purchase and one sale don't fill the ship's free hold, and the buy market's supply of the good isn't ABUNDANT (D56,
    /// D74); or the hold has no room.
    /// </summary>
    NotFullHold = 3,

    /// <summary>The credits for cargo, less the trip's fuel, don't pay for the units (D56).</summary>
    TooFewCredits = 4,

    /// <summary>The trip earns less than <c>Trade.MinProfitPerUnit</c> a unit after fuel, or nothing (D14).</summary>
    NotLucrative = 5,

    /// <summary>The trip passes every check: it is one of the ship's routes.</summary>
    Lucrative = 6,
}

/// <summary>
/// A route with a price gap, as one ship's checks found it (<see cref="TradeRoutePlanner.Judge"/>): one market sells the good
/// for less than another pays for it (slice 2.18, D76).
/// </summary>
public sealed record TradeRouteJudgement
{
    private const string AbundantSupply = "ABUNDANT";

    /// <summary>Creates a judgement.</summary>
    /// <param name="Ship">The ship, where it was when judged.</param>
    /// <param name="Check">The first check the route failed, or <see cref="TradeRouteCheck.Lucrative"/>.</param>
    /// <param name="Route">The route's figures, as far as the checks got.</param>
    /// <param name="Credits">The credits for cargo it was judged with.</param>
    /// <param name="MinProfitPerUnit">The profit per unit, after fuel, a trip had to earn (D14).</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TradeRouteJudgement(ShipModel Ship, TradeRouteCheck Check, TradeRoute Route, long Credits, int MinProfitPerUnit)
    {
        this.Ship = Ship;
        this.Check = Check;
        this.Route = Route;
        this.Credits = Credits;
        this.MinProfitPerUnit = MinProfitPerUnit;
    }

    /// <summary>The ship, where it was when judged.</summary>
    public required ShipModel Ship { get; init; }

    /// <summary>The first check the route failed, or <see cref="TradeRouteCheck.Lucrative"/>.</summary>
    public required TradeRouteCheck Check { get; init; }

    /// <summary>
    /// The route's figures, as far as the checks got: the prices always; the fuel from <see cref="TradeRouteCheck.NotFullHold"/>
    /// on; the units and the profit from <see cref="TradeRouteCheck.TooFewCredits"/> on.
    /// </summary>
    public required TradeRoute Route { get; init; }

    /// <summary>The credits for cargo it was judged with.</summary>
    public required long Credits { get; init; }

    /// <summary>The profit per unit, after fuel, a trip had to earn (D14).</summary>
    public required int MinProfitPerUnit { get; init; }

    /// <summary>What the sell market pays for a unit above what the buy market charges.</summary>
    public long PriceGap => Route.SellPrice - Route.BuyPrice;

    /// <summary>
    /// For each good, the judgement that got furthest through the checks, whichever ship and route it was for: among those
    /// that got as far, the one with the largest price gap, then by ship and route. The furthest come first, then by good.
    /// </summary>
    /// <param name="judgements">The judgements of one pass, for any number of ships.</param>
    /// <returns>One judgement per good.</returns>
    public static IReadOnlyList<TradeRouteJudgement> FurthestPerGood(IEnumerable<TradeRouteJudgement> judgements)
    {
        ArgumentNullException.ThrowIfNull(judgements);

        return [.. judgements
            .GroupBy(judgement => judgement.Route.TradeSymbol, StringComparer.OrdinalIgnoreCase)
            .Select(good => good
                .OrderByDescending(judgement => judgement.Check)
                .ThenByDescending(judgement => judgement.PriceGap)
                .ThenBy(judgement => judgement.Ship.Symbol, StringComparer.Ordinal)
                .ThenBy(judgement => judgement.Route.Key, StringComparer.Ordinal)
                .First())
            .OrderByDescending(judgement => judgement.Check)
            .ThenBy(judgement => judgement.Route.TradeSymbol, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Why the route isn't one of the ship's, in a sentence, with the figures of the check it failed; for a lucrative route,
    /// what it earns.
    /// </summary>
    /// <param name="map">The system's map it was judged with.</param>
    /// <returns>The sentence, starting with the ship.</returns>
    public string Why(TradeMarketMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var ship = Ship.Symbol;
        var buy = Route.BuyWaypointSymbol;
        var sell = Route.SellWaypointSymbol;
        return Check switch
        {
            TradeRouteCheck.BuyMarketOutOfReach => string.Create(
                CultureInfo.InvariantCulture,
                $"{ship}: can't reach {buy} from {Ship.WaypointSymbol} with {TradeRoutePlanner.FuelAtDeparture(map, Ship):N0} fuel aboard, refuelling at markets on the way ({Ship.FuelCapacity:N0}-unit tank)."),
            TradeRouteCheck.SellMarketOutOfReach => string.Create(
                CultureInfo.InvariantCulture,
                $"{ship}: can't fly on from {buy} to {sell} with {FuelAtBuyMarket(map):N0} fuel aboard, refuelling at markets on the way ({Ship.FuelCapacity:N0}-unit tank)."),
            TradeRouteCheck.NotFullHold => NotFullHold(map),
            TradeRouteCheck.TooFewCredits => string.Create(
                CultureInfo.InvariantCulture,
                $"{ship}: {Route.Units:N0} units at {Route.BuyPrice:N0} and {Route.FuelCost:N0} for fuel cost {(Route.Units * Route.BuyPrice) + Route.FuelCost:N0}; {Credits:N0} credits are free for cargo (D56)."),
            TradeRouteCheck.NotLucrative => string.Create(
                CultureInfo.InvariantCulture,
                $"{ship}: {Route.Units:N0} units earn {Route.Profit:N0} after {Route.FuelCost:N0} for fuel, {Route.Profit / Route.Units:N0} a unit; a trip must earn {MinProfitPerUnit:N0} a unit (D14)."),
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"{ship}: lucrative, {Route.Profit:N0} after fuel, {Route.Profit / Route.Units:N0} a unit."),
        };
    }

    /// <summary>The fuel aboard when the ship leaves the buy market: a full tank where it sells fuel, else what the flight there left.</summary>
    private int FuelAtBuyMarket(TradeMarketMap map)
        => map.SellsFuel(Route.BuyWaypointSymbol) || !TradeRoutePlanner.TryPlanFlight(map, Ship, Route.BuyWaypointSymbol, out var approach)
            ? Ship.FuelCapacity
            : approach.FuelLeft;

    private string NotFullHold(TradeMarketMap map)
    {
        var free = Ship.CargoCapacity - Ship.CargoCurrent;
        map.TryGetGood(Route.BuyWaypointSymbol, Route.TradeSymbol, out var atBuy);
        map.TryGetGood(Route.SellWaypointSymbol, Route.TradeSymbol, out var atSell);
        if (free <= 0)
        {
            return $"{Ship.Symbol}: no room in its hold.";
        }

        if (atSell.TradeVolume <= 0 || string.Equals(atBuy.Supply, AbundantSupply, StringComparison.OrdinalIgnoreCase))
        {
            // At an ABUNDANT seller a trip takes what both markets trade at once (D74): none, only where the buyer trades none.
            return $"{Ship.Symbol}: {Route.SellWaypointSymbol} names no trade volume for {Route.TradeSymbol}.";
        }

        var atOnce = Math.Min(free, Math.Min(atBuy.TradeVolume, atSell.TradeVolume));
        var supply = atBuy.Supply.Length > 0 ? atBuy.Supply : "no supply level listed";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Ship.Symbol}: one purchase and one sale take {atOnce:N0} of its {free:N0} free units ({Route.BuyWaypointSymbol} sells {atBuy.TradeVolume:N0} at a time, {Route.SellWaypointSymbol} buys {atSell.TradeVolume:N0}); less than a full hold needs ABUNDANT supply at {Route.BuyWaypointSymbol}, which has {supply} (D56, D74).");
    }
}
