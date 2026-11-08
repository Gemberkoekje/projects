using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Construction;

/// <summary>
/// The jump gate's production chain, with the supply each good has in the gate's system (PLAN.md slice 6.51, D139). Asked
/// on 2026-11-08, about what stops the gate: "Not just the gate mats themselves, also the materials needed to build the gate
/// mats. And the materials that they need, down to the ores." Each material the gate still needs is step 1; what it is made
/// from step 2 (IRON and QUARTZ_SAND for FAB_MATS), and so on down the production chains to the ores, the shortest way down.
/// </summary>
public static class ConstructionChain
{
    private const string ExportType = "EXPORT";
    private const string ImportType = "IMPORT";
    private const string ExchangeType = "EXCHANGE";

    private static readonly string[] SupplyLevels = ["SCARCE", "LIMITED", "MODERATE", "HIGH", "ABUNDANT"];

    /// <summary>
    /// Every good of the gate's chain, the materials first, each with the best supply among the markets of the gate's system
    /// that sell it (export or exchange it: where a load or a trade buys it) and the best among those that make a good of the
    /// chain from it (import it and export what it goes into: how well the next step is fed), as the map holds them. An ore
    /// is mostly mined, not sold, so it mostly has only the second.
    /// </summary>
    /// <param name="map">The gate's system, with the production chains.</param>
    /// <param name="site">The gate, as last seen: only the materials it still needs count.</param>
    /// <param name="systemSymbol">The gate's system.</param>
    /// <returns>The chain's goods, by step, then by name.</returns>
    public static IReadOnlyList<ConstructionChainGoodState> Of(TradeMarketMap map, ConstructionSiteModel site, string systemSymbol)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(systemSymbol);

        var steps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var next = new Queue<(string Good, int Step)>(site.Materials
            .Where(material => material.Fulfilled < material.Required)
            .Select(material => (material.TradeSymbol, 1)));
        while (next.TryDequeue(out var entry))
        {
            if (steps.TryAdd(entry.Good, entry.Step))
            {
                foreach (var input in map.InputsOf(entry.Good))
                {
                    next.Enqueue((input, entry.Step + 1));
                }
            }
        }

        var markets = map.MarketWaypoints
            .Where(waypoint => map.SystemOf(waypoint).Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return [.. steps
            .OrderBy(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ConstructionChainGoodState
            {
                TradeSymbol = entry.Key,
                Step = entry.Value,
                ExportSupply = Best(markets
                    .Select(market => map.TryGetGood(market, entry.Key, out var good) && (IsType(good, ExportType) || IsType(good, ExchangeType)) ? good.Supply : string.Empty)),
                ImportSupply = Best(markets
                    .Select(market => map.TryGetGood(market, entry.Key, out var good) && IsType(good, ImportType) && MakesChainGood(map, market, entry.Key, steps) ? good.Supply : string.Empty)),
            })];
    }

    /// <summary>Whether a market exports a good of the chain made from <paramref name="tradeSymbol"/> (the production chains).</summary>
    private static bool MakesChainGood(TradeMarketMap map, string market, string tradeSymbol, Dictionary<string, int> steps)
        => map.GoodsAt(market).Any(export => IsType(export, ExportType)
            && steps.ContainsKey(export.Symbol)
            && map.InputsOf(export.Symbol).Contains(tradeSymbol, StringComparer.OrdinalIgnoreCase));

    private static bool IsType(TradeGoodSnapshot good, string type) => good.Type.Equals(type, StringComparison.OrdinalIgnoreCase);

    /// <summary>The highest of the supply levels, SCARCE lowest and ABUNDANT highest; empty for none.</summary>
    private static string Best(IEnumerable<string> supplies)
        => supplies
            .Select(supply => Array.FindIndex(SupplyLevels, level => level.Equals(supply, StringComparison.OrdinalIgnoreCase)))
            .Where(rank => rank >= 0)
            .DefaultIfEmpty(-1)
            .Max() is var best and >= 0
                ? SupplyLevels[best]
                : string.Empty;
}
