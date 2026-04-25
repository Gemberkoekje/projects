using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Application.Services;

public sealed class TradeAnalyser : ITradeAnalyser
{
    public IReadOnlyList<TradeOpportunityDto> ComputeOpportunities(
        IReadOnlyList<MarketSnapshot> markets,
        int minProfitPerUnit = 0)
    {
        // Index: tradeSymbol → list of (waypoint, systemSymbol, purchasePrice)
        var buyOptions = new Dictionary<string, List<(string Waypoint, string System, int Price)>>(StringComparer.OrdinalIgnoreCase);
        var sellOptions = new Dictionary<string, List<(string Waypoint, string System, int Price)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var market in markets)
        {
            foreach (var good in market.TradeGoods)
            {
                // Only consider EXPORT and EXCHANGE types as buy sources
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

                // Any market listing a good with a positive sell price is a valid sell destination
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

            foreach (var buy in buys)
            {
                foreach (var sell in sells)
                {
                    // Cannot buy and sell at the same waypoint
                    if (buy.Waypoint.Equals(sell.Waypoint, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var profitPerUnit = sell.Price - buy.Price;
                    if (profitPerUnit < minProfitPerUnit) continue;

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
                        ComputedAt: DateTimeOffset.UtcNow));
                }
            }
        }

        return opportunities
            .OrderByDescending(o => o.ProfitPerJump)
            .ToList();
    }

    public TradeOpportunityDto? SelectBestRoute(
        IReadOnlyList<TradeOpportunityDto> routes,
        int cargoCapacity,
        int minProfitPerUnit,
        int maxDistanceJumps)
    {
        return routes
            .Where(r => r.ProfitPerUnit >= minProfitPerUnit && r.DistanceJumps <= maxDistanceJumps)
            .OrderByDescending(r => r.ProfitPerJump)
            .FirstOrDefault();
    }
}
