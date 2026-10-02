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
///   <item>a miner mines a surveyed ore first: one a usable survey holds, sold where it fetches most;
///   otherwise an ore a market has in low supply (SCARCE or LIMITED, D22), mined at the asteroid nearest
///   that market and sold there. Among either, the most a single extraction is expected to fetch;</item>
///   <item>only asteroids a ship can reach count, through refuelling stops (the drones' 80-unit tanks keep
///   them near the markets that sell fuel).</item>
/// </list>
/// </summary>
public static class MiningPlanner
{
    private static readonly IReadOnlySet<string> LowSupplyLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SCARCE", "LIMITED" };
    private static readonly IReadOnlySet<string> DemandTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IMPORT", "EXCHANGE" };

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
        return good.SellPrice > 0 && DemandTypes.Contains(good.Type) && LowSupplyLevels.Contains(good.Supply);
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

        var from = Position(ship);
        var fuel = map.SellsFuel(from) ? ship.FuelCapacity : ship.FuelCurrent;
        return TradeRoutePlanner.TryPlanFlight(map, from, destination, fuel, ship.FuelCapacity, out _);
    }

    /// <summary>
    /// What surveyors survey: the contract's ore, and each ore a market in the system buys, at the asteroid
    /// nearest the market that pays most for it, among those whose traits yield it and that one of the miners
    /// can reach (any asteroid while there are no miners). An ore needs a survey while it has fewer usable
    /// surveys there than <paramref name="stock"/> (D27). Those come first: the contract's ore, then the ore
    /// with the fewest usable surveys, then the best paid. The ores with their stock follow, for the plan's view.
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

        foreach (var ore in AsteroidDeposits.Ores.Order(StringComparer.Ordinal))
        {
            if (!TryFindBestBuyer(map, ore, out var buyer, out var price)
                || !TryFindNearestAsteroid(map, ore, buyer, asteroid => miners.Count == 0 || miners.Any(miner => CanReach(map, miner, asteroid)), out var asteroid))
            {
                continue;
            }

            var usable = SurveySelection.CountUsable(context.Surveys, asteroid, ore, context.Now);
            targets.Add(new SurveyTarget(ore, asteroid, buyer, price, ForContract: false, usable, NeedsSurvey: usable < stock));
        }

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
    /// What a miner can mine, best first: surveyed ores, then ores in low supply (D22); among either, the
    /// most a single extraction is expected to fetch (the ore's share of the deposits times its price), then
    /// the nearest asteroid. Opportunities other miners hold are left out: one miner per sell market and ore.
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

        // Surveyed: every ore a usable survey holds at an asteroid the miner can reach, sold where it fetches most.
        foreach (var asteroid in context.Surveys.Select(survey => survey.WaypointSymbol).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsExtractable(map, asteroid) || !CanReach(map, miner, asteroid))
            {
                continue;
            }

            var ores = context.Surveys
                .Where(survey => survey.WaypointSymbol.Equals(asteroid, StringComparison.OrdinalIgnoreCase))
                .SelectMany(survey => survey.Deposits.Select(deposit => deposit.Symbol))
                .Where(AsteroidDeposits.Ores.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var ore in ores)
            {
                if (SurveySelection.TryPickBest(context.Surveys, asteroid, ore, context.Now, out var best)
                    && TryFindBestBuyer(map, ore, out var buyer, out var price, from: asteroid, miner))
                {
                    Offer(new MiningTarget(ore, asteroid, buyer, price, SurveySelection.Share(best, ore), Surveyed: true, LowSupply: IsLowSupplyAt(map, buyer, ore)));
                }
            }
        }

        // Low supply: mined at the asteroid nearest the market, and sold there.
        foreach (var market in map.MarketWaypoints.Order(StringComparer.Ordinal))
        {
            foreach (var good in map.GoodsAt(market).Where(good => AsteroidDeposits.Ores.Contains(good.Symbol) && IsLowSupply(good)))
            {
                if (!TryFindNearestAsteroid(map, good.Symbol, market, asteroid => CanReach(map, miner, asteroid) && CanSellFrom(map, miner, asteroid, market), out var asteroid))
                {
                    continue;
                }

                var surveyed = SurveySelection.TryPickBest(context.Surveys, asteroid, good.Symbol, context.Now, out var best);
                var share = surveyed ? SurveySelection.Share(best, good.Symbol) : UnguidedShare(map, asteroid);
                Offer(new MiningTarget(good.Symbol, asteroid, market, good.SellPrice, share, surveyed, LowSupply: true));
            }
        }

        return [.. candidates.Values
            .OrderBy(target => target, Comparer<MiningTarget>.Create(CompareBestFirst))
            .ThenBy(target => map.TryGetDistance(Position(miner), target.AsteroidSymbol, out var distance) ? distance : double.MaxValue)];
    }

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
    /// Orders two mining targets (<see cref="MiningTargets"/>): surveyed first, then the most an extraction is
    /// expected to fetch, then by key.
    /// </summary>
    /// <param name="x">One target.</param>
    /// <param name="y">The other target.</param>
    /// <returns>Less than 0 when <paramref name="x"/> is the better target.</returns>
    public static int CompareBestFirst(MiningTarget x, MiningTarget y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        var surveyed = y.Surveyed.CompareTo(x.Surveyed);
        if (surveyed != 0)
        {
            return surveyed;
        }

        var value = y.ExpectedValue.CompareTo(x.ExpectedValue);
        return value != 0 ? value : string.CompareOrdinal(x.Key, y.Key);
    }

    /// <summary>The market in the system that pays most for an ore; a tie goes to the first by symbol.</summary>
    private static bool TryFindBestBuyer(TradeMarketMap map, string ore, out string buyer, out long price)
    {
        buyer = string.Empty;
        price = 0;
        foreach (var market in map.MarketWaypoints.Order(StringComparer.Ordinal))
        {
            if (map.TryGetGood(market, ore, out var good) && good.SellPrice > price)
            {
                buyer = market;
                price = good.SellPrice;
            }
        }

        return price > 0;
    }

    /// <summary>The market a miner gets most at for an ore, from the asteroid, after the fuel to get there.</summary>
    private static bool TryFindBestBuyer(TradeMarketMap map, string ore, out string buyer, out long price, string from, ShipModel miner)
    {
        buyer = string.Empty;
        price = 0;
        var best = long.MinValue;
        foreach (var market in map.MarketWaypoints.Order(StringComparer.Ordinal))
        {
            if (!map.TryGetGood(market, ore, out var good)
                || good.SellPrice <= 0
                || !TradeRoutePlanner.TryPlanFlight(map, from, market, miner.FuelCapacity, miner.FuelCapacity, out var flight))
            {
                continue;
            }

            var net = ((long)good.SellPrice * Math.Max(1, miner.CargoCapacity)) - flight.FuelCost;
            if (net > best)
            {
                best = net;
                buyer = market;
                price = good.SellPrice;
            }
        }

        return price > 0;
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

    /// <summary>Whether a miner, with a full tank at the asteroid, can carry its hold to the market.</summary>
    private static bool CanSellFrom(TradeMarketMap map, ShipModel miner, string asteroid, string market)
        => TradeRoutePlanner.TryPlanFlight(map, asteroid, market, miner.FuelCapacity, miner.FuelCapacity, out _);

    private static bool IsExtractable(TradeMarketMap map, string waypointSymbol)
        => map.Waypoints.Any(waypoint => waypoint.Symbol.Equals(waypointSymbol, StringComparison.OrdinalIgnoreCase)
            && AsteroidDeposits.IsExtractable(waypoint.Type));

    private static bool IsLowSupplyAt(TradeMarketMap map, string market, string ore)
        => map.TryGetGood(market, ore, out var good) && IsLowSupply(good);

    /// <summary>Without a survey, an extraction yields any of the asteroid's ores, about equally often.</summary>
    private static double UnguidedShare(TradeMarketMap map, string asteroid)
    {
        var waypoint = map.Waypoints.FirstOrDefault(candidate => candidate.Symbol.Equals(asteroid, StringComparison.OrdinalIgnoreCase));
        var ores = waypoint is null ? 0 : AsteroidDeposits.OresAt(waypoint).Count;
        return ores == 0 ? 0 : 1.0 / ores;
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
    /// <param name="LowSupply">Whether the sell market has the ore in low supply (D22).</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MiningTarget(string Ore, string AsteroidSymbol, string SellWaypointSymbol, long SellPrice, double Share, bool Surveyed, bool LowSupply)
    {
        this.Ore = Ore;
        this.AsteroidSymbol = AsteroidSymbol;
        this.SellWaypointSymbol = SellWaypointSymbol;
        this.SellPrice = SellPrice;
        this.Share = Share;
        this.Surveyed = Surveyed;
        this.LowSupply = LowSupply;
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

    /// <summary>Whether the sell market has the ore in low supply (D22).</summary>
    public required bool LowSupply { get; init; }

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
