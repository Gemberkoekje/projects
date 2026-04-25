using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class MigrationTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task EnsureCreated_AppliesAllTables()
    {
        // A fresh context against the same container should be able to query each table.
        await using var ctx = CreateFreshContext();

        // If any table is missing EF will throw; otherwise counts return 0.
        var credentialCount = await ctx.Credentials.CountAsync();
        var agentCount = await ctx.Agents.CountAsync();
        var shipCount = await ctx.Ships.CountAsync();
        var contractCount = await ctx.Contracts.CountAsync();
        var marketCount = await ctx.Markets.CountAsync();
        var shipyardCount = await ctx.Shipyards.CountAsync();
        var waypointCount = await ctx.Waypoints.CountAsync();
        var systemCount = await ctx.Systems.CountAsync();
        var settingCount = await ctx.Settings.CountAsync();
        var assignmentCount = await ctx.ShipAssignments.CountAsync();
        var tradeCount = await ctx.TradeOpportunities.CountAsync();
        var logCount = await ctx.ActivityLogs.CountAsync();

        credentialCount.Should().Be(0);
        agentCount.Should().Be(0);
        shipCount.Should().Be(0);
        contractCount.Should().Be(0);
        marketCount.Should().Be(0);
        shipyardCount.Should().Be(0);
        waypointCount.Should().Be(0);
        systemCount.Should().Be(0);
        settingCount.Should().Be(0);
        assignmentCount.Should().Be(0);
        tradeCount.Should().Be(0);
        logCount.Should().Be(0);
    }
}
