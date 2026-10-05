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
///   and once no market is short, the lowest supply there is, but never a market that has the ore ABUNDANT (D77). It
///   mines at an asteroid with a usable survey holding the ore, else at the asteroid nearest the market, and sells
///   there. Within a supply level, surveyed ores first, then the most a single extraction is expected to fetch. One
///   miner per sell market and ore, until every pair below ABUNDANT has one: then a drone shares a pair (D77);</item>
///   <item>only trips a ship can make in CRUISE count, through refuelling stops (the drones' 80-unit tanks keep
///   them near the markets that sell fuel): to the asteroid, and on to the market with the fuel left;</item>
///   <item>a market out of that reach that sells fuel counts too (slice 6.10c, D45): the ship drifts there first,
///   1 fuel whatever the distance, and mines from there, at an asteroid within a CRUISE round trip of it. Such a far
///   target ranks after every reachable one of its supply level;</item>
///   <item>a market that makes something from the ore comes before every other (D91): one that exchanges the ore, or
///   imports it without making anything from it, only pays for it. Such a wealth target ranks last, is mined for only when
///   no market that makes something from an ore is left to serve, shared ones included, and never counts as a market short
///   of its ore.</item>
/// </list>
/// </summary>
public static class MiningPlanner
{
    /// <summary>The supply at which a market has all of a good it wants: nobody gathers it for that market (D77).</summary>
    private const string AbundantSupply = "ABUNDANT";

    private static readonly IReadOnlySet<string> LowSupplyLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SCARCE", "LIMITED" };
    private static readonly IReadOnlySet<string> DemandTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IMPORT", "EXCHANGE" };

    /// <summary>A market's supply of a good, from the shortest: the order miners serve markets in (D28).</summary>
    private static readonly string[] SupplyLevels = ["SCARCE", "LIMITED", "MODERATE", "HIGH", AbundantSupply];

    /// <summary>The key of a mining opportunity: one miner per sell market and ore; a drone shares one only once every pair below ABUNDANT has a miner (D77).</summary>
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

    /// <summary>
    /// Whether a market has all of a good it wants (D77): ABUNDANT. No miner mines an ore for it then, and no siphoner
    /// siphons a gas for it. Asked on 2026-10-05: "They can mine until every mineral is ABUNDANT."
    /// </summary>
    /// <param name="supply">The supply level, as the API gives it.</param>
    /// <returns>True for ABUNDANT.</returns>
    public static bool IsAbundant(string supply) => string.Equals(supply, AbundantSupply, StringComparison.OrdinalIgnoreCase);

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
    /// Whether a surveyor can survey at an asteroid and fly on from there (B58): it reaches the asteroid, as
    /// <see cref="CanReach"/>, and from there, with the fuel left, a market that sells fuel, as a mining trip must get on to
    /// its market. Otherwise a survey there leaves it where no flight in CRUISE takes it anywhere.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="surveyor">The surveyor.</param>
    /// <param name="asteroid">Where it would survey.</param>
    /// <returns>True when it can survey there and fly on in CRUISE.</returns>
    public static bool CanSurveyAt(TradeMarketMap map, ShipModel surveyor, string asteroid)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(surveyor);

        var arrival = Arrival(map, surveyor, asteroid);
        return arrival.Reached
            && (map.SellsFuel(asteroid) || map.MarketWaypoints.Any(market => map.SellsFuel(market)
                && TradeRoutePlanner.TryPlanFlight(map, asteroid, market, arrival.Fuel, surveyor.FuelCapacity, out _)));
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
    /// nearest each market that buys it, among those whose traits yield it and where one of the miners could
    /// mine it for that market: a trip in CRUISE from where the miner is, to the asteroid and on to the market
    /// with the fuel left (any asteroid while there are no miners; B54: a miner that reached an asteroid but
    /// couldn't bring its ore back counted). One target per ore and asteroid, for the market that pays most of
    /// those it is nearest (D27). An ore needs a survey at an asteroid while it has fewer usable surveys there
    /// than <paramref name="stock"/>. Those come first: the contract's ore, then the fewest usable surveys, then
    /// the best paid. The targets with their stock follow, for the plan's view.
    /// </summary>
    /// <param name="context">The system.</param>
    /// <param name="contracts">The contract's ore and asteroid, while the contract plan mines; else none.</param>
    /// <param name="miners">The ships that mine with the surveys; a drone still drifting to a far market doesn't yet.</param>
    /// <param name="stock">The usable surveys to keep of each ore.</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<SurveyTarget> SurveyTargets(
        MiningContext context,
        IReadOnlyList<ContractOre> contracts,
        IReadOnlyList<ShipModel> miners,
        int stock)
        => SurveyTargets(context, contracts, miners, stock, []);

    /// <summary>
    /// What surveyors survey, as <see cref="SurveyTargets(MiningContext, IReadOnlyList{ContractOre}, IReadOnlyList{ShipModel}, int)"/>
    /// gives it, and each collection point's ores at its asteroid for its market (slice 6.18, D83): the drones parked there
    /// mine them for the shuttle, which no miner's trip there and back would show.
    /// </summary>
    /// <param name="context">The system.</param>
    /// <param name="contracts">The contract's ore and asteroid, while the contract plan mines; else none.</param>
    /// <param name="miners">The ships that mine with the surveys; a drone still drifting to a far market doesn't yet.</param>
    /// <param name="stock">The usable surveys to keep of each ore.</param>
    /// <param name="points">The system's collection points.</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<SurveyTarget> SurveyTargets(
        MiningContext context,
        IReadOnlyList<ContractOre> contracts,
        IReadOnlyList<ShipModel> miners,
        int stock,
        IReadOnlyList<CollectionPoint> points)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(miners);
        ArgumentNullException.ThrowIfNull(points);

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

        var arrivals = new Dictionary<(string Ship, string Asteroid), (bool Reached, int Fuel)>();
        bool Mines(ShipModel miner, string asteroid, string market)
        {
            if (!arrivals.TryGetValue((miner.Symbol, asteroid), out var arrival))
            {
                arrival = Arrival(map, miner, asteroid);
                arrivals[(miner.Symbol, asteroid)] = arrival;
            }

            return arrival.Reached && TradeRoutePlanner.TryPlanFlight(map, asteroid, market, arrival.Fuel, miner.FuelCapacity, out _);
        }

        // Surveys close to wherever the ore is sold (D27, refined): every market that buys it, not only the
        // one that pays most. Markets that share their nearest asteroid share its target.
        var sellable = new Dictionary<(string Ore, string Asteroid), SurveyTarget>();
        foreach (var ore in AsteroidDeposits.Ores.Order(StringComparer.Ordinal))
        {
            foreach (var (buyer, price) in Buyers(map, ore))
            {
                if (!TryFindNearestAsteroid(
                        map,
                        ore,
                        buyer,
                        asteroid => miners.Count == 0
                            || miners.Any(miner => Mines(miner, asteroid, buyer))
                            || points.Any(point => point.AsteroidSymbol.Equals(asteroid, StringComparison.OrdinalIgnoreCase)
                                && point.SellWaypointSymbol.Equals(buyer, StringComparison.OrdinalIgnoreCase)
                                && point.Ores.Contains(ore, StringComparer.OrdinalIgnoreCase)),
                        out var asteroid)
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
    /// short, the one with the lowest supply, even when it pays less; a market that has the ore ABUNDANT has all it wants,
    /// and isn't one (D77). Within a supply level, the targets in CRUISE reach first, then surveyed ores, then the most a
    /// single extraction is expected to fetch (the ore's share of the deposits times its price), then the nearest asteroid.
    /// A market out of the miner's CRUISE reach that sells fuel is a far target (D45, <see cref="MiningTarget.Far"/>): the
    /// miner drifts there first, and mines at the asteroid nearest it within a CRUISE round trip. Opportunities other miners
    /// hold are left out: one miner per sell market and ore (a drone shares one only when none is left,
    /// <see cref="SharedTargets"/>). The mining plan buys a drone only when its first trip here would serve a market short
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
            // D77: a market that has all of an ore it wants gets none mined for it.
            var ores = map.GoodsAt(market)
                .Where(good => AsteroidDeposits.Ores.Contains(good.Symbol) && IsDemanded(good) && !IsAbundant(good.Supply))
                .ToList();
            var driftsThere = ores.Count > 0 && CanDriftTo(map, miner, market);
            foreach (var good in ores)
            {
                // D91: whether the market makes something from the ore, or only pays for it.
                var feeds = map.MakesSomethingFrom(market, good.Symbol);
                foreach (var asteroid in surveyed)
                {
                    if (SurveySelection.TryPickBest(context.Surveys, asteroid, good.Symbol, context.Now, out var best)
                        && Gathers(asteroid, market))
                    {
                        Offer(new MiningTarget(good.Symbol, asteroid, market, good.SellPrice, SurveySelection.Share(best, good.Symbol), Surveyed: true, good.Supply)
                        {
                            FeedsProduction = feeds,
                        });
                    }
                }

                if (TryFindNearestAsteroid(map, good.Symbol, market, asteroid => Gathers(asteroid, market), out var nearest))
                {
                    Offer(Nearest(context, good, nearest, market, far: false) with { FeedsProduction = feeds });
                }

                // Out of CRUISE reach (D45): the miner drifts to the market, which sells fuel, and mines from there.
                if (driftsThere
                    && TryFindNearestAsteroid(map, good.Symbol, market, asteroid => RoundTrip(market, asteroid), out var far))
                {
                    Offer(Nearest(context, good, far, market, far: true) with { FeedsProduction = feeds });
                }
            }
        }

        return [.. candidates.Values
            .OrderBy(target => target, Comparer<MiningTarget>.Create(CompareBestFirst))
            .ThenBy(target => map.TryGetDistance(Position(miner), target.AsteroidSymbol, out var distance) ? distance : double.MaxValue)];
    }

    /// <summary>
    /// What a miner can mine, best first, with the ores no miner works on first (D48): of its SCARCE or LIMITED targets
    /// (D22) that no trip in <paramref name="covering"/> covers (D53), the nearest asteroid first ("near before far"); then
    /// the rest, in <see cref="MiningTargets(MiningContext, ShipModel, IReadOnlySet{string})"/>'s order (D28).
    /// </summary>
    /// <param name="context">The miner's system.</param>
    /// <param name="miner">The miner.</param>
    /// <param name="heldKeys">The opportunities other miners hold (<see cref="OpportunityKey"/>).</param>
    /// <param name="covering">The miners' trips, each covering its ore near the market it sells at.</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<MiningTarget> MiningTargets(MiningContext context, ShipModel miner, IReadOnlySet<string> heldKeys, IReadOnlyCollection<CoveringTrip> covering)
    {
        ArgumentNullException.ThrowIfNull(context);
        return UncoveredFirst(context.Map, miner, MiningTargets(context, miner, heldKeys), covering);
    }

    /// <summary>
    /// What a drone mines once every pair below ABUNDANT it could serve has a miner (D77, asked on 2026-10-05: "I'd like the
    /// miners to only mine, even if there is more profit in trading. They can mine until every mineral is ABUNDANT."): it
    /// shares a pair rather than trade. A pair whose market makes something from the ore first (D91), then the lowest supply
    /// (D28), a pair in CRUISE reach before one a drift away (D45), then the pair with the fewest miners, then in
    /// <see cref="MiningTargets(MiningContext, ShipModel, IReadOnlySet{string})"/>'s order. The mining plan buys no drone for a
    /// pair it would only share.
    /// </summary>
    /// <param name="context">The drone's system.</param>
    /// <param name="drone">The drone.</param>
    /// <param name="minersPerPair">How many miners work on each pair, by <see cref="OpportunityKey"/>; a pair it leaves out has none.</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<MiningTarget> SharedTargets(MiningContext context, ShipModel drone, IReadOnlyDictionary<string, int> minersPerPair)
    {
        ArgumentNullException.ThrowIfNull(minersPerPair);
        return [.. MiningTargets(context, drone, new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            .Select((target, rank) => (Target: target, Rank: rank))
            .OrderByDescending(entry => entry.Target.FeedsProduction)
            .ThenBy(entry => SupplyRank(entry.Target.Supply))
            .ThenBy(entry => entry.Target.Far)
            .ThenBy(entry => minersPerPair.GetValueOrDefault(entry.Target.Key))
            .ThenBy(entry => entry.Rank)
            .Select(entry => entry.Target)];
    }

    /// <summary>
    /// Puts the targets whose ore no miner works on first (D48): the SCARCE or LIMITED ones (D22) whose market makes
    /// something from the ore (D91) that no trip in <paramref name="covering"/> covers, as a trip covers its ore only at the
    /// markets its ship reaches in CRUISE from where it sells (D53, <see cref="CoveringTrip.Covers"/>); those in CRUISE reach
    /// before those a drift away (D45), the nearest asteroid first; then the rest, each group in the order given.
    /// </summary>
    /// <param name="map">The miner's system.</param>
    /// <param name="miner">The miner.</param>
    /// <param name="targets">Its targets, in D28's order (<see cref="MiningTargets(MiningContext, ShipModel, IReadOnlySet{string})"/>).</param>
    /// <param name="covering">The miners' trips, each covering its ore near the market it sells at.</param>
    /// <returns>The targets, best first.</returns>
    public static IReadOnlyList<MiningTarget> UncoveredFirst(TradeMarketMap map, ShipModel miner, IReadOnlyList<MiningTarget> targets, IReadOnlyCollection<CoveringTrip> covering)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(covering);

        var position = Position(miner);
        return [.. targets
            .Select((target, rank) => (
                Target: target,
                Rank: rank,
                Uncovered: target.FeedsProduction && target.LowSupply && !covering.Any(trip => trip.Covers(map, target.Ore, target.SellWaypointSymbol))))
            .OrderByDescending(entry => entry.Uncovered)
            .ThenBy(entry => entry.Uncovered && entry.Target.Far)
            .ThenBy(entry => !entry.Uncovered ? 0 : map.TryGetDistance(position, entry.Target.AsteroidSymbol, out var distance) ? distance : double.MaxValue)
            .ThenBy(entry => entry.Rank)
            .Select(entry => entry.Target)];
    }

    /// <summary>
    /// The SCARCE or LIMITED ores a ship could serve (D48), each once per area (D53): the ores of its low-supply targets
    /// (D22) whose market makes something from the ore (D91), whichever miner holds them, with the markets short of each
    /// grouped by the ship's CRUISE reach (<see cref="Areas"/>). An ore that no asteroid it can reach yields, or that no market
    /// it can carry it to is short of, isn't one. In X1-DC53 a drone's areas are the middle and B7: an ore short in both
    /// counts twice.
    /// </summary>
    /// <param name="context">The ship's system.</param>
    /// <param name="ship">The ship, as it is or as it would be bought.</param>
    /// <returns>The ores and their areas, by ore and first market.</returns>
    public static IReadOnlyList<MineralArea> ScarceOres(MiningContext context, ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ship);

        return Areas(
            context.Map,
            ship.FuelCapacity,
            MiningTargets(context, ship, new HashSet<string>(StringComparer.OrdinalIgnoreCase))
                .Where(target => target.FeedsProduction && target.LowSupply)
                .Select(target => new MineralArea(target.Ore, [target.SellWaypointSymbol])));
    }

    /// <summary>
    /// Whether a ship that works from a market covers another market (D53): it reaches it in CRUISE, through refuelling
    /// stops, leaving with a full tank. Asked on 2026-10-03: "A drone covers a mineral only for the markets it can reach in
    /// CRUISE from where it works (the middle, or B7)."
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="fuelCapacity">The ship's tank.</param>
    /// <param name="from">Where it works: the market it sells at.</param>
    /// <param name="market">The market it would cover.</param>
    /// <returns>True when a flight there exists; always for the market itself.</returns>
    public static bool Covers(TradeMarketMap map, int fuelCapacity, string from, string market)
        => TradeRoutePlanner.TryPlanFlight(map, from, market, fuelCapacity, fuelCapacity, out _);

    /// <summary>
    /// Groups each mineral's markets by area (D53): two markets share an area when a ship with this tank flies from one to
    /// the other in CRUISE (<see cref="Covers"/>), and so does a market that shares one with either. How the markets come
    /// grouped doesn't matter.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="fuelCapacity">The tank of the ships that would cover them.</param>
    /// <param name="markets">Each mineral's markets.</param>
    /// <returns>The areas, by mineral and first market; each area's markets by symbol.</returns>
    public static IReadOnlyList<MineralArea> Areas(TradeMarketMap map, int fuelCapacity, IEnumerable<MineralArea> markets)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(markets);

        var areas = new List<MineralArea>();
        foreach (var mineral in markets
            .GroupBy(area => area.Good, StringComparer.OrdinalIgnoreCase)
            .OrderBy(mineral => mineral.Key, StringComparer.Ordinal))
        {
            areas.AddRange(Groups(map, fuelCapacity, mineral.SelectMany(area => area.MarketSymbols))
                .Select(group => new MineralArea(mineral.Key, group)));
        }

        return areas;
    }

    /// <summary>
    /// Where a ship that can only survey should work (D54, D55): where most mining drones work, each area with drones with a
    /// survey ship of its own. Its own area is what it reaches in CRUISE from where it is; the drones beyond that group into
    /// areas as it would fly between them (<see cref="Areas"/>), and an area where another survey ship works, or is going,
    /// is taken. Of the areas not taken, it moves to the one with the most drones, when that has more than its own (a tie
    /// keeps it where it is), or has any while another survey ship works in its own: to the market in it, among those that
    /// sell fuel, where the most drones work, which it drifts to (D45). Asked on 2026-10-03: "Please add the option for the
    /// survey ship to get to the mining location without surveys", to work "where most drones mine"; and "Can we add that
    /// extra surveyor drones are bought to try and cover all areas with surveys?"
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="surveyor">The ship that can only survey.</param>
    /// <param name="droneWaypoints">Where each mining drone works, one entry a drone: its trip's market, else where it is.</param>
    /// <param name="otherSurveyorWaypoints">Where each other ship that can only survey works: where it is, or is going.</param>
    /// <param name="move">Where it would move to, with the drones there and in its own area.</param>
    /// <returns>True when it should move.</returns>
    public static bool TryFindBusierArea(
        TradeMarketMap map,
        ShipModel surveyor,
        IReadOnlyList<string> droneWaypoints,
        IReadOnlyList<string> otherSurveyorWaypoints,
        out SurveyorMove move)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(surveyor);
        ArgumentNullException.ThrowIfNull(droneWaypoints);
        ArgumentNullException.ThrowIfNull(otherSurveyorWaypoints);

        move = new SurveyorMove(string.Empty, 0, 0);
        var own = droneWaypoints.Count(waypoint => CanReach(map, surveyor, waypoint));
        var shared = otherSurveyorWaypoints.Any(waypoint => CanReach(map, surveyor, waypoint));
        var away = droneWaypoints.Where(waypoint => !CanReach(map, surveyor, waypoint)).ToList();
        var othersAway = otherSurveyorWaypoints.Where(waypoint => !CanReach(map, surveyor, waypoint)).ToList();
        int DronesAt(string waypoint) => away.Count(other => other.Equals(waypoint, StringComparison.OrdinalIgnoreCase));

        foreach (var (area, drones) in Groups(map, surveyor.FuelCapacity, away.Concat(othersAway))
            .Where(area => !area.Any(waypoint => othersAway.Contains(waypoint, StringComparer.OrdinalIgnoreCase)))
            .Select(area => (Area: area, Drones: area.Sum(DronesAt)))
            .Where(area => area.Drones > (shared ? 0 : own))
            .OrderByDescending(area => area.Drones)
            .ThenBy(area => area.Area[0], StringComparer.Ordinal))
        {
            var market = area
                .Where(waypoint => CanDriftTo(map, surveyor, waypoint))
                .OrderByDescending(DronesAt)
                .ThenBy(waypoint => waypoint, StringComparer.Ordinal)
                .FirstOrDefault() ?? string.Empty;
            if (market.Length > 0)
            {
                move = new SurveyorMove(market, drones, own, shared);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// How many areas the waypoints fall in, as a ship with this tank flies between them in CRUISE (D53's grouping): the
    /// areas with mining drones, each of which gets a survey ship of its own (D55).
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="fuelCapacity">The ship's tank.</param>
    /// <param name="waypoints">Where each mining drone works.</param>
    /// <returns>The number of areas; 0 without waypoints.</returns>
    public static int CountAreas(TradeMarketMap map, int fuelCapacity, IReadOnlyList<string> waypoints)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(waypoints);
        return Groups(map, fuelCapacity, waypoints).Count;
    }

    /// <summary>
    /// The low-supply opportunities of a system (D22): each market with an ore in low supply that it makes something from
    /// (D91), and the asteroid nearest it whose traits yield the ore. A ship that can reach the asteroid can take the
    /// opportunity. A market that only pays for the ore is mined for only when nothing else is left, so it is no opening that
    /// waits for a ship.
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
                .Where(good => AsteroidDeposits.Ores.Contains(good.Symbol) && IsLowSupply(good) && map.MakesSomethingFrom(market, good.Symbol))
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
    /// The far asteroids where a shuttle collects what parked drones mine (PLAN.md slice 6.18, D83, asked on 2026-10-05: "We
    /// park a light shuttle ... at the asteroid, and have the drones drop their ore into the light shuttle. When the light
    /// shuttle is full, it sells the ore at the market, then comes back."). For each market that sells fuel and buys an ore
    /// below ABUNDANT (D77) that no drone mines on a CRUISE round trip of it (D45): the asteroid nearest it that yields the
    /// ore, when a drone gets there from the market with a full tank and a shuttle flies there and back in CRUISE. One point
    /// per asteroid and market, with every such ore, and the SCARCE or LIMITED ones among them (D22), a drone each (D48). In
    /// X1-FJ91 on 2026-10-05: B44, 53 from B7, whose GOLD_ORE, SILVER_ORE and PLATINUM_ORE were SCARCE; a drone's 80-unit
    /// tank doesn't fly the 106 there and back.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="drone">A mining drone, for its tank; where it is doesn't matter.</param>
    /// <param name="shuttle">A collecting shuttle, for its tank.</param>
    /// <returns>The points, by market and asteroid.</returns>
    public static IReadOnlyList<CollectionPoint> CollectionPoints(TradeMarketMap map, ShipModel drone, ShipModel shuttle)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(drone);
        ArgumentNullException.ThrowIfNull(shuttle);

        var points = new Dictionary<(string Market, string Asteroid), (List<string> Ores, List<string> Scarce)>();
        foreach (var market in map.MarketWaypoints.Where(map.SellsFuel).Order(StringComparer.Ordinal))
        {
            foreach (var good in map.GoodsAt(market)
                .Where(good => AsteroidDeposits.Ores.Contains(good.Symbol) && IsDemanded(good) && !IsAbundant(good.Supply))
                .OrderBy(good => good.Symbol, StringComparer.Ordinal))
            {
                // A drone mines it on a round trip of the market (D45): nobody needs to collect it.
                if (TryFindNearestAsteroid(map, good.Symbol, market, asteroid => IsWithinRoundTrip(map, drone, market, asteroid), out _)
                    || !TryFindNearestAsteroid(
                        map,
                        good.Symbol,
                        market,
                        asteroid => TradeRoutePlanner.TryPlanFlight(map, market, asteroid, drone.FuelCapacity, drone.FuelCapacity, out _)
                            && IsWithinRoundTrip(map, shuttle, market, asteroid),
                        out var asteroid))
                {
                    continue;
                }

                if (!points.TryGetValue((market, asteroid), out var point))
                {
                    point = ([], []);
                    points[(market, asteroid)] = point;
                }

                point.Ores.Add(good.Symbol);
                if (IsLowSupply(good))
                {
                    point.Scarce.Add(good.Symbol);
                }
            }
        }

        return [.. points
            .OrderBy(entry => entry.Key.Market, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key.Asteroid, StringComparer.Ordinal)
            .Select(entry => new CollectionPoint(entry.Key.Asteroid, entry.Key.Market, entry.Value.Ores, entry.Value.Scarce))];
    }

    /// <summary>
    /// Orders two mining targets (<see cref="MiningTargets"/>): the one whose market makes something from the ore first
    /// (D91), then the lower supply (D28), then the one in CRUISE reach before a far one (D45), then surveyed first, then the
    /// most an extraction is expected to fetch, then by key.
    /// </summary>
    /// <param name="x">One target.</param>
    /// <param name="y">The other target.</param>
    /// <returns>Less than 0 when <paramref name="x"/> is the better target.</returns>
    public static int CompareBestFirst(MiningTarget x, MiningTarget y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        var feeds = y.FeedsProduction.CompareTo(x.FeedsProduction);
        if (feeds != 0)
        {
            return feeds;
        }

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

    /// <summary>
    /// Groups waypoints by area (D53): two share an area when a ship with this tank flies from one to the other in CRUISE
    /// (<see cref="Covers"/>), and so does a waypoint that shares one with either. Each group by symbol, the groups by their
    /// first.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<string>> Groups(TradeMarketMap map, int fuelCapacity, IEnumerable<string> waypoints)
    {
        var groups = new List<List<string>>();
        foreach (var waypoint in waypoints.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            var joined = groups
                .Where(group => group.Any(other => Covers(map, fuelCapacity, other, waypoint) || Covers(map, fuelCapacity, waypoint, other)))
                .ToList();
            var area = new List<string> { waypoint };
            foreach (var group in joined)
            {
                area.AddRange(group);
                groups.Remove(group);
            }

            groups.Add(area);
        }

        return [.. groups
            .Select(group => (IReadOnlyList<string>)[.. group.Order(StringComparer.Ordinal)])
            .OrderBy(group => group[0], StringComparer.Ordinal)];
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

    /// <summary>
    /// Whether a market within one tank of a waypoint buys a good, at any price: a full tank flies there in CRUISE without a
    /// refuelling stop. What a mining trip keeps of the other ores it gets (D71, asked on 2026-10-04: "only throw out
    /// minerals that they cannot sell within a single tank of fuel"); <see cref="IsSellableFrom"/>, D33's rule, also counts
    /// markets a refuelling stop away.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="ship">The miner.</param>
    /// <param name="from">Where it mines.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <returns>True when a market within one tank buys it.</returns>
    public static bool IsSellableWithinOneTank(TradeMarketMap map, ShipModel ship, string from, string tradeSymbol)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);
        return map.MarketWaypoints.Any(market =>
            map.TryGetGood(market, tradeSymbol, out var good)
            && good.SellPrice > 0
            && TradeRoutePlanner.TryPlanFlight(map, from, market, ship.FuelCapacity, ship.FuelCapacity, out var flight)
            && flight.Stops.Count <= 1);
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

    /// <summary>
    /// Whether the sell market makes something from the ore (D91, <see cref="TradeMarketMap.MakesSomethingFrom"/>). A market
    /// that exchanges the ore, or imports it without making anything from it, only pays for it: such a target is a wealth
    /// trade, ranks after every other and never counts as a market short of its ore. True unless set.
    /// </summary>
    public bool FeedsProduction { get; init; } = true;

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

/// <summary>
/// A trip that works on a mineral, an ore or a gas (D48): what it gathers, where it sells it, and the tank of its ship,
/// which says where the trip covers the mineral (D53).
/// </summary>
public sealed record CoveringTrip
{
    /// <summary>Creates a covering trip.</summary>
    /// <param name="Good">The ore or gas the trip gathers.</param>
    /// <param name="SellWaypointSymbol">Where it sells it.</param>
    /// <param name="FuelCapacity">The tank of the ship on the trip.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public CoveringTrip(string Good, string SellWaypointSymbol, int FuelCapacity)
    {
        this.Good = Good;
        this.SellWaypointSymbol = SellWaypointSymbol;
        this.FuelCapacity = FuelCapacity;
    }

    /// <summary>The ore or gas the trip gathers.</summary>
    public required string Good { get; init; }

    /// <summary>Where it sells it.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>The tank of the ship on the trip.</summary>
    public required int FuelCapacity { get; init; }

    /// <summary>
    /// Whether the trip covers a mineral at a market (D53): it gathers the mineral, and its ship reaches the market in
    /// CRUISE from where it sells (<see cref="MiningPlanner.Covers"/>). A drone that mines for B7 doesn't cover the middle;
    /// the command ship's 400-unit tank reaches both.
    /// </summary>
    /// <param name="map">The system.</param>
    /// <param name="good">The ore or gas.</param>
    /// <param name="market">The market short of it.</param>
    /// <returns>True when the trip covers the mineral there.</returns>
    public bool Covers(TradeMarketMap map, string good, string market)
        => Good.Equals(good, StringComparison.OrdinalIgnoreCase) && MiningPlanner.Covers(map, FuelCapacity, SellWaypointSymbol, market);
}

/// <summary>
/// A mineral, an ore or a gas, and the markets of one area that buy it (D53): markets a drone flies between in CRUISE. The
/// coverage tier counts one drone for each, and the role board keeps one gathering it (D48).
/// </summary>
public sealed record MineralArea
{
    /// <summary>Creates a mineral's area.</summary>
    /// <param name="Good">The ore or gas.</param>
    /// <param name="MarketSymbols">The markets, by symbol.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MineralArea(string Good, IReadOnlyList<string> MarketSymbols)
    {
        this.Good = Good;
        this.MarketSymbols = MarketSymbols;
    }

    /// <summary>The ore or gas.</summary>
    public required string Good { get; init; }

    /// <summary>The markets, by symbol.</summary>
    public required IReadOnlyList<string> MarketSymbols { get; init; }
}

/// <summary>
/// A far asteroid where a shuttle collects what parked drones mine, and the market it sells at (PLAN.md slice 6.18, D83,
/// <see cref="MiningPlanner.CollectionPoints"/>).
/// </summary>
public sealed record CollectionPoint
{
    /// <summary>Creates a collection point.</summary>
    /// <param name="AsteroidSymbol">Where the drones are parked.</param>
    /// <param name="SellWaypointSymbol">Where the shuttle sells, and the drones and the shuttle fill their tanks.</param>
    /// <param name="Ores">The market's ores below ABUNDANT that only this point serves, by symbol.</param>
    /// <param name="ScarceOres">Those the market has SCARCE or LIMITED (D22), by symbol: a drone is kept for each (D48).</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public CollectionPoint(string AsteroidSymbol, string SellWaypointSymbol, IReadOnlyList<string> Ores, IReadOnlyList<string> ScarceOres)
    {
        this.AsteroidSymbol = AsteroidSymbol;
        this.SellWaypointSymbol = SellWaypointSymbol;
        this.Ores = Ores;
        this.ScarceOres = ScarceOres;
    }

    /// <summary>Where the drones are parked.</summary>
    public required string AsteroidSymbol { get; init; }

    /// <summary>Where the shuttle sells, and the drones and the shuttle fill their tanks.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>The market's ores below ABUNDANT that only this point serves, by symbol.</summary>
    public required IReadOnlyList<string> Ores { get; init; }

    /// <summary>Those the market has SCARCE or LIMITED (D22), by symbol: a drone is kept for each (D48).</summary>
    public required IReadOnlyList<string> ScarceOres { get; init; }

    /// <summary>The drones the point wants: one per SCARCE or LIMITED ore (D48).</summary>
    public int DronesWanted => ScarceOres.Count;

    /// <summary>The point's key: its market and asteroid.</summary>
    public string Key => $"{SellWaypointSymbol}|{AsteroidSymbol}".ToUpperInvariant();
}

/// <summary>
/// Where a ship that can only survey moves to (D54, D55, <see cref="MiningPlanner.TryFindBusierArea"/>): a market in an area
/// with no survey ship of its own, where more mining drones work than in its own, or any while it shares its own.
/// </summary>
public sealed record SurveyorMove
{
    /// <summary>Creates a move.</summary>
    /// <param name="MarketSymbol">The market it drifts to: one that sells fuel.</param>
    /// <param name="Drones">The mining drones that work in that area.</param>
    /// <param name="OwnDrones">The mining drones that work in its own.</param>
    /// <param name="Shared">Whether another survey ship works in its own area (D55).</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SurveyorMove(string MarketSymbol, int Drones, int OwnDrones, bool Shared = false)
    {
        this.MarketSymbol = MarketSymbol;
        this.Drones = Drones;
        this.OwnDrones = OwnDrones;
        this.Shared = Shared;
    }

    /// <summary>The market it drifts to: one that sells fuel.</summary>
    public required string MarketSymbol { get; init; }

    /// <summary>The mining drones that work in that area.</summary>
    public required int Drones { get; init; }

    /// <summary>The mining drones that work in its own.</summary>
    public required int OwnDrones { get; init; }

    /// <summary>Whether another survey ship works in its own area (D55).</summary>
    public bool Shared { get; init; }
}
