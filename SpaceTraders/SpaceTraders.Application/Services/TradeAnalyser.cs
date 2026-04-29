using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Application.Services;

public sealed class TradeAnalyser : ITradeAnalyser
{
    private static readonly ITradeSymbolNormalizer SymbolNormalizer = new TradeSymbolNormalizer();

    public IReadOnlyList<TradeOpportunityDto> ComputeOpportunities(
        IReadOnlyList<MarketSnapshot> markets,
        int minProfitPerUnit = 0)
    {
        var buyOptions = new Dictionary<string, List<TradeOption>>(StringComparer.OrdinalIgnoreCase);
        var sellOptions = new Dictionary<string, List<TradeOption>>(StringComparer.OrdinalIgnoreCase);
        var supplyChainConsumers = BuildSupplyChainConsumers(markets);

        foreach (var market in markets)
        {
            foreach (var good in market.TradeGoods)
            {
                var symbol = SymbolNormalizer.Normalize(good.Symbol);
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                if (good.PurchasePrice > 0 && IsBuyType(good.Type))
                {
                    if (!buyOptions.TryGetValue(symbol, out var buys))
                    {
                        buys = [];
                        buyOptions[symbol] = buys;
                    }

                    buys.Add(new TradeOption(
                        market.WaypointSymbol,
                        market.SystemSymbol,
                        good.PurchasePrice,
                        good.TradeVolume,
                        good.Supply,
                        good.Activity,
                        good.Type));
                }

                if (good.SellPrice > 0)
                {
                    if (!sellOptions.TryGetValue(symbol, out var sells))
                    {
                        sells = [];
                        sellOptions[symbol] = sells;
                    }

                    sells.Add(new TradeOption(
                        market.WaypointSymbol,
                        market.SystemSymbol,
                        good.SellPrice,
                        good.TradeVolume,
                        good.Supply,
                        good.Activity,
                        good.Type));
                }
            }
        }

        var opportunities = new List<TradeOpportunityDto>();

        foreach (var (symbol, buys) in buyOptions)
        {
            if (!sellOptions.TryGetValue(symbol, out var sells))
            {
                continue;
            }

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
                    var travelMultiplier = distanceJumps + 1;
                    var profitPerJump = (decimal)profitPerUnit / travelMultiplier;
                    var effectiveTradeVolume = Math.Max(1, Math.Min(buy.TradeVolume, sell.TradeVolume));

                    var estimatedFuelCost = distanceJumps == 0 ? 1m : 5m * travelMultiplier;
                    var estimatedTravelTimeMinutes = distanceJumps == 0 ? 2m : 6m * travelMultiplier;
                    var opportunityCostPenalty = effectiveTradeVolume > 0
                        ? Math.Max(0m, 100m - effectiveTradeVolume) / 25m
                        : 0m;
                    var cooldownPenalty = ResolveActivityPenalty(buy.Activity) + ResolveActivityPenalty(sell.Activity);
                    var rateLimitPenalty = distanceJumps > 0 ? 1m : 0.25m;

                    var categoryBonus = ResolveCategoryBonus(buy.Type, sell.Type);
                    var supplyBonus = ResolveSupplyBonus(buy.Supply, sell.Supply);
                    var supplyChainBonus = supportsSupplyChain ? Math.Min(10m, supplyChainDepth * 2m) : 0m;
                    var volumeBonus = Math.Min(10m, effectiveTradeVolume / 10m);

                    var routeScore =
                        (profitPerJump * 2m)
                        + categoryBonus
                        + supplyBonus
                        + supplyChainBonus
                        + volumeBonus
                        - estimatedFuelCost
                        - (estimatedTravelTimeMinutes / 10m)
                        - opportunityCostPenalty
                        - cooldownPenalty
                        - rateLimitPenalty;

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
                        ComputedAt: TimeProvider.System.GetUtcNow(),
                        BuyType: NormalizeType(buy.Type),
                        SellType: NormalizeType(sell.Type),
                        EffectiveTradeVolume: effectiveTradeVolume,
                        EstimatedFuelCost: estimatedFuelCost,
                        EstimatedTravelTimeMinutes: estimatedTravelTimeMinutes,
                        OpportunityCostPenalty: opportunityCostPenalty,
                        CooldownPenalty: cooldownPenalty,
                        RateLimitPenalty: rateLimitPenalty,
                        RouteScore: routeScore));
                }
            }
        }

        return opportunities
            .OrderByDescending(o => o.SupportsSupplyChain)
            .ThenByDescending(o => o.SupplyChainDepth)
            .ThenByDescending(o => o.RouteScore)
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
        var capacity = cargoCapacity > 0 ? cargoCapacity : int.MaxValue;
        var candidates = routes
            .Where(r => r.ProfitPerUnit >= minProfitPerUnit && r.DistanceJumps <= maxDistanceJumps)
            .Select(r => new
            {
                Route = r,
                Score = BuildCapacityAdjustedScore(r, capacity)
            });

        if (prioritizeSupplyChain)
        {
            var supplyChainRoute = candidates
                .Where(r => r.Route.SupportsSupplyChain)
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => r.Route.SupplyChainDepth)
                .Select(r => r.Route)
                .FirstOrDefault();

            if (supplyChainRoute is not null)
            {
                return supplyChainRoute;
            }
        }

        return candidates
            .OrderByDescending(r => r.Score)
            .Select(r => r.Route)
            .FirstOrDefault();
    }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> BuildProductionChain(IReadOnlyList<MarketSnapshot> markets)
    {
        var consumers = BuildSupplyChainConsumers(markets);
        return consumers.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyCollection<string>)pair.Value.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private decimal BuildCapacityAdjustedScore(TradeOpportunityDto route, int cargoCapacity)
    {
        var maxUnits = route.EffectiveTradeVolume > 0 ? Math.Min(route.EffectiveTradeVolume, cargoCapacity) : cargoCapacity;
        var grossProfit = route.ProfitPerUnit * maxUnits;
        var travelPenalty = route.EstimatedFuelCost + (route.EstimatedTravelTimeMinutes / 5m);
        var operationPenalty = route.OpportunityCostPenalty + route.CooldownPenalty + route.RateLimitPenalty;
        return route.RouteScore + grossProfit - travelPenalty - operationPenalty;
    }

    private Dictionary<string, HashSet<string>> BuildSupplyChainConsumers(IReadOnlyList<MarketSnapshot> markets)
    {
        var consumers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var market in markets)
        {
            var imports = market.Imports
                .Concat(market.TradeGoods
                    .Where(g => IsImportType(g.Type))
                    .Select(g => g.Symbol))
                .Select(SymbolNormalizer.Normalize)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var outputs = market.Exports
                .Concat(market.Exchange)
                .Concat(market.TradeGoods
                    .Where(g => IsBuyType(g.Type))
                    .Select(g => g.Symbol))
                .Select(SymbolNormalizer.Normalize)
                .Where(s => !string.IsNullOrWhiteSpace(s))
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

    private static bool IsBuyType(string type)
        => type.Equals("EXPORT", StringComparison.OrdinalIgnoreCase)
            || type.Equals("EXCHANGE", StringComparison.OrdinalIgnoreCase);

    private static bool IsImportType(string type)
        => type.Equals("IMPORT", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "UNKNOWN";
        }

        return type.Trim().ToUpperInvariant();
    }

    private static decimal ResolveCategoryBonus(string buyType, string sellType)
    {
        var bonus = 0m;
        if (buyType.Equals("EXPORT", StringComparison.OrdinalIgnoreCase))
        {
            bonus += 6m;
        }
        if (buyType.Equals("EXCHANGE", StringComparison.OrdinalIgnoreCase))
        {
            bonus += 3m;
        }
        if (sellType.Equals("IMPORT", StringComparison.OrdinalIgnoreCase))
        {
            bonus += 6m;
        }

        return bonus;
    }

    private static decimal ResolveSupplyBonus(string buySupply, string sellSupply)
    {
        return ResolveSupplyScore(buySupply) + (ResolveSupplyScore(sellSupply) / 2m);
    }

    private static decimal ResolveSupplyScore(string supply)
    {
        if (supply.Equals("ABUNDANT", StringComparison.OrdinalIgnoreCase)) return 6m;
        if (supply.Equals("HIGH", StringComparison.OrdinalIgnoreCase)) return 4m;
        if (supply.Equals("MODERATE", StringComparison.OrdinalIgnoreCase)) return 2m;
        if (supply.Equals("LIMITED", StringComparison.OrdinalIgnoreCase)) return -1m;
        if (supply.Equals("SCARCE", StringComparison.OrdinalIgnoreCase)) return -3m;
        return 0m;
    }

    private static decimal ResolveActivityPenalty(string activity)
    {
        if (activity.Equals("STRONG", StringComparison.OrdinalIgnoreCase)) return 2m;
        if (activity.Equals("GROWING", StringComparison.OrdinalIgnoreCase)) return 1m;
        if (activity.Equals("WEAK", StringComparison.OrdinalIgnoreCase)) return 0.5m;
        if (activity.Equals("RESTRICTED", StringComparison.OrdinalIgnoreCase)) return 4m;
        return 0m;
    }

    private sealed record TradeOption(
        string Waypoint,
        string System,
        int Price,
        int TradeVolume,
        string Supply,
        string Activity,
        string Type);
}
