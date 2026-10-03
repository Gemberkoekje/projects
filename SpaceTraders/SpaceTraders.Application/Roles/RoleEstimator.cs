using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Siphoning;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Roles;

/// <summary>Everything the role board's estimates read about one system (PLAN.md slice 6.9).</summary>
public sealed record RoleContext
{
    /// <summary>Creates the context of one evaluation.</summary>
    /// <param name="Mining">The system's map, its usable surveys, the credits and the time.</param>
    /// <param name="MinProfitPerUnit">The profit per unit, after fuel, a trade trip must earn (D14).</param>
    /// <param name="FuelReserveCredits">The credits cargo must leave for fuel (D24).</param>
    /// <param name="Chains">What a good is worth beyond its price, by the production chains (D39).</param>
    /// <param name="Rates">How fast each ship fills its hold.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RoleContext(MiningContext Mining, int MinProfitPerUnit, long FuelReserveCredits, ChainValues Chains, IGatheringRates Rates)
    {
        this.Mining = Mining;
        this.MinProfitPerUnit = MinProfitPerUnit;
        this.FuelReserveCredits = FuelReserveCredits;
        this.Chains = Chains;
        this.Rates = Rates;
    }

    /// <summary>The system's map, its usable surveys, the credits and the time.</summary>
    public required MiningContext Mining { get; init; }

    /// <summary>The profit per unit, after fuel, a trade trip must earn (D14).</summary>
    public required int MinProfitPerUnit { get; init; }

    /// <summary>The credits cargo must leave for fuel (D24).</summary>
    public required long FuelReserveCredits { get; init; }

    /// <summary>What a good is worth beyond its price, by the production chains (D39).</summary>
    public required ChainValues Chains { get; init; }

    /// <summary>How fast each ship fills its hold.</summary>
    public required IGatheringRates Rates { get; init; }

    /// <summary>The system's map.</summary>
    public TradeMarketMap Map => Mining.Map;
}

/// <summary>
/// What one role would earn a ship (PLAN.md slice 6.9, D38): the trips its plan would give the ship, each with what it
/// earns and how long it takes, so roles compare per hour, a ship's time being what it has to give.
/// </summary>
public sealed record RoleOption
{
    /// <summary>Creates an option.</summary>
    /// <param name="Role">The role the trip belongs to.</param>
    /// <param name="JobKey">What only one ship can take at a time: a trade route (D18), a mining or siphon opening.</param>
    /// <param name="Job">The trip, in a few words.</param>
    /// <param name="Credits">What the trip earns: after fuel, with the production chains' share (D39).</param>
    /// <param name="Seconds">How long it takes.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RoleOption(FleetRole Role, string JobKey, string Job, long Credits, double Seconds)
    {
        this.Role = Role;
        this.JobKey = JobKey;
        this.Job = Job;
        this.Credits = Credits;
        this.Seconds = Seconds;
    }

    /// <summary>The role the trip belongs to.</summary>
    public required FleetRole Role { get; init; }

    /// <summary>What only one ship can take at a time: a trade route (D18), a mining or siphon opening.</summary>
    public required string JobKey { get; init; }

    /// <summary>The trip, in a few words.</summary>
    public required string Job { get; init; }

    /// <summary>What the trip earns: after fuel, with the production chains' share (D39).</summary>
    public required long Credits { get; init; }

    /// <summary>How long it takes.</summary>
    public required double Seconds { get; init; }

    /// <summary>What the trip earns per hour.</summary>
    public double CreditsPerHour => Seconds <= 0 ? 0 : Credits * 3600.0 / Seconds;
}

/// <summary>
/// The role board's estimates (PLAN.md slice 6.9), without any I/O: for each role a ship could take, the trips its
/// plan would offer it, valued per hour.
/// <list type="bullet">
///   <item>trade: every lucrative route (D14) from where the ship is, its profit after fuel;</item>
///   <item>mine: every mining target (D28's targets), a full hold of the target ore, as the trip keeps only that,
///   filled at the ship's rate times the ore's share of the extractions (its survey's, or one of the asteroid's ores
///   without one), less the fuel there and on to the market;</item>
///   <item>siphon: every siphon target, a full hold of the gases a market buys, as the trip keeps them all (D33),
///   each at the best price it fetches from the gas giant, less the fuel;</item>
/// </list>
/// each with what the production chains add (D39). A trip's time is its flights in CRUISE, as the API reckons them
/// (15 seconds plus the distance times 25 over the engine's speed), <see cref="StopSeconds"/> at each landing, and for
/// mining and siphoning the cooldowns to fill the hold, half a tick after each. Surveying has no estimate: it comes
/// first (D38).
/// </summary>
public static class RoleEstimator
{
    /// <summary>The engine speed of a ship whose engine isn't cached yet: the Impulse Drive I of the drones and probes.</summary>
    public const int DefaultEngineSpeed = 9;

    /// <summary>Seconds at each landing: docking, a trade or a refuel, the market's refresh, orbiting again.</summary>
    public const double StopSeconds = 10;

    /// <summary>Seconds between ticks; a cooldown that ends waits half of one, on average, for the next step.</summary>
    public const double TickSeconds = 5;

    private static readonly IReadOnlySet<string> NoneHeld = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The trips a role would offer a ship, best per hour first.</summary>
    /// <param name="context">The ship's system.</param>
    /// <param name="ship">The ship.</param>
    /// <param name="role">The role.</param>
    /// <param name="limit">The most trips to return.</param>
    /// <returns>The trips; none for a role the ship can't take, and for surveying.</returns>
    public static IReadOnlyList<RoleOption> Options(RoleContext context, ShipModel ship, FleetRole role, int limit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ship);

        var here = AtPosition(ship);
        var options = role switch
        {
            FleetRole.Trade when ship.IsTradingCapable => TradeOptions(context, here),
            FleetRole.Mine when FleetRoles.CanMine(ship) => MineOptions(context, here),
            FleetRole.Siphon when FleetRoles.CanSiphon(ship) => SiphonOptions(context, here),
            _ => [],
        };

        return [.. options
            .Where(option => option.Credits > 0)
            .OrderByDescending(option => option.CreditsPerHour)
            .ThenBy(option => option.JobKey, StringComparer.Ordinal)
            .Take(Math.Max(0, limit))];
    }

    /// <summary>The seconds a flight takes in CRUISE, leg by leg through its stops, as the API reckons them.</summary>
    /// <param name="map">The system.</param>
    /// <param name="from">Where the flight starts.</param>
    /// <param name="stops">Where it lands, in order, the destination last.</param>
    /// <param name="speed">The engine's speed.</param>
    /// <returns>The seconds in flight; 0 to stay put.</returns>
    public static double FlightSeconds(TradeMarketMap map, string from, IReadOnlyList<string> stops, int speed)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stops);

        var seconds = 0.0;
        var at = from;
        foreach (var stop in stops)
        {
            if (map.TryGetDistance(at, stop, out var distance))
            {
                seconds += 15 + (Math.Max(1, Math.Round(distance)) * 25 / Math.Max(1, speed));
            }

            at = stop;
        }

        return seconds;
    }

    /// <summary>
    /// The ship as the estimates take it: docked where it is, or where it is going, with the fuel it has, so a flight
    /// from a market that sells fuel leaves with a full tank, as the navigation refuels there.
    /// </summary>
    private static ShipModel AtPosition(ShipModel ship)
        => ship with
        {
            WaypointSymbol = MiningPlanner.Position(ship),
            Status = "DOCKED",
            ArrivesAt = null,
            DestWaypointSymbol = null,
        };

    private static List<RoleOption> TradeOptions(RoleContext context, ShipModel ship)
    {
        var map = context.Map;
        var speed = FleetRoles.EngineSpeed(ship, DefaultEngineSpeed);
        var credits = Math.Max(0, context.Mining.Credits - context.FuelReserveCredits);
        var options = new List<RoleOption>();
        foreach (var route in TradeRoutePlanner.Rank(map, ship, credits, context.MinProfitPerUnit, NoneHeld))
        {
            if (!TradeRoutePlanner.TryPlanFlight(map, ship, route.BuyWaypointSymbol, out var approach)
                || !TradeRoutePlanner.TryPlanFlight(
                    map,
                    route.BuyWaypointSymbol,
                    route.SellWaypointSymbol,
                    map.SellsFuel(route.BuyWaypointSymbol) ? ship.FuelCapacity : approach.FuelLeft,
                    ship.FuelCapacity,
                    out var haul))
            {
                continue;
            }

            var seconds = FlightSeconds(map, ship.WaypointSymbol ?? string.Empty, approach.Stops, speed)
                + FlightSeconds(map, route.BuyWaypointSymbol, haul.Stops, speed)
                + (StopSeconds * (Math.Max(1, approach.Stops.Count) + haul.Stops.Count));
            var chain = route.Units * context.Chains.PerUnit(route.SellWaypointSymbol, route.TradeSymbol);
            options.Add(new RoleOption(
                FleetRole.Trade,
                "trade|" + route.Key,
                $"{route.TradeSymbol} from {route.BuyWaypointSymbol} to {route.SellWaypointSymbol}",
                route.Profit + (long)Math.Round(chain),
                seconds));
        }

        return options;
    }

    private static List<RoleOption> MineOptions(RoleContext context, ShipModel ship)
    {
        var rate = context.Rates.For(ship.Symbol, GatheringKind.Mining);
        var options = new List<RoleOption>();
        foreach (var target in MiningPlanner.MiningTargets(context.Mining, ship, NoneHeld))
        {
            var kept = rate.UnitsPerAction * target.Share;
            if (kept > 0
                && TryGatherTrip(context, ship, target.AsteroidSymbol, target.SellWaypointSymbol, kept, rate, out var seconds, out var fuel))
            {
                var perUnit = target.SellPrice + context.Chains.PerUnit(target.SellWaypointSymbol, target.Ore);
                options.Add(new RoleOption(
                    FleetRole.Mine,
                    "mine|" + target.Key,
                    $"{target.Ore} at {target.AsteroidSymbol} for {target.SellWaypointSymbol}",
                    (long)Math.Round((ship.CargoCapacity * perUnit) - fuel),
                    seconds));
            }
        }

        return options;
    }

    private static List<RoleOption> SiphonOptions(RoleContext context, ShipModel ship)
    {
        var map = context.Map;
        var rate = context.Rates.For(ship.Symbol, GatheringKind.Siphoning);
        var perUnitAt = new Dictionary<string, (double Share, double PerUnit)>(StringComparer.OrdinalIgnoreCase);
        var options = new List<RoleOption>();
        foreach (var target in SiphonPlanner.SiphonTargets(map, ship, NoneHeld))
        {
            if (!perUnitAt.TryGetValue(target.GasGiantSymbol, out var kept))
            {
                kept = KeptGases(context, ship, target.GasGiantSymbol);
                perUnitAt[target.GasGiantSymbol] = kept;
            }

            if (kept.Share > 0
                && TryGatherTrip(context, ship, target.GasGiantSymbol, target.SellWaypointSymbol, rate.UnitsPerAction * kept.Share, rate, out var seconds, out var fuel))
            {
                options.Add(new RoleOption(
                    FleetRole.Siphon,
                    "siphon|" + target.Key,
                    $"{target.Gas} at {target.GasGiantSymbol} for {target.SellWaypointSymbol}",
                    (long)Math.Round((ship.CargoCapacity * kept.PerUnit) - fuel),
                    seconds));
            }
        }

        return options;
    }

    /// <summary>
    /// What a siphon trip keeps at a gas giant (D33): every gas a market the ship can carry it to buys. Its share of the
    /// siphons, and what a unit of it fetches on average, each gas at the best price it gets, with the chains' share.
    /// </summary>
    private static (double Share, double PerUnit) KeptGases(RoleContext context, ShipModel ship, string gasGiantSymbol)
    {
        var map = context.Map;
        var giant = map.Waypoints.FirstOrDefault(waypoint => waypoint.Symbol.Equals(gasGiantSymbol, StringComparison.OrdinalIgnoreCase));
        var gases = giant is null ? [] : GasGiants.GasesAt(giant);
        var values = new List<double>();
        foreach (var gas in gases)
        {
            var best = map.MarketWaypoints
                .Where(market => map.TryGetGood(market, gas, out var good)
                    && good.SellPrice > 0
                    && MiningPlanner.CanSellFrom(map, ship, gasGiantSymbol, market))
                .Select(market => map.TryGetGood(market, gas, out var good) ? good.SellPrice + context.Chains.PerUnit(market, gas) : 0)
                .DefaultIfEmpty(0)
                .Max();
            if (best > 0)
            {
                values.Add(best);
            }
        }

        return gases.Count == 0 || values.Count == 0 ? (0, 0) : (values.Count / (double)gases.Count, values.Average());
    }

    /// <summary>
    /// A trip that fills the hold at a source and sells at a market: the flight there, the cooldowns to fill the hold at
    /// <paramref name="keptPerAction"/> units an extraction, and the flight on to the market, with their fuel.
    /// </summary>
    private static bool TryGatherTrip(
        RoleContext context,
        ShipModel ship,
        string source,
        string market,
        double keptPerAction,
        GatheringRate rate,
        out double seconds,
        out long fuel)
    {
        var map = context.Map;
        seconds = 0;
        fuel = 0;
        if (ship.CargoCapacity <= 0
            || !TradeRoutePlanner.TryPlanFlight(map, ship, source, out var approach)
            || !TradeRoutePlanner.TryPlanFlight(map, source, market, ship.FuelCapacity, ship.FuelCapacity, out var haul))
        {
            return false;
        }

        var speed = FleetRoles.EngineSpeed(ship, DefaultEngineSpeed);
        var actions = Math.Ceiling(ship.CargoCapacity / keptPerAction);
        seconds = FlightSeconds(map, ship.WaypointSymbol ?? string.Empty, approach.Stops, speed)
            + (actions * (rate.SecondsPerAction + (TickSeconds / 2)))
            + FlightSeconds(map, source, haul.Stops, speed)
            + (StopSeconds * (approach.Stops.Count + haul.Stops.Count));
        fuel = approach.FuelCost + haul.FuelCost;
        return true;
    }
}
