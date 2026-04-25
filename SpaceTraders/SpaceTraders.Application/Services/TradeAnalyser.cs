using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Application.Services;

public sealed class TradeAnalyser : ITradeAnalyser
{
    public IReadOnlyList<TradeOpportunityDto> ComputeOpportunities(
        IReadOnlyList<MarketSnapshot> markets,
        int minProfitPerUnit = 0)
    {
        var buyOptions = new Dictionary<string, List<(string Waypoint, string System, int Price)>>(StringComparer.OrdinalIgnoreCase);
        var sellOptions = new Dictionary<string, List<(string Waypoint, string System, int Price)>>(StringComparer.OrdinalIgnoreCase);
        var supplyChainConsumers = BuildSupplyChainConsumers(markets);

        foreach (var market in markets)
        {
            foreach (var good in market.TradeGoods)
            {
                if (good.PurchasePrice > 0 &&
                    (good.Type.Equals("EXPORT", StringComparison.OrdinalIgnoreCase) ||
                     good.Type.Equals("EXCHANGE", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!buyOptions.TryGetValue(good.Symbol, out var buys))
                    {
                        buys = [];
                        buyOptions[good.Symbol] = buys;
                    }
                    buys.Add((market.WaypointSymbol, market.SystemSymbol, good.PurchasePrice));
                }

                if (good.SellPrice > 0)
                {
                    if (!sellOptions.TryGetValue(good.Symbol, out var sells))
                    {
                        sells = [];
                        sellOptions[good.Symbol] = sells;
                    }
                    sells.Add((market.WaypointSymbol, market.SystemSymbol, good.SellPrice));
                }
            }
        }

        var opportunities = new List<TradeOpportunityDto>();

        foreach (var (symbol, buys) in buyOptions)
        {
            if (!sellOptions.TryGetValue(symbol, out var sells)) continue;

            supplyChainConsumers.TryGetValue(symbol, out var consumers);
            var supplyChainDepth = consumers?.Count ?? 0;
            var supportsSupplyChain = supplyChainDepth > 0;

            foreach (var buy in buys)
            {
                foreach (var sell in sells)
                {
                    if (buy.Waypoint.Equals(sell.Waypoint, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var profitPerUnit = sell.Price - buy.Price;
                    if (profitPerUnit < minProfitPerUnit)
                    {
                        continue;
                    }

                    var distanceJumps = buy.System.Equals(sell.System, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    var profitPerJump = distanceJumps == 0
                        ? (decimal)profitPerUnit
                        : (decimal)profitPerUnit / (distanceJumps + 1);

                    opportunities.Add(new TradeOpportunityDto(
                        Id: 0,
                        TradeSymbol: symbol,
                        BuyWaypoint: buy.Waypoint,
                        SellWaypoint: sell.Waypoint,
                        BuyPrice: buy.Price,
                        SellPrice: sell.Price,
                        ProfitPerUnit: profitPerUnit,
                        DistanceJumps: distanceJumps,
                        ProfitPerJump: profitPerJump,
                        SupportsSupplyChain: supportsSupplyChain,
                        SupplyChainDepth: supplyChainDepth,
                        ComputedAt: DateTimeOffset.UtcNow));
                }
            }
        }

        return opportunities
            .OrderByDescending(o => o.SupportsSupplyChain)
            .ThenByDescending(o => o.SupplyChainDepth)
            .ThenByDescending(o => o.ProfitPerJump)
            .ToList();
    }

    public TradeOpportunityDto? SelectBestRoute(
        IReadOnlyList<TradeOpportunityDto> routes,
        int cargoCapacity,
        int minProfitPerUnit,
        int maxDistanceJumps,
        bool prioritizeSupplyChain = true)
    {
        var candidates = routes
            .Where(r => r.ProfitPerUnit >= minProfitPerUnit && r.DistanceJumps <= maxDistanceJumps);

        if (prioritizeSupplyChain)
        {
            var supplyChainRoute = candidates
                .Where(r => r.SupportsSupplyChain)
                .OrderByDescending(r => r.SupplyChainDepth)
                .ThenByDescending(r => r.ProfitPerJump)
                .FirstOrDefault();

            if (supplyChainRoute is not null)
            {
                return supplyChainRoute;
            }
        }

        return candidates
            .OrderByDescending(r => r.ProfitPerJump)
            .FirstOrDefault();
    }

    private static Dictionary<string, HashSet<string>> BuildSupplyChainConsumers(IReadOnlyList<MarketSnapshot> markets)
    {
        var consumers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var market in markets)
        {
            var imports = market.Imports
                .Concat(market.TradeGoods
                    .Where(g => g.Type.Equals("IMPORT", StringComparison.OrdinalIgnoreCase))
                    .Select(g => g.Symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var outputs = market.Exports
                .Concat(market.Exchange)
                .Concat(market.TradeGoods
                    .Where(g => g.Type.Equals("EXPORT", StringComparison.OrdinalIgnoreCase) ||
                                g.Type.Equals("EXCHANGE", StringComparison.OrdinalIgnoreCase))
                    .Select(g => g.Symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var import in imports)
            {
                if (!consumers.TryGetValue(import, out var destinations))
                {
                    destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    consumers[import] = destinations;
                }

                foreach (var output in outputs)
                {
                    destinations.Add(output);
                }
            }
        }

        return consumers;
    }
}
