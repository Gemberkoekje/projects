using FluentAssertions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Tests.Services;

public sealed class TradeAnalyserTests
{
    private static MarketSnapshot BuildMarket(
        string waypoint,
        string system,
        (string symbol, string type, int purchasePrice, int sellPrice, int tradeVolume)[] goods,
        string[]? imports = null,
        string[]? exports = null,
        string[]? exchange = null)
    {
        var goodSnapshots = goods
            .Select(g => new TradeGoodSnapshot(g.symbol, g.type, g.purchasePrice, g.sellPrice, g.tradeVolume, "ABUNDANT"))
            .ToList();
        return new MarketSnapshot(waypoint, system, goodSnapshots, imports ?? [], exports ?? [], exchange ?? []);
    }

    [Fact]
    public void ComputeOpportunities_SinglePair_ReturnsRoute()
    {
        var markets = new[]
        {
            BuildMarket("SYS-A-WP-1", "SYS-A", [("IRON_ORE", "EXPORT", 100, 80, 10)]),
            BuildMarket("SYS-A-WP-2", "SYS-A", [("IRON_ORE", "IMPORT", 200, 150, 10)]),
        };

        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities(markets);

        results.Should().ContainSingle(r =>
            r.TradeSymbol == "IRON_ORE" &&
            r.BuyWaypoint == "SYS-A-WP-1" &&
            r.BuyPrice == 100 &&
            r.SellPrice == 150 &&
            r.ProfitPerUnit == 50);
    }

    [Fact]
    public void ComputeOpportunities_SameWaypoint_Excluded()
    {
        var markets = new[]
        {
            BuildMarket("SYS-A-WP-1", "SYS-A", [("IRON_ORE", "EXPORT", 100, 150, 10)]),
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
            BuildMarket("SYS-A-WP-1", "SYS-A", [("IRON_ORE", "EXPORT", 200, 80, 10)]),
            BuildMarket("SYS-A-WP-2", "SYS-A", [("IRON_ORE", "IMPORT", 80, 100, 10)]),
        };

        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities(markets, minProfitPerUnit: 0);

        results.Should().NotContain(r => r.ProfitPerUnit < 0);
    }

    [Fact]
    public void ComputeOpportunities_SortedByProfitPerJumpDescending()
    {
        var markets = new[]
        {
            BuildMarket("SYS-A-WP-1", "SYS-A", [("IRON_ORE", "EXPORT", 100, 0, 10), ("GOLD", "EXPORT", 100, 0, 10)]),
            BuildMarket("SYS-A-WP-2", "SYS-A", [("IRON_ORE", "IMPORT", 0, 150, 10), ("GOLD", "IMPORT", 0, 300, 10)]),
        };

        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities(markets);

        results.First().TradeSymbol.Should().Be("GOLD");
        results.Last().TradeSymbol.Should().Be("IRON_ORE");
    }

    [Fact]
    public void ComputeOpportunities_CrossSystemRoute_DistanceIsOne()
    {
        var markets = new[]
        {
            BuildMarket("SYS-A-WP-1", "SYS-A", [("IRON_ORE", "EXPORT", 100, 0, 10)]),
            BuildMarket("SYS-B-WP-1", "SYS-B", [("IRON_ORE", "IMPORT", 0, 200, 10)]),
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
            BuildMarket("SYS-A-WP-1", "SYS-A", [("IRON_ORE", "EXPORT", 100, 0, 10)]),
            BuildMarket("SYS-A-WP-2", "SYS-A", [("IRON_ORE", "IMPORT", 0, 200, 10)]),
        };

        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities(markets);

        results.Should().ContainSingle(r =>
            r.DistanceJumps == 0 &&
            r.ProfitPerJump == 100m);
    }

    [Fact]
    public void ComputeOpportunities_PrioritizesSupplyChainRoutes()
    {
        var markets = new[]
        {
            BuildMarket("SYS-A-WP-1", "SYS-A", [("IRON_ORE", "EXPORT", 100, 0, 10), ("COPPER", "EXPORT", 100, 0, 10)]),
            BuildMarket("SYS-A-WP-2", "SYS-A", [("IRON_ORE", "IMPORT", 0, 250, 10)], exports: ["STEEL"]),
            BuildMarket("SYS-A-WP-3", "SYS-A", [("COPPER", "IMPORT", 0, 300, 10)]),
        };

        var analyser = new TradeAnalyser();
        var results = analyser.ComputeOpportunities(markets);

        results.First().TradeSymbol.Should().Be("IRON_ORE");
        results.First().SupportsSupplyChain.Should().BeTrue();
        results.First().SupplyChainDepth.Should().Be(1);
    }

    [Fact]
    public void SelectBestRoute_RespectsMinProfitFilter()
    {
        var routes = new[]
        {
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(1, "GOLD", "WP-1", "WP-2", 100, 300, 200, 0, 200m, false, 0, DateTimeOffset.UtcNow),
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(2, "IRON", "WP-1", "WP-3", 100, 150, 50, 0, 50m, false, 0, DateTimeOffset.UtcNow),
        };

        var analyser = new TradeAnalyser();
        var best = analyser.SelectBestRoute(routes, cargoCapacity: 60, minProfitPerUnit: 100, maxDistanceJumps: 5);

        best.Should().NotBeNull();
        best!.TradeSymbol.Should().Be("GOLD");
    }

    [Fact]
    public void SelectBestRoute_PrefersSupplyChain_WhenEnabled()
    {
        var routes = new[]
        {
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(1, "GOLD", "WP-1", "WP-2", 100, 400, 300, 0, 300m, false, 0, DateTimeOffset.UtcNow),
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(2, "IRON", "WP-1", "WP-3", 100, 250, 150, 0, 150m, true, 2, DateTimeOffset.UtcNow),
        };

        var analyser = new TradeAnalyser();
        var best = analyser.SelectBestRoute(routes, cargoCapacity: 60, minProfitPerUnit: 0, maxDistanceJumps: 5);

        best.Should().NotBeNull();
        best!.TradeSymbol.Should().Be("IRON");
    }

    [Fact]
    public void SelectBestRoute_NoRouteMeetsMinProfit_ReturnsNull()
    {
        var routes = new[]
        {
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(1, "IRON", "WP-1", "WP-3", 100, 150, 50, 0, 50m, false, 0, DateTimeOffset.UtcNow),
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
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(1, "GOLD", "WP-1", "WP-2", 100, 300, 200, 3, 67m, false, 0, DateTimeOffset.UtcNow),
            new SpaceTraders.Application.DTOs.TradeOpportunityDto(2, "IRON", "WP-1", "WP-3", 100, 150, 50, 0, 50m, false, 0, DateTimeOffset.UtcNow),
        };

        var analyser = new TradeAnalyser();
        var best = analyser.SelectBestRoute(routes, cargoCapacity: 60, minProfitPerUnit: 0, maxDistanceJumps: 1);

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
