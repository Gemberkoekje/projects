using System.Text;
using System.Text.Json;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.API.Services;

/// <summary>
/// The ship types the cached shipyards list and the goods the cached markets trade (their imports, exports, exchange and
/// priced goods), each with the waypoints that list it. A snapshot is taken when the cache lists one that no snapshot of
/// the run held yet (slice 2.15, D73).
/// </summary>
public sealed class KnownTypes
{
    /// <summary>A ship type, which a shipyard lists.</summary>
    public const string ShipTypeKind = "ShipType";

    /// <summary>A good, which a market trades.</summary>
    public const string GoodKind = "Good";

    private readonly IReadOnlyDictionary<string, SortedSet<string>> _shipTypes;
    private readonly IReadOnlyDictionary<string, SortedSet<string>> _goods;

    private KnownTypes(IReadOnlyDictionary<string, SortedSet<string>> shipTypes, IReadOnlyDictionary<string, SortedSet<string>> goods)
    {
        _shipTypes = shipTypes;
        _goods = goods;
    }

    /// <summary>The ship types and goods the shipyards and markets list, as the cache keeps them.</summary>
    /// <param name="shipyards">The cached shipyards: their ship types, and the ships listed for sale.</param>
    /// <param name="markets">The cached markets: their imports, exports, exchange and priced goods.</param>
    /// <returns>Each ship type and good, with the waypoints that list it.</returns>
    public static KnownTypes Of(IEnumerable<CachedShipyard> shipyards, IEnumerable<CachedMarket> markets)
    {
        var shipTypes = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var shipyard in shipyards)
        {
            foreach (var shipType in Symbols(shipyard.ShipTypesJson, "type").Concat(Symbols(shipyard.ShipsDetailJson, "type")))
            {
                Add(shipTypes, shipType, shipyard.WaypointSymbol);
            }
        }

        var goods = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var market in markets)
        {
            var listed = Symbols(market.ImportsJson, "symbol")
                .Concat(Symbols(market.ExportsJson, "symbol"))
                .Concat(Symbols(market.ExchangeJson, "symbol"))
                .Concat(Symbols(market.TradeGoodsJson, "symbol"));
            foreach (var good in listed)
            {
                Add(goods, good, market.WaypointSymbol);
            }
        }

        return new KnownTypes(shipTypes, goods);
    }

    /// <summary>What this lists that <paramref name="known"/> doesn't: the ship types first, then the goods, each in symbol order.</summary>
    /// <param name="known">What the snapshots held.</param>
    /// <returns>Each ship type and good that is new, with the waypoints that list it.</returns>
    public IReadOnlyList<Discovery> NotIn(KnownTypes known)
        => [.. New(_shipTypes, known._shipTypes, ShipTypeKind), .. New(_goods, known._goods, GoodKind)];

    /// <summary>What either lists.</summary>
    /// <param name="other">The other ship types and goods.</param>
    /// <returns>Both together.</returns>
    public KnownTypes With(KnownTypes other) => new(Merge(_shipTypes, other._shipTypes), Merge(_goods, other._goods));

    /// <summary>
    /// The discoveries in a line, such as <c>Ship types: SHIP_LIGHT_HAULER (X1-FJ91-A2). Goods: FAB_MATS (X1-FJ91-H59).</c>
    /// </summary>
    /// <param name="discoveries">What was new.</param>
    /// <returns>The ship types, then the goods, each with the waypoints that list it.</returns>
    public static string Describe(IReadOnlyList<Discovery> discoveries)
    {
        var text = new StringBuilder();
        Append(text, "Ship types", discoveries.Where(discovery => discovery.Kind == ShipTypeKind));
        Append(text, "Goods", discoveries.Where(discovery => discovery.Kind == GoodKind));
        return text.ToString();
    }

    private static void Append(StringBuilder text, string heading, IEnumerable<Discovery> discoveries)
    {
        var listed = discoveries.Select(discovery => $"{discovery.Symbol} ({string.Join(", ", discovery.Waypoints)})").ToList();
        if (listed.Count == 0)
        {
            return;
        }

        if (text.Length > 0)
        {
            text.Append(' ');
        }

        text.Append(heading).Append(": ").AppendJoin(", ", listed).Append('.');
    }

    private static IEnumerable<Discovery> New(
        IReadOnlyDictionary<string, SortedSet<string>> listed,
        IReadOnlyDictionary<string, SortedSet<string>> known,
        string kind)
        => listed
            .Where(entry => !known.ContainsKey(entry.Key))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new Discovery(kind, entry.Key, [.. entry.Value]));

    private static Dictionary<string, SortedSet<string>> Merge(
        IReadOnlyDictionary<string, SortedSet<string>> first,
        IReadOnlyDictionary<string, SortedSet<string>> second)
    {
        var merged = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var (symbol, waypoints) in first.Concat(second))
        {
            foreach (var waypoint in waypoints)
            {
                Add(merged, symbol, waypoint);
            }
        }

        return merged;
    }

    private static void Add(Dictionary<string, SortedSet<string>> listed, string symbol, string waypoint)
    {
        if (!listed.TryGetValue(symbol, out var waypoints))
        {
            waypoints = new SortedSet<string>(StringComparer.Ordinal);
            listed[symbol] = waypoints;
        }

        waypoints.Add(waypoint);
    }

    /// <summary>
    /// The symbols in a list the cache keeps as JSON text: each string, or each object's <paramref name="property"/>. Text
    /// that doesn't parse lists none, so one bad column can't stop the others.
    /// </summary>
    private static IEnumerable<string> Symbols(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var symbols = new List<string>();
            var root = document.RootElement;
            for (var index = 0; index < root.GetArrayLength(); index++)
            {
                var element = root[index];
                var symbol = element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Object when element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                        => value.GetString(),
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    symbols.Add(symbol);
                }
            }

            return symbols;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
