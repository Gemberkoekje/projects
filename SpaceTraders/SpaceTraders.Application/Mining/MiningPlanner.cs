using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Mining;

/// <summary>
/// The survey and mining choices (PLAN.md slice 6.4), without any I/O, so the plans and their tests decide
/// alike:
/// <list type="bullet">
///   <item>a surveyor surveys the contract's ore at the contract's asteroid first; otherwise an ore a market
///   in the system buys, at the asteroid nearest the market that pays most for it. Ores without a usable
///   survey there come first, then the best paid;</item>
///   <item>a miner serves the market shortest of an ore first (D28): SCARCE, then LIMITED (low supply, D22),
///   and once no market is short, the lowest supply there is. It mines at an asteroid with a usable survey
///   holding the ore, else at the asteroid nearest the market, and sells there. Within a supply level,
///   surveyed ores first, then the most a single extraction is expected to fetch;</item>
///   <item>only trips a ship can make in CRUISE count, through refuelling stops (the drones' 80-unit tanks keep
///   them near the markets that sell fuel): to the asteroid, and on to the market with the fuel left;</item>
///   <item>a market out of that reach that sells fuel counts too (slice 6.10c, D45): the ship drifts there first,
///   1 fuel whatever the distance, and mines from there, at an asteroid within a CRUISE round trip of it. Such a far
///   target ranks after every reachable one of its supply level.</item>
/// </list>
/// </summary>
public static class MiningPlanner
{
    private static readonly IReadOnlySet<string> LowSupplyLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SCARCE", "LIMITED" };
    private static readonly IReadOnlySet<string> DemandTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IMPORT", "EXCHANGE" };

    /// <summary>A market's supply of a good, from the shortest: the order miners serve markets in (D28).</summary>
    private static readonly string[] SupplyLevels = ["SCARCE", "LIMITED", "MODERATE", "HIGH", "ABUNDANT"];

    /// <summary>The key of a mining opportunity: one miner per sell market and ore.</summary>
    /// <param name="sellWaypointSymbol">Where the ore is sold.</param>
    /// <param name="ore">The ore.</param>
    /// <returns>The key, in upper case.</returns>
    public static string OpportunityKey(string sellWaypointSymbol, string ore)
        => $"{sellWaypointSymbol}|{ore}".ToUpperInvariant();

    /// <summary>Whether a market has a good in low supply and takes it (D22): SCARCE or LIMITED, imported or exchanged.</summary>
    /// <param name="good">The good as last seen at the market.</param>
    /// <returns>True when mining it for that market is an opportunity.</returns>
    public static bool IsLowSupply(TradeGoodSnapshot good)
    {
        ArgumentNullException.ThrowIfNull(good);
        return IsDemanded(good) && IsLowSupply(good.Supply);
    }

    /// <summary>Whether a supply level is low (D22): SCARCE or LIMITED.</summary>
    /// <param name="supply">The supply level, as the API gives it.</param>
    /// <returns>True for SCARCE and LIMITED.</returns>
    public static bool IsLowSupply(string supply) => LowSupplyLevels.Contains(supply ?? string.Empty);

    /// <summary>Where a supply level stands, from the shortest: SCARCE is 0, an unknown level comes last (D28).</summary>
    /// <param name="supply">The supply level, as the API gives it.</param>
    /// <returns>The rank.</returns>
    public static int SupplyRank(string supply)
    {
        var rank = Array.FindIndex(SupplyLevels, level => level.Equals(supply, StringComparison.OrdinalIgnoreCase));
        return rank < 0 ? SupplyLevels.Length : rank;
    }

    /// <summary>Where a ship is, or, in transit, where it is going.</summary>
    /// <param name="ship">The ship.</param>
    /// <returns>The waypoint.</returns>
    public static string Position(ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return ship.LocalStatus == ShipLocalStatus.InTransit && !string.IsNullOrWhiteSpace(ship.DestWaypointSymbol)
            ? ship.DestWaypointSymbol
            : ship.WaypointSymbol ?? string.Empty;
    }

    /// <summary>
    /// Whether a ship can fly to a waypoint, through refuelling stops: where it is (or goes) sells fuel, it
    /// leaves with a full tank, else with the fuel aboard.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="ship">The ship.</param>
    /// <param name="destination">Where it would go.</param>
    /// <returns>True when a flight there exists.</returns>
    public static bool CanReach(TradeMarketMap map, ShipModel ship, string destination)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);

        return Arrival(map, ship, destination).Reached;
    }

    /// <summary>
    /// Whether a ship would drift to a market to gather from there (D45): it can't reach the market in CRUISE, it has the 1
    /// fuel a drift burns, and the market sells fuel, as the ship flies on in CRUISE from there.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="ship">The miner or siphoner.</param>
    /// <param name="market">Where it would drift to and sell.</param>
    /// <returns>True when the ship would drift there.</returns>
    public static bool CanDriftTo(TradeMarketMap map, ShipModel ship, string market)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);

        return ship.FuelCapacity > 0 && ship.FuelCurrent > 0 && map.SellsFuel(market) && !CanReach(map, ship, market);
    }

    /// <summary>
    /// Whether a ship that fills its tank at a market can fly from there to a source and back in CRUISE, with the fuel it
    /// has left at the source, or a full tank where the source sells fuel (D45: "only targets whose asteroid is within a
    /// CRUISE round trip of that market").
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="ship">The miner or siphoner.</param>
    /// <param name="market">Where it fills its tank and sells.</param>
    /// <param name="source">Where it fills its hold: an asteroid or a gas giant.</param>
    /// <returns>True when both flights exist.</returns>
    public static bool IsWithinRoundTrip(TradeMarketMap map, ShipModel ship, string market, string source)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);

        return TradeRoutePlanner.TryPlanFlight(map, market, source, ship.FuelCapacity, ship.FuelCapacity, out var outward)
            && TradeRoutePlanner.TryPlanFlight(map, source, market, map.SellsFuel(source) ? ship.FuelCapacity : outward.FuelLeft, ship.FuelCapacity, out _);
    }

    /// <summary>
    /// Whether a ship can take an opening: it reaches the source in CRUISE, or would drift to the market and gather from
    /// there (D45, <see cref="CanDriftTo"/>, <see cref="IsWithinRoundTrip"/>).
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="ship">The miner or siphoner.</param>
    /// <param name="source">Where the opening is gathered: an asteroid or a gas giant.</param>
    /// <param name="market">Where it is sold.</param>
    /// <returns>True when the ship can take the opening.</returns>
    public static bool CanTake(TradeMarketMap map, ShipModel ship, string source, string market)
        => CanReach(map, ship, source) || (CanDriftTo(map, ship, market) && IsWithinRoundTrip(map, ship, market, source));

    /// <summary>
    /// What surveyors survey: the contract's ore, and each ore a market in the system buys, at the asteroid
    /// nearest each market that buys it, among those whose traits yield it and that one of the miners can
    /// reach (any asteroid while there are no miners): one target per ore and asteroid, for the market that
    /// pays most of those it is nearest (D27). An ore needs a survey at an asteroid while it has fewer usable
    /// surveys there than <paramref name="stock"/>. Those come first: the contract's ore, then the fewest
    /// usable surveys, then the best paid. The targets with their stock follow, for the plan's view.
    /// </summary>
    /// <param name="context">The system.</param>
    /// <param name="contracts">The contract's ore and asteroid, while the contract plan mines; else none.</param>
    /// <param name="miners">The ships that mine with the surveys.</param>
    /// <param name="stock">The usable surveys to keep of each ore.</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<SurveyTarget> SurveyTargets(
        MiningContext context,
        IReadOnlyList<ContractOre> contracts,
        IReadOnlyList<ShipModel> miners,
        int stock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(miners);

        var map = context.Map;
        var targets = new List<SurveyTarget>();
        foreach (var contract in contracts)
        {
            var usable = SurveySelection.CountUsable(context.Surveys, contract.AsteroidSymbol, contract.Ore, context.Now);
            targets.Add(new SurveyTarget(
                contract.Ore,
                contract.AsteroidSymbol,
                contract.DestinationSymbol,
                0,
                ForContract: true,
                usable,
                NeedsSurvey: usable < stock));
        }

        // Surveys close to wherever the ore is sold (D27, refined): every market that buys it, not only the
        // one that pays most. Markets that share their nearest asteroid share its target.
        var sellable = new Dictionary<(string Ore, string Asteroid), SurveyTarget>();
        foreach (var ore in AsteroidDeposits.Ores.Order(StringComparer.Ordinal))
        {
            foreach (var (buyer, price) in Buyers(map, ore))
            {
                if (!TryFindNearestAsteroid(map, ore, buyer, asteroid => miners.Count == 0 || miners.Any(miner => CanReach(map, miner, asteroid)), out var asteroid)
                    || (sellable.TryGetValue((ore, asteroid), out var known) && known.SellPrice >= price))
                {
                    continue;
                }

                var usable = SurveySelection.CountUsable(context.Surveys, asteroid, ore, context.Now);
                sellable[(ore, asteroid)] = new SurveyTarget(ore, asteroid, buyer, price, ForContract: false, usable, NeedsSurvey: usable < stock);
            }
        }

        targets.AddRange(sellable.Values);

        // The contract's ore came first whatever surveys there were, so the only surveyor surveyed for it
        // without end (D27).
        return [.. targets
            .OrderByDescending(target => target.NeedsSurvey)
            .ThenByDescending(target => target.ForContract)
            .ThenBy(target => target.UsableSurveys)
            .ThenByDescending(target => target.SellPrice)
            .ThenBy(target => target.Ore, StringComparer.Ordinal)];
    }

    /// <summary>
    /// What a miner can mine, best first (D28): for every market that buys an ore, mined at an asteroid with a
    /// usable survey holding it, or else at the asteroid nearest the market, and sold there. The markets shortest
    /// of their ore come first, so a miner serves a SCARCE market before a LIMITED one, and once no market is
    /// short, the one with the lowest supply, even when it pays less. Within a supply level, the targets in CRUISE
    /// reach first, then surveyed ores, then the most a single extraction is expected to fetch (the ore's share of
    /// the deposits times its price), then the nearest asteroid. A market out of the miner's CRUISE reach that sells
    /// fuel is a far target (D45, <see cref="MiningTarget.Far"/>): the miner drifts there first, and mines at the
    /// asteroid nearest it within a CRUISE round trip. Opportunities other miners hold are left out: one miner per
    /// sell market and ore. The mining plan buys a drone only when its first trip here would serve a market short
    /// of its ore.
    /// </summary>
    /// <param name="context">The miner's system.</param>
    /// <param name="miner">The miner.</param>
    /// <param name="heldKeys">The opportunities other miners hold (<see cref="OpportunityKey"/>).</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<MiningTarget> MiningTargets(MiningContext context, ShipModel miner, IReadOnlySet<string> heldKeys)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(miner);
        ArgumentNullException.ThrowIfNull(heldKeys);

        var map = context.Map;
        var candidates = new Dictionary<string, MiningTarget>(StringComparer.OrdinalIgnoreCase);
        void Offer(MiningTarget target)
        {
            if (heldKeys.Contains(target.Key))
            {
                return;
            }

            if (!candidates.TryGetValue(target.Key, out var known) || CompareBestFirst(target, known) < 0)
            {
                candidates[target.Key] = target;
            }
        }

        var arrivals = new Dictionary<string, (bool Reached, int Fuel)>(StringComparer.OrdinalIgnoreCase);
        (bool Reached, int Fuel) ArrivalAt(string asteroid)
        {
            if (!arrivals.TryGetValue(asteroid, out var arrival))
            {
                arrival = Arrival(map, miner, asteroid);
                arrivals[asteroid] = arrival;
            }

            return arrival;
        }

        // The trip in CRUISE: to the asteroid, and on to the market with the fuel left there (slice 6.10c).
        bool Gathers(string asteroid, string market)
            => ArrivalAt(asteroid) is { Reached: true } arrival
                && TradeRoutePlanner.TryPlanFlight(map, asteroid, market, arrival.Fuel, miner.FuelCapacity, out _);

        // A far market's ores share its asteroids: each round trip is worked out once.
        var roundTrips = new Dictionary<(string Market, string Asteroid), bool>();
        bool RoundTrip(string market, string asteroid)
        {
            if (!roundTrips.TryGetValue((market, asteroid), out var within))
            {
                within = IsWithinRoundTrip(map, miner, market, asteroid);
                roundTrips[(market, asteroid)] = within;
            }

            return within;
        }

        var surveyed = context.Surveys
            .Select(survey => survey.WaypointSymbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(asteroid => IsExtractable(map, asteroid) && ArrivalAt(asteroid).Reached)
            .ToList();
        foreach (var market in map.MarketWaypoints.Order(StringComparer.Ordinal))
        {
            var ores = map.GoodsAt(market).Where(good => AsteroidDeposits.Ores.Contains(good.Symbol) && IsDemanded(good)).ToList();
            var driftsThere = ores.Count > 0 && CanDriftTo(map, miner, market);
            foreach (var good in ores)
            {
                foreach (var asteroid in surveyed)
                {
                    if (SurveySelection.TryPickBest(context.Surveys, asteroid, good.Symbol, context.Now, out var best)
                        && Gathers(asteroid, market))
                    {
                        Offer(new MiningTarget(good.Symbol, asteroid, market, good.SellPrice, SurveySelection.Share(best, good.Symbol), Surveyed: true, good.Supply));
                    }
                }

                if (TryFindNearestAsteroid(map, good.Symbol, market, asteroid => Gathers(asteroid, market), out var nearest))
                {
                    Offer(Nearest(context, good, nearest, market, far: false));
                }

                // Out of CRUISE reach (D45): the miner drifts to the market, which sells fuel, and mines from there.
                if (driftsThere
                    && TryFindNearestAsteroid(map, good.Symbol, market, asteroid => RoundTrip(market, asteroid), out var far))
                {
                    Offer(Nearest(context, good, far, market, far: true));
                }
            }
        }

        return [.. candidates.Values
            .OrderBy(target => target, Comparer<MiningTarget>.Create(CompareBestFirst))
            .ThenBy(target => map.TryGetDistance(Position(miner), target.AsteroidSymbol, out var distance) ? distance : double.MaxValue)];
    }

    /// <summary>
    /// What a miner can mine, best first, with the ores no miner works on first (D48): of its SCARCE or LIMITED targets
    /// (D22) whose ore isn't in <paramref name="coveredOres"/>, the nearest asteroid first ("near before far"); then the
    /// rest, in <see cref="MiningTargets(MiningContext, ShipModel, IReadOnlySet{string})"/>'s order (D28).
    /// </summary>
    /// <param name="context">The miner's system.</param>
    /// <param name="miner">The miner.</param>
    /// <param name="heldKeys">The opportunities other miners hold (<see cref="OpportunityKey"/>).</param>
    /// <param name="coveredOres">The ores a miner's trip works on.</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<MiningTarget> MiningTargets(MiningContext context, ShipModel miner, IReadOnlySet<string> heldKeys, IReadOnlySet<string> coveredOres)
    {
        ArgumentNullException.ThrowIfNull(context);
        return UncoveredFirst(context.Map, miner, MiningTargets(context, miner, heldKeys), coveredOres);
    }

    /// <summary>
    /// Puts the targets whose ore no miner works on first (D48): the SCARCE or LIMITED ones (D22) whose ore isn't in
    /// <paramref name="coveredOres"/>, those in CRUISE reach before those a drift away (D45), the nearest asteroid first;
    /// then the rest, each group in the order given.
    /// </summary>
    /// <param name="map">The miner's system.</param>
    /// <param name="miner">The miner.</param>
    /// <param name="targets">Its targets, in D28's order (<see cref="MiningTargets(MiningContext, ShipModel, IReadOnlySet{string})"/>).</param>
    /// <param name="coveredOres">The ores a miner's trip works on.</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<MiningTarget> UncoveredFirst(TradeMarketMap map, ShipModel miner, IReadOnlyList<MiningTarget> targets, IReadOnlySet<string> coveredOres)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(coveredOres);

        var position = Position(miner);
        return [.. targets
            .Select((target, rank) => (Target: target, Rank: rank, Uncovered: target.LowSupply && !coveredOres.Contains(target.Ore)))
            .OrderByDescending(entry => entry.Uncovered)
            .ThenBy(entry => entry.Uncovered && entry.Target.Far)
            .ThenBy(entry => !entry.Uncovered ? 0 : map.TryGetDistance(position, entry.Target.AsteroidSymbol, out var distance) ? distance : double.MaxValue)
            .ThenBy(entry => entry.Rank)
            .Select(entry => entry.Target)];
    }

    /// <summary>
    /// The SCARCE or LIMITED ores a ship could serve (D48): the ores of its low-supply targets (D22), whichever miner
    /// holds them. An ore that no asteroid it can reach yields, or that no market it can carry it to is short of, isn't
    /// one.
    /// </summary>
    /// <param name="context">The ship's system.</param>
    /// <param name="ship">The ship, as it is or as it would be bought.</param>
    /// <returns>The ores, by symbol.</returns>
    public static IReadOnlySet<string> ScarceOres(MiningContext context, ShipModel ship)
        => MiningTargets(context, ship, new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            .Where(target => target.LowSupply)
            .Select(target => target.Ore)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The low-supply opportunities of a system (D22): each market with an ore in low supply, and the asteroid
    /// nearest it whose traits yield the ore. A ship that can reach the asteroid can take the opportunity.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <returns>The opportunities, by market and ore.</returns>
    public static IReadOnlyList<MiningOpportunity> LowSupplyOpportunities(TradeMarketMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var opportunities = new List<MiningOpportunity>();
        foreach (var market in map.MarketWaypoints.Order(StringComparer.Ordinal))
        {
            foreach (var good in map.GoodsAt(market)
                .Where(good => AsteroidDeposits.Ores.Contains(good.Symbol) && IsLowSupply(good))
                .OrderBy(good => good.Symbol, StringComparer.Ordinal))
            {
                if (TryFindNearestAsteroid(map, good.Symbol, market, _ => true, out var asteroid))
                {
                    opportunities.Add(new MiningOpportunity(good.Symbol, asteroid, market, good.SellPrice));
                }
            }
        }

        return opportunities;
    }

    /// <summary>
    /// Orders two mining targets (<see cref="MiningTargets"/>): the lower supply first (D28), then the one in CRUISE
    /// reach before a far one (D45), then surveyed first, then the most an extraction is expected to fetch, then by key.
    /// </summary>
    /// <param name="x">One target.</param>
    /// <param name="y">The other target.</param>
    /// <returns>Less than 0 when <paramref name="x"/> is the better target.</returns>
    public static int CompareBestFirst(MiningTarget x, MiningTarget y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        var supply = SupplyRank(x.Supply).CompareTo(SupplyRank(y.Supply));
        if (supply != 0)
        {
            return supply;
        }

        var far = x.Far.CompareTo(y.Far);
        if (far != 0)
        {
            return far;
        }

        var surveyed = y.Surveyed.CompareTo(x.Surveyed);
        if (surveyed != 0)
        {
            return surveyed;
        }

        var value = y.ExpectedValue.CompareTo(x.ExpectedValue);
        return value != 0 ? value : string.CompareOrdinal(x.Key, y.Key);
    }

    /// <summary>Every market in the system that buys an ore, with what it pays, by symbol.</summary>
    private static IEnumerable<(string Market, long Price)> Buyers(TradeMarketMap map, string ore)
    {
        foreach (var market in map.MarketWaypoints.Order(StringComparer.Ordinal))
        {
            if (map.TryGetGood(market, ore, out var good) && good.SellPrice > 0)
            {
                yield return (market, good.SellPrice);
            }
        }
    }

    /// <summary>The extractable asteroid nearest a waypoint whose traits yield the ore, among those <paramref name="allowed"/> lets through.</summary>
    private static bool TryFindNearestAsteroid(TradeMarketMap map, string ore, string near, Func<string, bool> allowed, out string asteroid)
    {
        asteroid = map.Waypoints
            .Where(waypoint => AsteroidDeposits.CanYield(waypoint, ore))
            .Select(waypoint => (waypoint.Symbol, Distance: map.TryGetDistance(near, waypoint.Symbol, out var distance) ? distance : double.MaxValue))
            .Where(candidate => candidate.Distance < double.MaxValue)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Symbol, StringComparer.Ordinal)
            .Select(candidate => candidate.Symbol)
            .FirstOrDefault(allowed) ?? string.Empty;
        return asteroid.Length > 0;
    }

    /// <summary>
    /// Whether a ship, with a full tank where it mines or siphons, can carry its hold to the market. The siphon
    /// plan (slice 6.7) asks the same of a gas giant.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="ship">The miner or siphoner.</param>
    /// <param name="source">Where it fills its hold: an asteroid or a gas giant.</param>
    /// <param name="market">Where it would sell.</param>
    /// <returns>True when a flight there exists.</returns>
    public static bool CanSellFrom(TradeMarketMap map, ShipModel ship, string source, string market)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return TradeRoutePlanner.TryPlanFlight(map, source, market, ship.FuelCapacity, ship.FuelCapacity, out _);
    }

    /// <summary>
    /// Whether a market the ship can carry a good to from a waypoint buys it, at any price: what a siphon (D33) and a
    /// spare-time trip (slice 6.8) keep of what they get there. What no such market buys would fill the hold for good.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="ship">The ship.</param>
    /// <param name="from">Where it fills its hold.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <returns>True when a market it can carry the good to buys it.</returns>
    public static bool IsSellableFrom(TradeMarketMap map, ShipModel ship, string from, string tradeSymbol)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.MarketWaypoints.Any(market =>
            map.TryGetGood(market, tradeSymbol, out var good)
            && good.SellPrice > 0
            && CanSellFrom(map, ship, from, market));
    }

    /// <summary>Whether a market takes a good: it imports or exchanges it, at a price. Gases too (slice 6.7).</summary>
    /// <param name="good">The good as last seen at the market.</param>
    /// <returns>True when the market buys the good.</returns>
    public static bool IsDemanded(TradeGoodSnapshot good)
    {
        ArgumentNullException.ThrowIfNull(good);
        return good.SellPrice > 0 && DemandTypes.Contains(good.Type);
    }

    private static bool IsExtractable(TradeMarketMap map, string waypointSymbol)
        => map.Waypoints.Any(waypoint => waypoint.Symbol.Equals(waypointSymbol, StringComparison.OrdinalIgnoreCase)
            && AsteroidDeposits.IsExtractable(waypoint.Type));

    /// <summary>Without a survey, an extraction yields any of the asteroid's ores, about equally often.</summary>
    private static double UnguidedShare(TradeMarketMap map, string asteroid)
    {
        var waypoint = map.Waypoints.FirstOrDefault(candidate => candidate.Symbol.Equals(asteroid, StringComparison.OrdinalIgnoreCase));
        var ores = waypoint is null ? 0 : AsteroidDeposits.OresAt(waypoint).Count;
        return ores == 0 ? 0 : 1.0 / ores;
    }

    /// <summary>A market's ore mined at an asteroid: with the best usable survey there for it, else unguided.</summary>
    private static MiningTarget Nearest(MiningContext context, TradeGoodSnapshot good, string asteroid, string market, bool far)
    {
        var isSurveyed = SurveySelection.TryPickBest(context.Surveys, asteroid, good.Symbol, context.Now, out var best);
        var share = isSurveyed ? SurveySelection.Share(best, good.Symbol) : UnguidedShare(context.Map, asteroid);
        return new MiningTarget(good.Symbol, asteroid, market, good.SellPrice, share, isSurveyed, good.Supply, far);
    }

    /// <summary>
    /// Whether a ship gets to a waypoint in CRUISE, through refuelling stops, and the fuel it has there: a full tank where
    /// the waypoint sells fuel. It leaves where it is (or goes) with a full tank where that sells fuel, else with the fuel
    /// aboard.
    /// </summary>
    internal static (bool Reached, int Fuel) Arrival(TradeMarketMap map, ShipModel ship, string destination)
    {
        var from = Position(ship);
        var fuel = map.SellsFuel(from) ? ship.FuelCapacity : ship.FuelCurrent;
        return TradeRoutePlanner.TryPlanFlight(map, from, destination, fuel, ship.FuelCapacity, out var flight)
            ? (true, map.SellsFuel(destination) ? ship.FuelCapacity : flight.FuelLeft)
            : (false, 0);
    }
}

/// <summary>The contract's ore, the asteroid it is mined at and where it is delivered.</summary>
public sealed record ContractOre
{
    /// <summary>Creates the contract's mining target.</summary>
    /// <param name="Ore">The ore the contract wants.</param>
    /// <param name="AsteroidSymbol">Where the contract plan mines it.</param>
    /// <param name="DestinationSymbol">Where it is delivered.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractOre(string Ore, string AsteroidSymbol, string DestinationSymbol)
    {
        this.Ore = Ore;
        this.AsteroidSymbol = AsteroidSymbol;
        this.DestinationSymbol = DestinationSymbol;
    }

    /// <summary>The ore the contract wants.</summary>
    public required string Ore { get; init; }

    /// <summary>Where the contract plan mines it.</summary>
    public required string AsteroidSymbol { get; init; }

    /// <summary>Where it is delivered.</summary>
    public required string DestinationSymbol { get; init; }
}

/// <summary>Something to survey: an ore, the asteroid to survey for it, and the market it is for.</summary>
public sealed record SurveyTarget
{
    /// <summary>Creates a survey target.</summary>
    /// <param name="Ore">The ore surveyed for.</param>
    /// <param name="AsteroidSymbol">The asteroid to survey.</param>
    /// <param name="BuyerSymbol">The market the ore goes to: the contract's destination, or the market that pays most.</param>
    /// <param name="SellPrice">What that market pays per unit; 0 for the contract.</param>
    /// <param name="ForContract">Whether the contract wants the ore.</param>
    /// <param name="UsableSurveys">How many usable surveys of the asteroid hold the ore already.</param>
    /// <param name="NeedsSurvey">Whether that is fewer than the stock to keep (D27).</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SurveyTarget(string Ore, string AsteroidSymbol, string BuyerSymbol, long SellPrice, bool ForContract, int UsableSurveys, bool NeedsSurvey)
    {
        this.Ore = Ore;
        this.AsteroidSymbol = AsteroidSymbol;
        this.BuyerSymbol = BuyerSymbol;
        this.SellPrice = SellPrice;
        this.ForContract = ForContract;
        this.UsableSurveys = UsableSurveys;
        this.NeedsSurvey = NeedsSurvey;
    }

    /// <summary>The ore surveyed for.</summary>
    public required string Ore { get; init; }

    /// <summary>The asteroid to survey.</summary>
    public required string AsteroidSymbol { get; init; }

    /// <summary>The market the ore goes to: the contract's destination, or the market that pays most.</summary>
    public required string BuyerSymbol { get; init; }

    /// <summary>What that market pays per unit; 0 for the contract.</summary>
    public required long SellPrice { get; init; }

    /// <summary>Whether the contract wants the ore.</summary>
    public required bool ForContract { get; init; }

    /// <summary>How many usable surveys of the asteroid hold the ore already.</summary>
    public required int UsableSurveys { get; init; }

    /// <summary>Whether that is fewer than the stock to keep: the ore needs a survey (D27).</summary>
    public required bool NeedsSurvey { get; init; }

    /// <summary>Whether a usable survey of the asteroid holds the ore already.</summary>
    public bool HasUsableSurvey => UsableSurveys > 0;
}

/// <summary>A mining trip a miner could take: where it mines which ore, and where it sells it.</summary>
public sealed record MiningTarget
{
    /// <summary>Creates a mining target.</summary>
    /// <param name="Ore">The ore.</param>
    /// <param name="AsteroidSymbol">Where it is mined.</param>
    /// <param name="SellWaypointSymbol">Where it is sold.</param>
    /// <param name="SellPrice">What that market pays per unit, as last seen.</param>
    /// <param name="Share">The share of extractions expected to yield the ore: the best survey's, or one ore of the asteroid's without one.</param>
    /// <param name="Surveyed">Whether a usable survey of the asteroid holds the ore.</param>
    /// <param name="Supply">The sell market's supply of the ore, as last seen (SCARCE to ABUNDANT).</param>
    /// <param name="Far">Whether the market is out of the miner's CRUISE reach, so the miner drifts there first (D45).</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MiningTarget(string Ore, string AsteroidSymbol, string SellWaypointSymbol, long SellPrice, double Share, bool Surveyed, string Supply, bool Far = false)
    {
        this.Ore = Ore;
        this.AsteroidSymbol = AsteroidSymbol;
        this.SellWaypointSymbol = SellWaypointSymbol;
        this.SellPrice = SellPrice;
        this.Share = Share;
        this.Surveyed = Surveyed;
        this.Supply = Supply;
        this.Far = Far;
    }

    /// <summary>The ore.</summary>
    public required string Ore { get; init; }

    /// <summary>Where it is mined.</summary>
    public required string AsteroidSymbol { get; init; }

    /// <summary>Where it is sold.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>What that market pays per unit, as last seen.</summary>
    public required long SellPrice { get; init; }

    /// <summary>The share of extractions expected to yield the ore.</summary>
    public required double Share { get; init; }

    /// <summary>Whether a usable survey of the asteroid holds the ore.</summary>
    public required bool Surveyed { get; init; }

    /// <summary>The sell market's supply of the ore, as last seen (SCARCE to ABUNDANT).</summary>
    public required string Supply { get; init; }

    /// <summary>
    /// Whether the market is out of the miner's CRUISE reach (D45): the miner drifts there first, 1 fuel whatever the
    /// distance and about ten times slower, refuels, and mines from there in CRUISE.
    /// </summary>
    public bool Far { get; init; }

    /// <summary>Whether the sell market has the ore in low supply (D22): SCARCE or LIMITED.</summary>
    public bool LowSupply => MiningPlanner.IsLowSupply(Supply);

    /// <summary>What one extraction is expected to fetch per unit: the share times the price.</summary>
    public double ExpectedValue => Share * SellPrice;

    /// <summary>The opportunity's key: one miner per sell market and ore (<see cref="MiningPlanner.OpportunityKey"/>).</summary>
    public string Key => MiningPlanner.OpportunityKey(SellWaypointSymbol, Ore);
}

/// <summary>A market with an ore in low supply (D22), and the asteroid nearest it that yields the ore.</summary>
public sealed record MiningOpportunity
{
    /// <summary>Creates an opportunity.</summary>
    /// <param name="Ore">The ore.</param>
    /// <param name="AsteroidSymbol">The asteroid nearest the market whose traits yield it.</param>
    /// <param name="SellWaypointSymbol">The market.</param>
    /// <param name="SellPrice">What the market pays per unit, as last seen.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MiningOpportunity(string Ore, string AsteroidSymbol, string SellWaypointSymbol, long SellPrice)
    {
        this.Ore = Ore;
        this.AsteroidSymbol = AsteroidSymbol;
        this.SellWaypointSymbol = SellWaypointSymbol;
        this.SellPrice = SellPrice;
    }

    /// <summary>The ore.</summary>
    public required string Ore { get; init; }

    /// <summary>The asteroid nearest the market whose traits yield it.</summary>
    public required string AsteroidSymbol { get; init; }

    /// <summary>The market.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>What the market pays per unit, as last seen.</summary>
    public required long SellPrice { get; init; }

    /// <summary>The opportunity's key (<see cref="MiningPlanner.OpportunityKey"/>).</summary>
    public string Key => MiningPlanner.OpportunityKey(SellWaypointSymbol, Ore);
}
