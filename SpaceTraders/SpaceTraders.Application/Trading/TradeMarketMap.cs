using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// What a trade decision reads about one system (PLAN.md slice 6.5), or, for the trading plan, about the systems within its
/// reach (slice 6.29, D96): where each waypoint is, what each market buys and sells at the prices last seen there, the game's
/// production chains, and the ways between the systems through their jump gates (<see cref="Gates"/>). Distances are measured
/// within a system only: a waypoint of another system is reached by its gates.
/// </summary>
/// <remarks>Immutable once built, so it can be shared between the decisions of one pass.</remarks>
public sealed class TradeMarketMap
{
    private const string FuelSymbol = "FUEL";
    private const string ImportType = "IMPORT";
    private const string ExportType = "EXPORT";
    private const string ExchangeType = "EXCHANGE";
    private const string AbundantSupply = "ABUNDANT";

    private static readonly IReadOnlySet<string> NoMarkets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (int X, int Y)> _positions;
    private readonly Dictionary<string, string> _systems;
    private readonly Dictionary<string, Dictionary<string, TradeGoodSnapshot>> _goods;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _madeFrom;
    private readonly HashSet<string> _madeInto;
    private readonly long _averageFuelPrice;
    private readonly IReadOnlySet<string> _constructionMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _goingIntoConstruction = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the map of one system, or of several (slice 6.29).</summary>
    /// <param name="waypoints">The systems' waypoints, with their coordinates.</param>
    /// <param name="markets">The systems' markets, with the goods and prices last seen there.</param>
    /// <param name="madeFrom">The production chains: for each good, the goods it is made from.</param>
    public TradeMarketMap(
        IEnumerable<WaypointCacheModel> waypoints,
        IEnumerable<MarketSnapshot> markets,
        IReadOnlyDictionary<string, IReadOnlyList<string>> madeFrom)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentNullException.ThrowIfNull(markets);
        ArgumentNullException.ThrowIfNull(madeFrom);

        Waypoints = [.. waypoints.GroupBy(waypoint => waypoint.Symbol, StringComparer.OrdinalIgnoreCase).Select(group => group.First())];
        _positions = Waypoints.ToDictionary(waypoint => waypoint.Symbol, waypoint => (waypoint.X, waypoint.Y), StringComparer.OrdinalIgnoreCase);
        _systems = Waypoints
            .Where(waypoint => !string.IsNullOrWhiteSpace(waypoint.SystemSymbol))
            .ToDictionary(waypoint => waypoint.Symbol, waypoint => waypoint.SystemSymbol, StringComparer.OrdinalIgnoreCase);
        _goods = markets
            .GroupBy(market => market.WaypointSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().TradeGoods
                    .GroupBy(good => good.Symbol, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(goods => goods.Key, goods => goods.First(), StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        _madeFrom = madeFrom;
        _madeInto = madeFrom.Values.SelectMany(inputs => inputs).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fuelPrices = _goods.Values
            .Where(goods => goods.TryGetValue(FuelSymbol, out var fuel) && fuel.PurchasePrice > 0)
            .Select(goods => (long)goods[FuelSymbol].PurchasePrice)
            .ToList();
        _averageFuelPrice = fuelPrices.Count == 0 ? 0 : (long)Math.Ceiling(fuelPrices.Average());
    }

    /// <summary>A copy that shares everything with <paramref name="other"/> (<see cref="WithoutJumps"/>).</summary>
    private TradeMarketMap(TradeMarketMap other)
    {
        Waypoints = other.Waypoints;
        _positions = other._positions;
        _systems = other._systems;
        _goods = other._goods;
        _madeFrom = other._madeFrom;
        _madeInto = other._madeInto;
        _averageFuelPrice = other._averageFuelPrice;
        _constructionMaterials = other._constructionMaterials;
        _goingIntoConstruction = other._goingIntoConstruction;
        ConstructionSystemSymbol = other.ConstructionSystemSymbol;
        StaleMarkets = other.StaleMarkets;
    }

    /// <summary>The systems' waypoints, with their types and traits (what an asteroid yields, slice 6.4).</summary>
    public IReadOnlyList<WaypointCacheModel> Waypoints { get; }

    /// <summary>
    /// The flights planned on this map, by start, end, fuel aboard and tank (slice 6.29): a pass weighs the same haul for every
    /// good two markets trade, and every trader, so each is planned once (<see cref="TradeRoutePlanner.TryPlanFlight(TradeMarketMap, string, string, int, int, out TradeFlight)"/>).
    /// </summary>
    internal System.Collections.Concurrent.ConcurrentDictionary<string, (bool Found, TradeFlight Flight)> Flights { get; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The waypoints that have a market with known prices.</summary>
    public IReadOnlyCollection<string> MarketWaypoints => _goods.Keys;

    /// <summary>
    /// The ways between the map's systems through their jump gates (slice 6.29, D96): <see cref="TradeGates.None"/> unless set,
    /// and then a waypoint of another system is out of reach.
    /// </summary>
    public TradeGates Gates { get; init; } = TradeGates.None;

    /// <summary>
    /// The markets whose prices are too old to choose a trade route by (D96: <c>Trade.MaxPriceAgeMinutes</c>, at home too): their
    /// prices still say where fuel is sold, but no route buys or sells there. None unless set.
    /// </summary>
    public IReadOnlySet<string> StaleMarkets { get; init; } = NoMarkets;

    /// <summary>
    /// The materials the system's jump gate still needs, while the construction plan buys them (PLAN.md slice 6.22, D89): the
    /// routes that feed the markets making them come first. None unless set.
    /// </summary>
    public IReadOnlySet<string> ConstructionMaterials
    {
        get => _constructionMaterials;
        init
        {
            _constructionMaterials = value;
            _goingIntoConstruction = GoodsGoingInto(value, _madeFrom);
        }
    }

    /// <summary>
    /// The system whose jump gate needs <see cref="ConstructionMaterials"/> (D68: the headquarters'): only its markets feed the
    /// gate (D89). Empty for any market, as a map of that one system has it.
    /// </summary>
    public string ConstructionSystemSymbol { get; init; } = string.Empty;

    /// <summary>
    /// The same map without the ways between its systems (slice 6.29, D96): what a ship whose work is in its own system trades
    /// by, a drone between its trips, say; and where a trip moves its sale to.
    /// </summary>
    /// <returns>The map, every waypoint of another system out of reach.</returns>
    public TradeMarketMap WithoutJumps()
        => ReferenceEquals(Gates, TradeGates.None) ? this : new TradeMarketMap(this);

    /// <summary>
    /// The same map with the markets whose prices are too old to decide by (<see cref="StaleMarkets"/>): what mining abroad reads
    /// (PLAN.md slice 6.40, D122), where only the markets the probes keep fresh count.
    /// </summary>
    /// <param name="staleMarkets">The markets whose prices are too old.</param>
    /// <returns>The map, those markets stale.</returns>
    public TradeMarketMap WithStaleMarkets(IReadOnlySet<string> staleMarkets)
    {
        ArgumentNullException.ThrowIfNull(staleMarkets);
        return new TradeMarketMap(this) { Gates = Gates, StaleMarkets = staleMarkets };
    }

    /// <summary>The system a waypoint is in: as its cached waypoint says, else as its symbol does.</summary>
    /// <param name="waypointSymbol">The waypoint.</param>
    /// <returns>The system.</returns>
    public string SystemOf(string waypointSymbol)
    {
        ArgumentNullException.ThrowIfNull(waypointSymbol);
        return _systems.TryGetValue(waypointSymbol, out var system) ? system : WaypointSymbols.SystemOf(waypointSymbol);
    }

    /// <summary>Whether two waypoints are in the same system.</summary>
    /// <param name="a">One waypoint.</param>
    /// <param name="b">The other.</param>
    /// <returns>True for the same system.</returns>
    public bool SameSystem(string a, string b) => SystemOf(a).Equals(SystemOf(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a market's prices are recent enough to choose a trade route by (<see cref="StaleMarkets"/>, D96).</summary>
    /// <param name="waypointSymbol">The market's waypoint.</param>
    /// <returns>True unless its prices are too old.</returns>
    public bool IsFresh(string waypointSymbol) => !StaleMarkets.Contains(waypointSymbol);

    /// <summary>The goods a market lists, with their last known prices; empty for an unknown market.</summary>
    /// <param name="waypointSymbol">The market's waypoint.</param>
    /// <returns>The market's goods.</returns>
    public IEnumerable<TradeGoodSnapshot> GoodsAt(string waypointSymbol)
        => _goods.TryGetValue(waypointSymbol, out var goods) ? goods.Values : [];

    /// <summary>Looks up one good at one market.</summary>
    /// <param name="waypointSymbol">The market's waypoint.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <param name="good">The good as last seen there.</param>
    /// <returns>Whether the market lists the good.</returns>
    public bool TryGetGood(string waypointSymbol, string tradeSymbol, out TradeGoodSnapshot good)
    {
        if (_goods.TryGetValue(waypointSymbol, out var goods) && goods.TryGetValue(tradeSymbol, out var found))
        {
            good = found;
            return true;
        }

        good = new TradeGoodSnapshot(tradeSymbol, string.Empty, 0, 0, 0, string.Empty);
        return false;
    }

    /// <summary>The straight-line distance between two waypoints of one system, as the game measures it.</summary>
    /// <param name="from">One waypoint.</param>
    /// <param name="to">The other waypoint.</param>
    /// <param name="distance">The distance.</param>
    /// <returns>Whether both waypoints are on the map, in the same system: between systems a ship jumps (slice 6.29).</returns>
    public bool TryGetDistance(string from, string to, out double distance)
    {
        if (_positions.TryGetValue(from, out var a) && _positions.TryGetValue(to, out var b) && SameSystem(from, to))
        {
            var dx = (double)b.X - a.X;
            var dy = (double)b.Y - a.Y;
            distance = Math.Sqrt((dx * dx) + (dy * dy));
            return true;
        }

        distance = 0;
        return false;
    }

    /// <summary>Whether a ship can refuel at the waypoint: its market sells <c>FUEL</c>.</summary>
    /// <param name="waypointSymbol">The waypoint.</param>
    /// <returns>True when the market there lists FUEL with a price.</returns>
    public bool SellsFuel(string waypointSymbol)
        => TryGetGood(waypointSymbol, FuelSymbol, out var fuel) && fuel.PurchasePrice > 0;

    /// <summary>
    /// What one unit of <c>FUEL</c> costs at the waypoint's market, which fills 100 units of a ship's
    /// tank; where the market doesn't sell it, the average over the system's markets, else 0.
    /// </summary>
    /// <param name="waypointSymbol">The waypoint.</param>
    /// <returns>The price of one market unit of FUEL.</returns>
    public long FuelPrice(string waypointSymbol)
        => TryGetGood(waypointSymbol, FuelSymbol, out var fuel) && fuel.PurchasePrice > 0 ? fuel.PurchasePrice : _averageFuelPrice;

    /// <summary>
    /// Whether nothing is made from the good (D82): no good in the production chains is made from it. Ships are made goods
    /// there too, so SHIP_PARTS and SHIP_PLATING, which every ship is made from, are no end products. In X1-FJ91 on
    /// 2026-10-05, 13 of the 50 goods traded were, from ANTIMATTER to SUPERGRAINS. Without the chains every good is one.
    /// </summary>
    /// <param name="tradeSymbol">The good.</param>
    /// <returns>True when nothing is made from the good.</returns>
    public bool IsEndProduct(string tradeSymbol) => !_madeInto.Contains(tradeSymbol);

    /// <summary>
    /// The goods a good is made from, by the production chains: IRON from IRON_ORE, FAB_MATS from IRON and QUARTZ_SAND. None
    /// for a good nothing makes, or without the chains.
    /// </summary>
    /// <param name="tradeSymbol">The good.</param>
    /// <returns>What it is made from.</returns>
    public IReadOnlyList<string> InputsOf(string tradeSymbol)
        => _madeFrom.TryGetValue(tradeSymbol, out var inputs) ? inputs : [];

    /// <summary>
    /// Whether a good goes into a material the jump gate still needs (PLAN.md slice 6.25, D92): it is one of
    /// <see cref="ConstructionMaterials"/>, or one of them is made from it, directly or through the goods made from it (the
    /// production chains). IRON goes into FAB_MATS; COPPER into ADVANCED_CIRCUITRY, through ELECTRONICS and MICROPROCESSORS.
    /// None while the gate needs nothing.
    /// </summary>
    /// <param name="tradeSymbol">The good.</param>
    /// <returns>True when more of the good makes more of what the gate needs.</returns>
    public bool GoesIntoConstruction(string tradeSymbol) => _goingIntoConstruction.Contains(tradeSymbol);

    /// <summary>
    /// The pricier good a market makes from <paramref name="tradeSymbol"/>: the market imports the good,
    /// exports something made from it (the production chains), and charges more for that export than
    /// for the good. Delivering the good there grows that production.
    /// </summary>
    /// <param name="waypointSymbol">The market's waypoint.</param>
    /// <param name="tradeSymbol">The good delivered.</param>
    /// <returns>The most expensive such export, or empty when the market makes nothing pricier from the good.</returns>
    public string PricierGoodMadeFrom(string waypointSymbol, string tradeSymbol)
    {
        if (!TryGetGood(waypointSymbol, tradeSymbol, out var input)
            || !input.Type.Equals(ImportType, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return GoodsAt(waypointSymbol)
            .Where(export => export.Type.Equals(ExportType, StringComparison.OrdinalIgnoreCase)
                && export.PurchasePrice > input.PurchasePrice
                && _madeFrom.TryGetValue(export.Symbol, out var inputs)
                && inputs.Contains(tradeSymbol, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(export => export.PurchasePrice)
            .ThenBy(export => export.Symbol, StringComparer.Ordinal)
            .Select(export => export.Symbol)
            .FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// The jump gate's material a market makes from a good delivered there (D89): the market imports the good, below ABUNDANT,
    /// and exports one of <see cref="ConstructionMaterials"/> made from it (the production chains), so more of the good grows
    /// that production. On 2026-10-05 the two FAB_MATS markets, D52 and F58, made 1 to 4 units a tick while their IRON was
    /// SCARCE, and the gate needed 1,260 more.
    /// </summary>
    /// <param name="waypointSymbol">The market's waypoint.</param>
    /// <param name="tradeSymbol">The good delivered.</param>
    /// <returns>The material, or empty when delivering the good there feeds none.</returns>
    public string ConstructionMaterialMadeFrom(string waypointSymbol, string tradeSymbol)
    {
        if (ConstructionMaterials.Count == 0
            || (ConstructionSystemSymbol.Length > 0 && !SystemOf(waypointSymbol).Equals(ConstructionSystemSymbol, StringComparison.OrdinalIgnoreCase))
            || !TryGetGood(waypointSymbol, tradeSymbol, out var input)
            || !input.Type.Equals(ImportType, StringComparison.OrdinalIgnoreCase)
            || input.Supply.Equals(AbundantSupply, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return GoodsAt(waypointSymbol)
            .Where(export => export.Type.Equals(ExportType, StringComparison.OrdinalIgnoreCase)
                && ConstructionMaterials.Contains(export.Symbol)
                && _madeFrom.TryGetValue(export.Symbol, out var inputs)
                && inputs.Contains(tradeSymbol, StringComparer.OrdinalIgnoreCase))
            .Select(export => export.Symbol)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// Whether a market makes something from a good delivered there (D91): it imports the good and exports something made
    /// from it (the production chains); without the chains, any import counts, as before. A market that exchanges the good,
    /// or imports it without making anything from it, only pays for it: a sale there is a wealth trade, never a supply trade.
    /// In X1-FJ91 on 2026-10-05, H60 made IRON from IRON_ORE, D52 imported IRON_ORE and made nothing from it, and B7 and H62
    /// exchanged ores.
    /// </summary>
    /// <param name="waypointSymbol">The market's waypoint.</param>
    /// <param name="tradeSymbol">The good delivered.</param>
    /// <returns>True when delivering the good there feeds the market's production.</returns>
    public bool MakesSomethingFrom(string waypointSymbol, string tradeSymbol)
    {
        if (!TryGetGood(waypointSymbol, tradeSymbol, out var input)
            || !input.Type.Equals(ImportType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _madeFrom.Count == 0
            || GoodsAt(waypointSymbol).Any(export => export.Type.Equals(ExportType, StringComparison.OrdinalIgnoreCase)
                && _madeFrom.TryGetValue(export.Symbol, out var inputs)
                && inputs.Contains(tradeSymbol, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether a market exchanges a good (D91): it buys and sells it, and makes nothing from it. A sale there is a wealth
    /// trade, considered last, never a supply trade. Asked on 2026-10-05: "EXCHANGE nodes should be lowest priority and only
    /// considered as wealth trades, never as supply trades."
    /// </summary>
    /// <param name="waypointSymbol">The market's waypoint.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <returns>True when the market lists the good as EXCHANGE.</returns>
    public bool Exchanges(string waypointSymbol, string tradeSymbol)
        => TryGetGood(waypointSymbol, tradeSymbol, out var good) && good.Type.Equals(ExchangeType, StringComparison.OrdinalIgnoreCase);

    /// <summary>The materials, and every good they are made from, directly or further down the production chains.</summary>
    private static HashSet<string> GoodsGoingInto(IEnumerable<string> materials, IReadOnlyDictionary<string, IReadOnlyList<string>> madeFrom)
    {
        var goods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var next = new Queue<string>(materials);
        while (next.TryDequeue(out var good))
        {
            if (goods.Add(good) && madeFrom.TryGetValue(good, out var inputs))
            {
                foreach (var input in inputs)
                {
                    next.Enqueue(input);
                }
            }
        }

        return goods;
    }
}
