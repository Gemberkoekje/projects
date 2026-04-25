using FluentAssertions;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class TradeOpportunityRepositoryTests : IntegrationTestBase
{
    private static TradeOpportunityDto MakeOpportunity(int id, string trade, decimal profitPerJump, int distance = 1) =>
        new(id, trade, "WP-BUY", "WP-SELL", 100, 200, 100, distance, profitPerJump, DateTimeOffset.UtcNow);

    [SkippableFact]
    public async Task ReplaceAllAsync_ThenGetTopRoutes_ReturnsSortedByProfit()
    {
        var repo = new TradeOpportunityRepository(Db);
        var opportunities = new List<TradeOpportunityDto>
        {
            MakeOpportunity(0, "IRON_ORE", profitPerJump: 50m),
            MakeOpportunity(0, "GOLD", profitPerJump: 200m),
            MakeOpportunity(0, "FUEL", profitPerJump: 10m),
        };

        await repo.ReplaceAllAsync(opportunities);

        await using var fresh = CreateFreshContext();
        var top = await new TradeOpportunityRepository(fresh).GetTopRoutesAsync(10);

        top.Should().HaveCount(3);
        top[0].TradeSymbol.Should().Be("GOLD");
        top[1].TradeSymbol.Should().Be("IRON_ORE");
        top[2].TradeSymbol.Should().Be("FUEL");
    }

    [SkippableFact]
    public async Task ReplaceAllAsync_CalledTwice_ReplacesExistingRows()
    {
        var repo = new TradeOpportunityRepository(Db);
        await repo.ReplaceAllAsync([MakeOpportunity(0, "OLD_TRADE", 100m)]);
        await repo.ReplaceAllAsync([MakeOpportunity(0, "NEW_TRADE", 150m)]);

        await using var fresh = CreateFreshContext();
        var top = await new TradeOpportunityRepository(fresh).GetTopRoutesAsync(10);

        top.Should().ContainSingle();
        top[0].TradeSymbol.Should().Be("NEW_TRADE");
    }

    [SkippableFact]
    public async Task GetBestRouteForCapacityAsync_FiltersCorrectly()
    {
        var repo = new TradeOpportunityRepository(Db);
        await repo.ReplaceAllAsync([
            MakeOpportunity(0, "IRON_ORE", 50m, distance: 1),   // profit/unit = 100, dist = 1 ✓
            MakeOpportunity(0, "GOLD", 200m, distance: 5),       // distance too high ✗
            MakeOpportunity(0, "FUEL", 5m, distance: 1),         // profit/unit = 100 but low PpJ ✓
        ]);

        await using var fresh = CreateFreshContext();
        var best = await new TradeOpportunityRepository(fresh)
            .GetBestRouteForCapacityAsync(cargoCapacity: 60, minProfitPerUnit: 50, maxDistanceJumps: 2);

        // GOLD excluded by distance; both IRON and FUEL have profit/unit 100 >= 50 and dist 1 <= 2
        best.Should().NotBeNull();
        best!.TradeSymbol.Should().Be("IRON_ORE"); // highest ProfitPerJump among eligible
    }

    [SkippableFact]
    public async Task GetTopRoutesAsync_WithMaxResults_LimitsOutput()
    {
        var repo = new TradeOpportunityRepository(Db);
        var many = Enumerable.Range(1, 10)
            .Select(i => MakeOpportunity(0, $"TRADE_{i}", i))
            .ToList();
        await repo.ReplaceAllAsync(many);

        await using var fresh = CreateFreshContext();
        var top = await new TradeOpportunityRepository(fresh).GetTopRoutesAsync(3);

        top.Should().HaveCount(3);
    }
}
