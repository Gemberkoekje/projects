using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Trading;

/// <summary>Everything a trade decision reads: the system's map, the credits and the minimum profit.</summary>
public sealed record TradeContext
{
    /// <summary>Creates the context of one decision.</summary>
    /// <param name="Map">The ship's system: positions, markets and production chains.</param>
    /// <param name="Credits">The credits on hand, as cached.</param>
    /// <param name="MinProfitPerUnit">The profit per unit, after fuel, a trip must earn (D14).</param>
    /// <param name="FuelReserveCredits">The credits cargo must leave, so that fuel can always be bought (D24).</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TradeContext(TradeMarketMap Map, long Credits, int MinProfitPerUnit, long FuelReserveCredits = 0)
    {
        this.Map = Map;
        this.Credits = Credits;
        this.MinProfitPerUnit = MinProfitPerUnit;
        this.FuelReserveCredits = FuelReserveCredits;
    }

    /// <summary>The ship's system: positions, markets and production chains; for the trading plan, the systems within its reach.</summary>
    public required TradeMarketMap Map { get; init; }

    /// <summary>The credits on hand, as cached.</summary>
    public required long Credits { get; init; }

    /// <summary>The profit per unit, after fuel, a trip must earn (D14).</summary>
    public required int MinProfitPerUnit { get; init; }

    /// <summary>
    /// The credits cargo must leave (D24, <c>Trade.FuelReserveCredits</c>): below them only fuel is bought,
    /// so a ship never holds cargo it can't afford to fly to its buyer.
    /// </summary>
    public required long FuelReserveCredits { get; init; }

    /// <summary>The credits a cargo purchase may use: those on hand above <see cref="FuelReserveCredits"/>.</summary>
    public long CreditsForCargo => Math.Max(0, Credits - FuelReserveCredits);

    /// <summary>
    /// Whether the jump gate's next load waits only for one of our ships, once the gate can be built, as the construction plan
    /// last said it (PLAN.md slice 6.52, D140, <see cref="PurchaseNeed.WaitsForShip"/>): the trading plan may then buy a light
    /// hauler for it. False unless set.
    /// </summary>
    public bool GateLoadWaitsForShip { get; init; }
}

/// <summary>Reads the <see cref="TradeContext"/> for a system from the cache.</summary>
public interface ITradeContextReader
{
    /// <summary>Reads what a trade decision in a system needs.</summary>
    /// <param name="systemSymbol">The system the ship is in.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The system's map, the credits and the minimum profit per unit.</returns>
    Task<TradeContext> ReadAsync(string systemSymbol, CancellationToken cancellationToken);

    /// <summary>
    /// Reads what a trade trip decides by in its system (PLAN.md slices 6.46 and 6.47, D131, D132): as <see cref="ReadAsync"/>,
    /// with the system's markets nobody has looked at within <c>Trade.MaxPriceAgeMinutes</c> held as predicted for now
    /// (<see cref="TradeMarketMap.PredictionWeights"/>), as the trading plan chose the trip by.
    /// </summary>
    /// <param name="systemSymbol">The system the ship is in.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The system's map, the credits and the minimum profit per unit.</returns>
    Task<TradeContext> ReadTradeAsync(string systemSymbol, CancellationToken cancellationToken);

    /// <summary>
    /// Reads what the trading plan's decisions need (PLAN.md slice 6.29, D96): the map of the systems within its reach of
    /// <paramref name="systemSymbol"/>, the ways between them through the built gates (<see cref="TradeMarketMap.Gates"/>), and
    /// which markets' prices are too old to choose a route by (<see cref="TradeMarketMap.StaleMarkets"/>).
    /// </summary>
    /// <param name="systemSymbol">The system the ship is in.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The map of the systems in reach, the credits and the minimum profit per unit.</returns>
    Task<TradeContext> ReadReachAsync(string systemSymbol, CancellationToken cancellationToken);

    /// <summary>
    /// The markets whose prices are recent enough to decide by (D96): seen with prices at most <c>Trade.MaxPriceAgeMinutes</c>
    /// ago. Mining abroad reads only those (PLAN.md slice 6.40, D122).
    /// </summary>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>Their waypoints, in every system.</returns>
    Task<IReadOnlySet<string>> FreshMarketsAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
/// <remarks>
/// Only the production chains may cost an API call, once per process (<see cref="ISupplyChainCache"/>);
/// everything else comes from the database. Without the chains no route counts as feeding production,
/// and routes are ranked by what they earn an hour alone (D95).
/// </remarks>
public sealed class TradeContextReader(
    IMarketRepository markets,
    IWaypointRepository waypoints,
    IAgentRepository agents,
    ISettingsRepository settings,
    ISupplyChainCache supplyChain,
    IConstructionSites constructionSites,
    ISpaceTradersPort port,
    IGateNetwork gates,
    ISystemRepository systems,
    IMarketPredictions predictions,
    IPredictionConfidence confidence,
    PurchaseNeeds purchaseNeeds,
    IPlanRepository plans) : ITradeContextReader
{
    /// <summary>The setting that holds the profit per unit, after fuel, a trip must earn (D14).</summary>
    public const string MinProfitPerUnitSetting = "Trade.MinProfitPerUnit";

    /// <summary>The setting that holds the credits cargo must leave, so fuel can always be bought (D24).</summary>
    public const string FuelReserveCreditsSetting = "Trade.FuelReserveCredits";

    /// <summary>
    /// The setting that holds the trade reach, in jumps (D96): a route buys within that many jumps of the ship's system and sells
    /// within that many of the buy market's (slice 6.29); the probes of the explored systems within it of home come before the
    /// drones and cargo ships that take turns, the others' last (slice 6.28, D97).
    /// </summary>
    public const string MaxHaulDistanceSetting = "Trade.MaxHaulDistance";

    /// <summary>The trade reach when <see cref="MaxHaulDistanceSetting"/> gives none, as it is seeded.</summary>
    public const int DefaultMaxHaulDistance = 5;

    /// <summary>
    /// The setting that holds how old, in minutes, a market's prices may be for a trade route to buy or sell there (D96), at home
    /// too.
    /// </summary>
    public const string MaxPriceAgeMinutesSetting = "Trade.MaxPriceAgeMinutes";

    /// <summary>The prices' age when <see cref="MaxPriceAgeMinutesSetting"/> gives none, as it is seeded.</summary>
    public const int DefaultMaxPriceAgeMinutes = 30;

    private const string AntimatterSymbol = "ANTIMATTER";

    /// <inheritdoc />
    public Task<TradeContext> ReadAsync(string systemSymbol, CancellationToken cancellationToken)
        => ReadSystemAsync(systemSymbol, predict: false, cancellationToken);

    /// <inheritdoc />
    public Task<TradeContext> ReadTradeAsync(string systemSymbol, CancellationToken cancellationToken)
        => ReadSystemAsync(systemSymbol, predict: true, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The systems the built gates reach from <paramref name="systemSymbol"/> within twice <c>Trade.MaxHaulDistance</c> jumps (the
    /// buy market within the reach of the ship's system, the sell market within the reach of the buy market's), each with its
    /// cached waypoints and markets. A market whose prices are older than <c>Trade.MaxPriceAgeMinutes</c>, or that was never seen
    /// with prices, is stale. The materials of the headquarters' jump gate are the only ones that count (D68), and only its
    /// system's markets feed them (D89).
    /// </remarks>
    public async Task<TradeContext> ReadReachAsync(string systemSymbol, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var reach = await settings.GetAsync<int>(MaxHaulDistanceSetting, cancellationToken) is var jumps and > 0 ? jumps : DefaultMaxHaulDistance;
        var network = await gates.ReadAsync(cancellationToken);
        var inReach = network is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { systemSymbol }
            : ExploreAtlas.Reachable(network, systemSymbol, now)
                .Where(system => system.Value <= 2 * reach)
                .Select(system => system.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var reachWaypoints = new List<WaypointCacheModel>();
        foreach (var system in inReach.Order(StringComparer.Ordinal))
        {
            reachWaypoints.AddRange(await waypoints.GetBySystemAsync(system, cancellationToken));
        }

        var allMarkets = await markets.GetAllSnapshotsAsync(cancellationToken);
        var maxAge = await MaxPriceAgeAsync(cancellationToken);
        var staleRecords = (await markets.GetAllFreshnessAsync(cancellationToken))
            .Where(market => inReach.Contains(market.SystemSymbol) && !IsFresh(market, now, maxAge))
            .ToList();
        var stale = staleRecords.Select(market => market.WaypointSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var basics = await ReadBasicsAsync(cancellationToken);
        var home = basics.HomeSystemSymbol;

        // D131, D132: the stale markets seen with prices are predicted for now, in every system, and no longer stale.
        var (reachMarkets, predicted) = await PredictAsync(
            [.. allMarkets.Where(market => inReach.Contains(market.SystemSymbol))],
            staleRecords,
            network,
            now,
            cancellationToken);
        stale.ExceptWith(predicted.Keys);
        var feedersFirst = await FeedersFirstAsync(now, cancellationToken);
        var map = new TradeMarketMap(reachWaypoints, reachMarkets, basics.Chains)
        {
            ConstructionMaterials = home.Length > 0 ? await MaterialsAsync(home, cancellationToken) : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ConstructionSystemSymbol = home,
            ConstructionFirst = feedersFirst,
            FeedersHeldBack = GateCanBeBuilt(now) && !feedersFirst,
            StaleMarkets = stale,
            PredictionWeights = predicted,
            Gates = network is null
                ? TradeGates.None
                : new TradeGates(
                    network,
                    now,
                    reach,
                    AntimatterPrices(allMarkets),
                    (await systems.GetAllAsync(cancellationToken))
                        .GroupBy(system => system.Symbol, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => (group.First().X, group.First().Y), StringComparer.OrdinalIgnoreCase),
                    await settings.GetAsync<long>(CreditReserve.FloorSetting, cancellationToken)),
        };

        return new TradeContext(map, basics.Credits, basics.MinProfitPerUnit, basics.FuelReserve)
        {
            GateLoadWaitsForShip = GateLoadWaitsForShip(now),
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> FreshMarketsAsync(CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var maxAge = await MaxPriceAgeAsync(cancellationToken);
        return (await markets.GetAllFreshnessAsync(cancellationToken))
            .Where(market => IsFresh(market, now, maxAge))
            .Select(market => market.WaypointSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The map of one system (<see cref="ReadAsync"/>); with <paramref name="predict"/>, the home system's markets nobody has
    /// looked at within the age held as predicted for now (<see cref="ReadTradeAsync"/>, D131).
    /// </summary>
    private async Task<TradeContext> ReadSystemAsync(string systemSymbol, bool predict, CancellationToken cancellationToken)
    {
        var systemWaypoints = await waypoints.GetBySystemAsync(systemSymbol, cancellationToken);
        IReadOnlyList<MarketSnapshot> systemMarkets = [.. (await markets.GetAllSnapshotsAsync(cancellationToken))
            .Where(market => market.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))];
        var basics = await ReadBasicsAsync(cancellationToken);
        IReadOnlyDictionary<string, double> predicted = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (predict)
        {
            var now = TimeProvider.System.GetUtcNow();
            var maxAge = await MaxPriceAgeAsync(cancellationToken);
            var staleRecords = (await markets.GetAllFreshnessAsync(cancellationToken))
                .Where(market => market.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase) && !IsFresh(market, now, maxAge))
                .ToList();
            if (staleRecords.Count > 0)
            {
                (systemMarkets, predicted) = await PredictAsync(systemMarkets, staleRecords, await gates.ReadAsync(cancellationToken), now, cancellationToken);
            }
        }

        var at = TimeProvider.System.GetUtcNow();
        var feedersFirst = await FeedersFirstAsync(at, cancellationToken);
        return new TradeContext(
            new TradeMarketMap(systemWaypoints, systemMarkets, basics.Chains)
            {
                ConstructionMaterials = await MaterialsAsync(systemSymbol, cancellationToken),
                ConstructionFirst = feedersFirst,
                FeedersHeldBack = GateCanBeBuilt(at) && !feedersFirst,
                PredictionWeights = predicted,
            },
            basics.Credits,
            basics.MinProfitPerUnit,
            basics.FuelReserve);
    }

    /// <summary>
    /// D131, D132: the markets, with each one nobody has looked at within the age, but that was seen with prices, as predicted for
    /// now (<see cref="IMarketPredictions"/>), and how far each is trusted (<see cref="IPredictionConfidence"/>, D133). Asked on
    /// 2026-11-08: "I'd like to expand the prediction logic to all systems." A market never seen with prices has nothing to
    /// predict from.
    /// </summary>
    private async Task<(IReadOnlyList<MarketSnapshot> Markets, IReadOnlyDictionary<string, double> Predicted)> PredictAsync(
        IReadOnlyList<MarketSnapshot> candidates,
        IReadOnlyList<MarketFreshnessRecord> staleRecords,
        ExplorePlanState? network,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lastLooks = staleRecords
            .Where(record => record.HasPrices)
            .GroupBy(record => record.WaypointSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Max(record => record.LastObservedAt), StringComparer.OrdinalIgnoreCase);
        var toPredict = candidates
            .Where(market => market.TradeGoods.Count > 0 && lastLooks.ContainsKey(market.WaypointSymbol))
            .Select(market => (market, lastLooks[market.WaypointSymbol]))
            .ToList();
        if (toPredict.Count == 0)
        {
            return (candidates, new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
        }

        var predictedMarkets = (await predictions.PredictAsync(toPredict, now, cancellationToken))
            .ToDictionary(market => market.WaypointSymbol, StringComparer.OrdinalIgnoreCase);
        var weights = await confidence.WeightsAsync(
            predictedMarkets.Values.ToDictionary(market => market.WaypointSymbol, market => market.SystemSymbol, StringComparer.OrdinalIgnoreCase),
            network,
            now,
            cancellationToken);
        return ([.. candidates.Select(market => predictedMarkets.GetValueOrDefault(market.WaypointSymbol, market))], weights);
    }

    /// <summary>
    /// Whether the jump gate can be built (PLAN.md slice 6.49, D136): no plan needs anything that comes before the gate's loads in
    /// the order ships are bought in (the contract's drone, the surveyors, the cargo ships of <c>Trade.ShipPurchases</c>, home's
    /// probes). Asked on 2026-11-08, after the first hours of the run of 2026-11-08 went to gate trades worth −400 to +3,000
    /// credits while the credits stood still: "Gate-first once the gate can be built". Until then the routes that feed the gate
    /// rank and pay as any other (<see cref="TradeMarketMap.ConstructionFirst"/>).
    /// </summary>
    private bool GateCanBeBuilt(DateTimeOffset now) => !purchaseNeeds.OpenBefore(PurchaseTier.Construction, now);

    /// <summary>
    /// Whether the routes that feed the jump gate's chain go first (<see cref="TradeMarketMap.ConstructionFirst"/>): once the gate
    /// can be built (D136), and only while its forecast says supply is what finishes it last (PLAN.md slice 6.56, D144). Asked on
    /// 2026-11-09, "prioritize feeder trips only when supply binds", after two of the four cargo ships spent two hours on feeder
    /// trips of −700 to +700 credits each while money set the finish (money 14.65 hours, supply 10.23). Before the first forecast
    /// they go first, as before.
    /// </summary>
    private async Task<bool> FeedersFirstAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!GateCanBeBuilt(now))
        {
            return false;
        }

        var binding = (await plans.GetAsync<ConstructionPlanState>(PlanTypes.Construction, cancellationToken))?.Forecast.Binding ?? string.Empty;
        return binding.Length == 0 || binding == GateForecast.Supply;
    }

    /// <summary>
    /// D140: whether the gate's next load waits only for one of our ships, as the construction plan last said it, once the gate
    /// can be built.
    /// </summary>
    private bool GateLoadWaitsForShip(DateTimeOffset now)
        => GateCanBeBuilt(now)
            && purchaseNeeds.Reported().TryGetValue(AutomationPlan.Construction, out var reported)
            && reported.Need.Tier == PurchaseTier.Construction
            && reported.Need.WaitsForShip
            && purchaseNeeds.Counts(reported.At, now);

    /// <summary>Whether a market's prices are recent enough to decide by (D96): seen with prices within the age.</summary>
    private static bool IsFresh(MarketFreshnessRecord market, DateTimeOffset now, TimeSpan maxAge)
        => market.HasPrices && now - market.LastObservedAt <= maxAge;

    /// <summary>How old a market's prices may be (D96): <c>Trade.MaxPriceAgeMinutes</c>, else as it is seeded.</summary>
    private async Task<TimeSpan> MaxPriceAgeAsync(CancellationToken cancellationToken)
        => TimeSpan.FromMinutes(await settings.GetAsync<int>(MaxPriceAgeMinutesSetting, cancellationToken) is var minutes and > 0 ? minutes : DefaultMaxPriceAgeMinutes);

    /// <summary>What a unit of ANTIMATTER costs at each market that sells it, as last seen: a jump buys one at the gate's (D63).</summary>
    private static Dictionary<string, long> AntimatterPrices(IEnumerable<MarketSnapshot> allMarkets)
        => allMarkets
            .SelectMany(market => market.TradeGoods
                .Where(good => good.Symbol.Equals(AntimatterSymbol, StringComparison.OrdinalIgnoreCase) && good.PurchasePrice > 0)
                .Select(good => (market.WaypointSymbol, Price: (long)good.PurchasePrice)))
            .GroupBy(entry => entry.WaypointSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Price, StringComparer.OrdinalIgnoreCase);

    /// <summary>The production chains, the credits, the headquarters' system, the minimum profit and the fuel reserve.</summary>
    private async Task<Basics> ReadBasicsAsync(CancellationToken cancellationToken)
    {
        var chains = await supplyChain.GetAsync(port, TimeProvider.System.GetUtcNow(), cancellationToken);
        var agent = await agents.GetAsync(cancellationToken);
        var minProfitPerUnit = await settings.GetAsync<int>(MinProfitPerUnitSetting, cancellationToken);
        var fuelReserve = await settings.GetAsync<long>(FuelReserveCreditsSetting, cancellationToken);
        return new Basics(
            chains,
            BusinessSystems.Of(agent).FirstOrDefault() ?? string.Empty,
            agent?.Credits ?? 0,
            Math.Max(0, minProfitPerUnit),
            Math.Max(0, fuelReserve));
    }

    /// <summary>
    /// D89: while the system's jump gate needs materials and the construction plan buys them, the routes that feed the markets
    /// making them come first. Empty with the construction plan off.
    /// </summary>
    private async Task<IReadOnlySet<string>> MaterialsAsync(string systemSymbol, CancellationToken cancellationToken)
    {
        var materials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (await settings.IsPlanEnabledAsync(AutomationPlan.Construction, cancellationToken))
        {
            materials.UnionWith((await constructionSites.CachedNeedingMaterialsAsync(cancellationToken))
                .Where(site => WaypointSymbols.SystemOf(site.WaypointSymbol).Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))
                .SelectMany(site => site.Materials)
                .Where(material => material.Fulfilled < material.Required)
                .Select(material => material.TradeSymbol));
        }

        return materials;
    }

    /// <summary>What every trade decision reads besides the map.</summary>
    private sealed record Basics(
        IReadOnlyDictionary<string, IReadOnlyList<string>> Chains,
        string HomeSystemSymbol,
        long Credits,
        int MinProfitPerUnit,
        long FuelReserve);
}
