using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Siphoning;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

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
        TradeMap = Mining.Map;
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

    /// <summary>
    /// The fleet's trips under way, by ship (B67): a ship's trade estimate leaves out the routes the other ships' trade trips
    /// hold, and the goods their trips are on their way to buy at a market (D80), as the trading plan does. None unless set.
    /// </summary>
    public IReadOnlyDictionary<string, ShipGoal> Trips { get; init; } = new Dictionary<string, ShipGoal>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What trading has actually earned lately, per hour of a trader's time (D87, <see cref="TradeEarnings"/>): no trade
    /// estimate promises more. Infinite, no cap, unless set.
    /// </summary>
    public double TradeCreditsPerHourAtMost { get; init; } = double.PositiveInfinity;

    /// <summary>The system's map.</summary>
    public TradeMarketMap Map => Mining.Map;

    /// <summary>
    /// The map a ship's trade estimates come from (PLAN.md slice 6.29, D96): the systems within the trade reach, as the trading
    /// plan ranks a trader's routes by. The system's own map unless set.
    /// </summary>
    public TradeMarketMap TradeMap { get; init; }
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
    /// <param name="Credits">What the trip earns: after fuel, with the production chains' share (D39), at most as much again as the goods earn (D49).</param>
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

    /// <summary>What the trip earns: after fuel, with the production chains' share (D39), at most as much again as the goods earn (D49).</summary>
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
///   <item>trade: every lucrative route (D14) from where the ship is that the trading plan could give it, its profit after
///   fuel: none another trip holds, nor of a good another trip is on its way to buy at that market (B67, D80); at most what
///   trading has actually earned lately per hour (D87);</item>
///   <item>mine: every mining target (D28's targets), a full hold of the target ore, as the trip keeps only that,
///   filled at the ship's rate times the ore's share of the extractions (its survey's, or one of the asteroid's ores
///   without one), at the price the target's market pays, less the fuel there and on to the market;</item>
///   <item>siphon: every siphon target, a full hold of the gases a market buys, as the trip keeps them all (D33): its
///   own gas at the price its market pays (D49), each other gas at the best price it fetches from the gas giant, where
///   the trips after sell it, less the fuel;</item>
/// </list>
/// each with what the production chains add (D39), at most what the trip earns on a unit (D49): a gathered unit's
/// price there, a traded unit's margin. A trip's time is its flights in CRUISE, as the API reckons them
/// (15 seconds plus the distance times 25 over the engine's speed), <see cref="StopSeconds"/> at each landing: the trading
/// plan's own timing (<see cref="TripTime"/>, D95), a trade trip's being the route's <see cref="TradeRoute.Seconds"/>; and for
/// mining and siphoning the cooldowns to fill the hold, half a tick after each. A mining or siphon trip to a market out
/// of the ship's CRUISE reach drifts there first (slice 6.10c, D45): ten times as long as in CRUISE, and the 1 fuel it
/// burns is bought back there. Surveying has no estimate: it comes first (D38). Nor has building the jump gate, which
/// pays nothing and comes before the trips by profit (slice 6.6, D65).
/// </summary>
public static class RoleEstimator
{
    /// <summary>The engine speed of a ship whose engine isn't cached yet: the Impulse Drive I of the drones and probes.</summary>
    public const int DefaultEngineSpeed = TripTime.DefaultEngineSpeed;

    /// <summary>Seconds at each landing: docking, a trade or a refuel, the market's refresh, orbiting again.</summary>
    public const double StopSeconds = TripTime.StopSeconds;

    /// <summary>Seconds between ticks; a cooldown that ends waits half of one, on average, for the next step.</summary>
    public const double TickSeconds = 5;

    private static readonly IReadOnlySet<string> NoneHeld = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The trips a role would offer a ship, best per hour first.</summary>
    /// <param name="context">The ship's system.</param>
    /// <param name="ship">The ship.</param>
    /// <param name="role">The role.</param>
    /// <param name="limit">The most trips to return.</param>
    /// <returns>The trips; none for a role the ship can't take, for surveying and for building.</returns>
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

            // One option per job, its best: only one ship can take it (B67), and the others would crowd out other jobs.
            .GroupBy(option => option.JobKey, StringComparer.Ordinal)
            .Select(job => job.OrderByDescending(option => option.CreditsPerHour).ThenBy(option => option.Job, StringComparer.Ordinal).First())
            .OrderByDescending(option => option.CreditsPerHour)
            .ThenBy(option => option.JobKey, StringComparer.Ordinal)
            .Take(Math.Max(0, limit))];
    }

    /// <summary>
    /// The seconds a flight takes in CRUISE, leg by leg through its stops, as the API reckons them: the trading plan's timing
    /// (<see cref="TripTime.CruiseSeconds"/>, D95), so a role's rate and a route's rank agree.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="from">Where the flight starts.</param>
    /// <param name="stops">Where it lands, in order, the destination last.</param>
    /// <param name="speed">The engine's speed.</param>
    /// <returns>The seconds in flight; 0 to stay put.</returns>
    public static double FlightSeconds(TradeMarketMap map, string from, IReadOnlyList<string> stops, int speed)
        => TripTime.CruiseSeconds(map, from, stops, speed);

    /// <summary>
    /// The seconds a flight takes in DRIFT (slice 6.10c, D45), as the API reckons it: 15 seconds plus the distance times 250
    /// over the engine's speed, ten times what CRUISE takes.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="from">Where the flight starts.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="speed">The engine's speed.</param>
    /// <returns>The seconds in flight; 0 for a position the map doesn't know.</returns>
    public static double DriftSeconds(TradeMarketMap map, string from, string to, int speed)
    {
        ArgumentNullException.ThrowIfNull(map);

        return map.TryGetDistance(from, to, out var distance)
            ? 15 + (Math.Max(1, Math.Round(distance)) * 250 / Math.Max(1, speed))
            : 0;
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
        // Slice 6.29: the routes the trading plan would give the ship as a trader, across the systems in reach.
        var map = context.TradeMap;
        var credits = Math.Max(0, context.Mining.Credits - context.FuelReserveCredits);

        // B67: the routes the other ships' trips hold, and the goods they are on their way to buy at a market (D80), aren't this
        // ship's to take, as the trading plan has it.
        var others = context.Trips
            .Where(trip => !trip.Key.Equals(ship.Symbol, StringComparison.OrdinalIgnoreCase))
            .Select(trip => trip.Value)
            .Where(trip => trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            .ToList();
        var trades = others.OfType<TradeBetweenMarketsGoal>().ToList();
        var heldKeys = trades
            .Select(trip => TradeRoutePlanner.RouteKey(trip.TradeSymbol, trip.BuyWaypointSymbol, trip.SellWaypointSymbol))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var heldBuys = HeldBuys.Of(trades, others.OfType<SupplyConstructionGoal>());
        var options = new List<RoleOption>();
        foreach (var route in TradeRoutePlanner.Rank(map, ship, credits, context.MinProfitPerUnit, heldKeys, heldBuys))
        {
            // D95: the trip's time as the trading plan ranks it, from where the ship is.
            var seconds = route.Seconds;

            // The chains add at most the route's own margin a unit (D49).
            var chain = route.Units * context.Chains.PerUnitAtMost(route.SellWaypointSymbol, route.TradeSymbol, route.SellPrice - route.BuyPrice);
            var earns = route.Profit + (long)Math.Round(chain);

            // D87: no more an hour than trading has actually earned lately.
            var atMost = context.TradeCreditsPerHourAtMost * seconds / 3600;
            var job = $"{route.TradeSymbol} from {route.BuyWaypointSymbol} to {route.SellWaypointSymbol}";
            options.Add(new RoleOption(
                FleetRole.Trade,
                TradeJobKey(route),
                earns > atMost ? job + ", at most what trading earned lately (D87)" : job,
                earns > atMost ? (long)Math.Floor(atMost) : earns,
                seconds));
        }

        return options;
    }

    /// <summary>
    /// What only one ship can take at a time of a trade route (D80, B67): the good at its buy market, which one trip at a time
    /// buys, so the board gives no two ships the same good there for different sell markets.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>The job's key.</returns>
    internal static string TradeJobKey(TradeRoute route) => $"trade|{route.BuyWaypointSymbol}|{route.TradeSymbol}".ToUpperInvariant();

    private static List<RoleOption> MineOptions(RoleContext context, ShipModel ship)
    {
        var rate = context.Rates.For(ship.Symbol, GatheringKind.Mining);
        var options = new List<RoleOption>();
        foreach (var target in MiningPlanner.MiningTargets(context.Mining, ship, NoneHeld))
        {
            var kept = rate.UnitsPerAction * target.Share;
            if (kept > 0
                && TryGatherTrip(context, ship, target.AsteroidSymbol, target.SellWaypointSymbol, target.Far, kept, rate, out var seconds, out var fuel))
            {
                var perUnit = UnitValue(context, target.SellWaypointSymbol, target.Ore, target.SellPrice);
                options.Add(new RoleOption(
                    FleetRole.Mine,
                    "mine|" + target.Key,
                    Job(target.Ore, target.AsteroidSymbol, target.SellWaypointSymbol, target.Far),
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
        var gasesAt = new Dictionary<string, IReadOnlyList<(string Gas, double Value)>>(StringComparer.OrdinalIgnoreCase);
        var options = new List<RoleOption>();
        foreach (var target in SiphonPlanner.SiphonTargets(map, ship, NoneHeld))
        {
            if (!gasesAt.TryGetValue(target.GasGiantSymbol, out var gases))
            {
                gases = GasValues(context, ship, target.GasGiantSymbol);
                gasesAt[target.GasGiantSymbol] = gases;
            }

            var kept = KeptGases(context, target, gases);
            if (kept.Share > 0
                && TryGatherTrip(context, ship, target.GasGiantSymbol, target.SellWaypointSymbol, target.Far, rate.UnitsPerAction * kept.Share, rate, out var seconds, out var fuel))
            {
                options.Add(new RoleOption(
                    FleetRole.Siphon,
                    "siphon|" + target.Key,
                    Job(target.Gas, target.GasGiantSymbol, target.SellWaypointSymbol, target.Far),
                    (long)Math.Round((ship.CargoCapacity * kept.PerUnit) - fuel),
                    seconds));
            }
        }

        return options;
    }

    /// <summary>
    /// What each gas a gas giant yields counts for on the trips after a siphon trip, which sell the gases it keeps where
    /// each fetches most (D33): the most it counts for at a market the ship can carry it to from the gas giant, its price
    /// there with the chains' share at most as much again (<see cref="UnitValue"/>, D49); 0 for a gas no such market buys.
    /// </summary>
    private static IReadOnlyList<(string Gas, double Value)> GasValues(RoleContext context, ShipModel ship, string gasGiantSymbol)
    {
        var map = context.Map;
        var giant = map.Waypoints.FirstOrDefault(waypoint => waypoint.Symbol.Equals(gasGiantSymbol, StringComparison.OrdinalIgnoreCase));
        var gases = giant is null ? [] : GasGiants.GasesAt(giant);
        var values = new List<(string Gas, double Value)>();
        foreach (var gas in gases)
        {
            var best = map.MarketWaypoints
                .Where(market => map.TryGetGood(market, gas, out var good)
                    && good.SellPrice > 0
                    && MiningPlanner.CanSellFrom(map, ship, gasGiantSymbol, market))
                .Select(market => map.TryGetGood(market, gas, out var good) ? UnitValue(context, market, gas, good.SellPrice) : 0)
                .DefaultIfEmpty(0)
                .Max();
            values.Add((gas, best));
        }

        return values;
    }

    /// <summary>
    /// What a siphon trip keeps (D33): every gas a market the ship can carry it to buys. Its share of the siphons, and
    /// what a unit of it fetches on average: the trip's own gas where the trip sells it (D49), each other gas at the most
    /// it fetches on the trips after (<see cref="GasValues"/>).
    /// </summary>
    private static (double Share, double PerUnit) KeptGases(RoleContext context, SiphonTarget target, IReadOnlyList<(string Gas, double Value)> gases)
    {
        var values = gases
            .Select(gas => gas.Gas.Equals(target.Gas, StringComparison.OrdinalIgnoreCase)
                ? UnitValue(context, target.SellWaypointSymbol, target.Gas, target.SellPrice)
                : gas.Value)
            .Where(value => value > 0)
            .ToList();
        return gases.Count == 0 || values.Count == 0 ? (0, 0) : (values.Count / (double)gases.Count, values.Average());
    }

    /// <summary>
    /// What a unit of a gathered good counts for at the market it is sold at: its price there, with the chains' share,
    /// at most as much again (<see cref="ChainValues.PerUnitAtMost"/>, D49).
    /// </summary>
    private static double UnitValue(RoleContext context, string market, string good, long price)
        => price + context.Chains.PerUnitAtMost(market, good, price);

    /// <summary>A gathering trip in a few words; one that drifts to its market first says so (D45).</summary>
    private static string Job(string good, string source, string market, bool drift)
        => drift ? $"{good} at {source} for {market}, drifting there first" : $"{good} at {source} for {market}";

    /// <summary>
    /// A trip that fills the hold at a source and sells at a market: the flight there, the cooldowns to fill the hold at
    /// <paramref name="keptPerAction"/> units an extraction, and the flight on to the market, with their fuel. A trip that
    /// drifts (<paramref name="drift"/>, D45) first drifts to the market, out of the ship's CRUISE reach, lands, buys back
    /// the 1 fuel it burned, and leaves there with a full tank.
    /// </summary>
    private static bool TryGatherTrip(
        RoleContext context,
        ShipModel ship,
        string source,
        string market,
        bool drift,
        double keptPerAction,
        GatheringRate rate,
        out double seconds,
        out long fuel)
    {
        var map = context.Map;
        seconds = 0;
        fuel = 0;
        var speed = FleetRoles.EngineSpeed(ship, DefaultEngineSpeed);
        var departing = drift ? ship with { WaypointSymbol = market, FuelCurrent = ship.FuelCapacity } : ship;
        if (ship.CargoCapacity <= 0
            || !TradeRoutePlanner.TryPlanFlight(map, departing, source, out var approach)
            || !TradeRoutePlanner.TryPlanFlight(map, source, market, ship.FuelCapacity, ship.FuelCapacity, out var haul))
        {
            return false;
        }

        var actions = Math.Ceiling(ship.CargoCapacity / keptPerAction);
        seconds = (drift ? DriftSeconds(map, ship.WaypointSymbol ?? string.Empty, market, speed) + StopSeconds : 0)
            + FlightSeconds(map, departing.WaypointSymbol ?? string.Empty, approach.Stops, speed)
            + (actions * (rate.SecondsPerAction + (TickSeconds / 2)))
            + FlightSeconds(map, source, haul.Stops, speed)
            + (StopSeconds * (approach.Stops.Count + haul.Stops.Count));
        fuel = (drift ? map.FuelPrice(market) : 0) + approach.FuelCost + haul.FuelCost;
        return true;
    }
}
