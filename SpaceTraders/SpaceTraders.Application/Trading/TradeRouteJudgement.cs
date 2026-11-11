using System.Globalization;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// How far a route got through the checks <see cref="TradeRoutePlanner.Rank(TradeMarketMap, ShipModel, long, int, IReadOnlySet{string}, HeldBuys)"/> runs on it, in the order they run: a route that
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

    /// <summary>The ship's hold has no room, or a market names no price or trade volume for the good.</summary>
    NoRoom = 3,

    /// <summary>The credits for cargo, less the trip's fuel, don't pay for a single unit.</summary>
    TooFewCredits = 4,

    /// <summary>
    /// Not even the first unit earns <c>Trade.MinProfitPerUnit</c> (D79), or the trip's units earn less than that a unit after
    /// fuel, or nothing (D14). For a trip that feeds a material the jump gate still needs, not even the first unit sells for
    /// what it cost (D90).
    /// </summary>
    NotLucrative = 5,

    /// <summary>The trip passes every check: it is one of the ship's routes.</summary>
    Lucrative = 6,

    /// <summary>
    /// The good is a material the jump gate still needs, and the buy market is in the gate's system: it is the gate's, and no
    /// trip buys it there (PLAN.md slice 6.45, D128).
    /// </summary>
    GateMaterial = 7,
}

/// <summary>
/// A route with a price gap, as one ship's checks found it (<see cref="TradeRoutePlanner.Judge(TradeMarketMap, ShipModel, long, int, IReadOnlySet{string}, HeldBuys)"/>): one market sells the good
/// for less than another pays for it (slice 2.18, D76).
/// </summary>
public sealed record TradeRouteJudgement
{
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
    /// The route's figures, as far as the checks got: the prices always; the fuel from <see cref="TradeRouteCheck.NoRoom"/> on;
    /// the units and the profit for a route that has units.
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
            .Select(good => good.Order(FurthestFirst).First())
            .OrderByDescending(judgement => judgement.Check)
            .ThenBy(judgement => judgement.Route.TradeSymbol, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The order <see cref="FurthestPerGood"/> picks a good's judgement by: the furthest through the checks first, then the largest
    /// price gap, then by ship and route. No two judgements of one pass are equal in it, as a ship judges each route once.
    /// </summary>
    public static IComparer<TradeRouteJudgement> FurthestFirst { get; } = Comparer<TradeRouteJudgement>.Create(static (x, y) =>
    {
        var byCheck = y.Check.CompareTo(x.Check);
        if (byCheck != 0)
        {
            return byCheck;
        }

        var byGap = y.PriceGap.CompareTo(x.PriceGap);
        if (byGap != 0)
        {
            return byGap;
        }

        var byShip = string.CompareOrdinal(x.Ship.Symbol, y.Ship.Symbol);
        return byShip != 0 ? byShip : string.CompareOrdinal(x.Route.Key, y.Route.Key);
    });

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
            TradeRouteCheck.NoRoom => NoRoom(),
            TradeRouteCheck.GateMaterial => $"{ship}: the jump gate still needs {Route.TradeSymbol}, so no trader buys it at {buy} (D128).",
            TradeRouteCheck.TooFewCredits => string.Create(
                CultureInfo.InvariantCulture,
                $"{ship}: a unit at {Route.BuyPrice:N0} and {Route.FuelCost:N0} for the trip's fuel cost {Route.BuyPrice + Route.FuelCost:N0}; {Credits:N0} credits are free for cargo."),
            TradeRouteCheck.NotLucrative when Route.Units == 0 => string.Create(
                CultureInfo.InvariantCulture,
                $"{ship}: a unit bought at {Route.BuyPrice:N0} and sold at {Route.SellPrice:N0} earns {Route.SellPrice - Route.BuyPrice:N0} before fuel; each must earn {MinProfitPerUnit:N0} (D14, D79)."),
            TradeRouteCheck.NotLucrative => string.Create(
                CultureInfo.InvariantCulture,
                $"{ship}: {Route.Units:N0} units earn {Route.Profit:N0} after {Route.FuelCost:N0} for fuel, {Route.Profit / Route.Units:N0} a unit; a trip must earn {MinProfitPerUnit:N0} a unit (D14)."),
            _ when Route.FeedsConstruction && Route.ConstructionStep > 1 => string.Create(
                CultureInfo.InvariantCulture,
                $"{ship}: feeds the jump gate's chain, {Route.ConstructionMaterial} {Route.ConstructionStep} steps from the gate, {Route.Units:N0} units for {Route.Profit:N0} after fuel (D90, D128)."),
            _ when Route.FeedsConstruction => string.Create(
                CultureInfo.InvariantCulture,
                $"{ship}: feeds the jump gate's {Route.ConstructionMaterial}, {Route.Units:N0} units for {Route.Profit:N0} after fuel (D89, D90)."),
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"{ship}: lucrative, {Route.Units:N0} units for {Route.Profit:N0} after fuel, {Route.Profit / Route.Units:N0} a unit; {Route.CreditsPerHour:N0} an hour, the trip taking about {TripTime.Minutes(Route.Seconds):N0} minutes (D95)."),
        };
    }

    /// <summary>The fuel aboard when the ship leaves the buy market: a full tank where it sells fuel, else what the flight there left.</summary>
    private int FuelAtBuyMarket(TradeMarketMap map)
        => map.SellsFuel(Route.BuyWaypointSymbol) || !TradeRoutePlanner.TryPlanFlight(map, Ship, Route.BuyWaypointSymbol, out var approach)
            ? Ship.FuelCapacity
            : approach.FuelLeft;

    private string NoRoom()
        => Ship.CargoCapacity - Ship.CargoCurrent <= 0
            ? $"{Ship.Symbol}: no room in its hold."
            : $"{Ship.Symbol}: {Route.BuyWaypointSymbol} or {Route.SellWaypointSymbol} names no price or trade volume for {Route.TradeSymbol}.";
}
