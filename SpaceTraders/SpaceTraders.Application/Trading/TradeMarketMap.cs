using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// What a trade decision reads about one system (PLAN.md slice 6.5): where each waypoint is, what
/// each market buys and sells at the prices last seen there, and the game's production chains.
/// </summary>
/// <remarks>Immutable once built, so it can be shared between the decisions of one pass.</remarks>
public sealed class TradeMarketMap
{
    private const string FuelSymbol = "FUEL";
    private const string ImportType = "IMPORT";
    private const string ExportType = "EXPORT";
    private const string AbundantSupply = "ABUNDANT";

    private readonly Dictionary<string, (int X, int Y)> _positions;
    private readonly Dictionary<string, Dictionary<string, TradeGoodSnapshot>> _goods;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _madeFrom;
    private readonly HashSet<string> _madeInto;
    private readonly long _averageFuelPrice;

    /// <summary>Builds the map of one system.</summary>
    /// <param name="waypoints">The system's waypoints, with their coordinates.</param>
    /// <param name="markets">The system's markets, with the goods and prices last seen there.</param>
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

    /// <summary>The system's waypoints, with their types and traits (what an asteroid yields, slice 6.4).</summary>
    public IReadOnlyList<WaypointCacheModel> Waypoints { get; }

    /// <summary>The waypoints that have a market with known prices.</summary>
    public IReadOnlyCollection<string> MarketWaypoints => _goods.Keys;

    /// <summary>
    /// The materials the system's jump gate still needs, while the construction plan buys them (PLAN.md slice 6.22, D89): the
    /// routes that feed the markets making them come first. None unless set.
    /// </summary>
    public IReadOnlySet<string> ConstructionMaterials { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>The straight-line distance between two waypoints, as the game measures it.</summary>
    /// <param name="from">One waypoint.</param>
    /// <param name="to">The other waypoint.</param>
    /// <param name="distance">The distance.</param>
    /// <returns>Whether both waypoints are on the map.</returns>
    public bool TryGetDistance(string from, string to, out double distance)
    {
        if (_positions.TryGetValue(from, out var a) && _positions.TryGetValue(to, out var b))
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
}
