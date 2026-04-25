using FluentAssertions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Tests.Services;

public sealed class TradeAnalyserTests
{
    private static MarketSnapshot BuildMarket(string waypoint, string system, params (string symbol, string type, int purchasePrice, int sellPrice, int tradeVolume)[] goods)
    {
        var goodSnapshots = goods
            .Select(g => new TradeGoodSnapshot(g.symbol, g.type, g.purchasePrice, g.sellPrice, g.tradeVolume, "ABUNDANT"))
            .ToList();
        return new MarketSnapshot(waypoint, system, goodSnapshots);
    }

    [Fact]
    public void ComputeOpportunities_SinglePair_ReturnsRoute()
    {
        var markets = new[]
        {
            BuildMarket("SYS-A-WP-1", "SYS-A", ("IRON_ORE", "EXPORT", 100, 80, 10)),
            BuildMarket("SYS-A-WP-2", "SYS-A", ("IRON_ORE", "IMPORT", 200, 150, 10))
        };

        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities(markets);

        results.Should().ContainSingle(r =>
            r.TradeSymbol == "IRON_ORE" &&
            r.BuyWaypoint == "SYS-A-WP-1" &&
            r.SellWaypoint == "SYS-A-WP-2" &&
            r.BuyPrice == 100 &&
            r.SellPrice == 150 &&
            r.ProfitPerUnit == 50);
    }

    [Fact]
    public void ComputeOpportunities_SameWaypoint_Excluded()
    {
        // Same waypoint for buy and sell should not produce a route
        var markets = new[]
        {
            BuildMarket("SYS-A-WP-1", "SYS-A", ("IRON_ORE", "EXPORT", 100, 150, 10))
        };

        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities(markets);

        results.Should().BeEmpty();
    }

    [Fact]
    public void ComputeOpportunities_NegativeProfit_ExcludedByMinProfit()
    {
        var markets = new[]
        {
            BuildMarket("SYS-A-WP-1", "SYS-A", ("IRON_ORE", "EXPORT", 200, 80, 10)),
            BuildMarket("SYS-A-WP-2", "SYS-A", ("IRON_ORE", "IMPORT", 80, 100, 10))
        };

        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities(markets, minProfitPerUnit: 0);

        // Profit = 100 - 200 = -100, filtered out because < 0
        results.Should().NotContain(r => r.ProfitPerUnit < 0);
    }

    [Fact]
    public void ComputeOpportunities_SortedByProfitPerJumpDescending()
    {
        var markets = new[]
        {
            BuildMarket("SYS-A-WP-1", "SYS-A", ("IRON_ORE", "EXPORT", 100, 0, 10), ("GOLD", "EXPORT", 100, 0, 10)),
            BuildMarket("SYS-A-WP-2", "SYS-A", ("IRON_ORE", "IMPORT", 0, 150, 10), ("GOLD", "IMPORT", 0, 300, 10))
        };

        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities(markets);

        // GOLD profit = 200, IRON_ORE profit = 50 → GOLD should come first
        results.First().TradeSymbol.Should().Be("GOLD");
        results.Last().TradeSymbol.Should().Be("IRON_ORE");
    }

    [Fact]
    public void ComputeOpportunities_CrossSystemRoute_DistanceIsOne()
    {
        var markets = new[]
        {
            BuildMarket("SYS-A-WP-1", "SYS-A", ("IRON_ORE", "EXPORT", 100, 0, 10)),
            BuildMarket("SYS-B-WP-1", "SYS-B", ("IRON_ORE", "IMPORT", 0, 200, 10))
        };

        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities(markets);

        results.Should().ContainSingle(r =>
            r.DistanceJumps == 1 &&
            r.ProfitPerJump == 100m / 2m);
    }

    [Fact]
    public void ComputeOpportunities_SameSystemRoute_DistanceIsZero()
    {
        var markets = new[]
        {
            BuildMarket("SYS-A-WP-1", "SYS-A", ("IRON_ORE", "EXPORT", 100, 0, 10)),
            BuildMarket("SYS-A-WP-2", "SYS-A", ("IRON_ORE", "IMPORT", 0, 200, 10))
        };

        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities(markets);

        results.Should().ContainSingle(r =>
            r.DistanceJumps == 0 &&
            r.ProfitPerJump == 100m);
    }

    [Fact]
    public void SelectBestRoute_RespectsMinProfitFilter()
    {
        var routes = new[]
        {
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(1, "GOLD", "WP-1", "WP-2", 100, 300, 200, 0, 200m, DateTimeOffset.UtcNow),
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(2, "IRON", "WP-1", "WP-3", 100, 150, 50, 0, 50m, DateTimeOffset.UtcNow)
        };

        var analyser = new TradeAnalyser();
        var best = analyser.SelectBestRoute(routes, cargoCapacity: 60, minProfitPerUnit: 100, maxDistanceJumps: 5);

        best.Should().NotBeNull();
        best!.TradeSymbol.Should().Be("GOLD");
    }

    [Fact]
    public void SelectBestRoute_NoRouteMeetsMinProfit_ReturnsNull()
    {
        var routes = new[]
        {
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(1, "IRON", "WP-1", "WP-3", 100, 150, 50, 0, 50m, DateTimeOffset.UtcNow)
        };

        var analyser = new TradeAnalyser();
        var best = analyser.SelectBestRoute(routes, cargoCapacity: 60, minProfitPerUnit: 100, maxDistanceJumps: 5);

        best.Should().BeNull();
    }

    [Fact]
    public void SelectBestRoute_RespectsMaxDistanceFilter()
    {
        var routes = new[]
        {
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(1, "GOLD", "WP-1", "WP-2", 100, 300, 200, 3, 67m, DateTimeOffset.UtcNow),
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(2, "IRON", "WP-1", "WP-3", 100, 150, 50, 0, 50m, DateTimeOffset.UtcNow)
        };

        var analyser = new TradeAnalyser();
        var best = analyser.SelectBestRoute(routes, cargoCapacity: 60, minProfitPerUnit: 0, maxDistanceJumps: 1);

        // GOLD has distance 3 which exceeds maxDistanceJumps=1; should return IRON
        best.Should().NotBeNull();
        best!.TradeSymbol.Should().Be("IRON");
    }

    [Fact]
    public void ComputeOpportunities_EmptyMarkets_ReturnsEmpty()
    {
        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities([]);

        results.Should().BeEmpty();
    }
}
