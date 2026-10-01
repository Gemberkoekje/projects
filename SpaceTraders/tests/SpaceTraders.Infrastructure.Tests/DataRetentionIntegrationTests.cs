using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Tests;

/// <summary>B3: every table that grows is pruned, by the policy in <see cref="DataRetention"/>.</summary>
[Trait("Category", "Integration")]
public sealed class DataRetentionIntegrationTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task PruneAsync_RunsForEveryPrunedTable()
    {
        var retention = new DataRetention(Db);

        foreach (var table in retention.PrunedTables)
        {
            var prune = async () => await retention.PruneAsync(table, Now);
            await prune.Should().NotThrowAsync(table);
        }
    }

    [SkippableFact]
    public async Task StartupSnapshots_KeepTheFirstAndTheLastTen()
    {
        for (var day = 1; day <= 15; day++)
        {
            Db.StartupSnapshots.Add(new StartupSnapshot { AgentId = Db.AgentId, CapturedAt = Now.AddDays(day - 20), IsInitialSnapshot = day == 1, SnapshotJson = "{}" });
        }

        await Db.SaveChangesAsync();

        await new DataRetention(Db).PruneAsync("startup_snapshots", Now);

        var kept = await Db.StartupSnapshots.AsNoTracking().OrderBy(s => s.CapturedAt).Select(s => s.CapturedAt).ToListAsync();
        kept.Should().Equal(new[] { 1, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }.Select(day => Now.AddDays(day - 20)));
    }

    [SkippableFact]
    public async Task Runs_AreKeptForAYear_WhicheverAgentTheyBelongTo()
    {
        Db.Runs.Add(new Run { Id = Guid.NewGuid(), AgentId = "AGENT@2025-09-01", Name = "old", StrategyLabel = "default", StartedAt = Now.AddDays(-400) });
        Db.Runs.Add(new Run { Id = Guid.NewGuid(), AgentId = "AGENT@2026-09-13", Name = "earlier agent", StrategyLabel = "default", StartedAt = Now.AddDays(-20) });
        Db.Runs.Add(new Run { Id = Guid.NewGuid(), AgentId = Db.AgentId, Name = "current", StrategyLabel = "default", StartedAt = Now.AddDays(-2) });
        await Db.SaveChangesAsync();

        await new DataRetention(Db).PruneAsync("runs", Now);

        (await Db.Runs.IgnoreQueryFilters().Select(r => r.Name).ToListAsync()).Should().BeEquivalentTo("earlier agent", "current");
    }

    [SkippableFact]
    public async Task MarketPriceSamples_KeepTheFirstSampleOfEachHour_AfterAWeek()
    {
        var hour = Now.AddDays(-10);
        Sample(hour.AddMinutes(1), "FOOD", 100);
        Sample(hour.AddMinutes(20), "FOOD", 101);
        Sample(hour.AddMinutes(40), "FOOD", 102);
        Sample(hour.AddMinutes(30), "FUEL", 200);
        Sample(hour.AddHours(1).AddMinutes(5), "FOOD", 103);
        Sample(Now.AddDays(-1), "FOOD", 104);
        Sample(Now.AddDays(-1).AddMinutes(1), "FOOD", 105);
        Sample(Now.AddDays(-100), "FOOD", 106);
        await Db.SaveChangesAsync();

        await new DataRetention(Db).PruneAsync("market_price_samples", Now);

        (await Db.MarketPriceSamples.Select(s => s.SellPrice).ToListAsync()).Should().BeEquivalentTo([100, 200, 103, 104, 105]);
    }

    [SkippableFact]
    public async Task ActivityLogs_FollowTheRetentionSetting()
    {
        Db.Settings.Add(new AgentSetting { AgentId = Db.AgentId, Key = DataRetention.ActivityLogRetentionSetting, Value = "60", Type = "int", Description = "Activity log retention" });
        Log(Now.AddDays(-45), "kept");
        Log(Now.AddDays(-61), "pruned");
        await Db.SaveChangesAsync();

        await new DataRetention(Db).PruneAsync("activity_logs", Now);

        (await Db.ActivityLogs.Select(l => l.Message).ToListAsync()).Should().Equal("kept");
    }

    private void Sample(DateTimeOffset observedAt, string good, int sellPrice)
        => Db.MarketPriceSamples.Add(new MarketPriceSample { AgentId = Db.AgentId, WaypointSymbol = "X1-AB-1", GoodSymbol = good, ObservedAt = observedAt, SellPrice = sellPrice });

    private void Log(DateTimeOffset timestamp, string message)
        => Db.ActivityLogs.Add(new ActivityLog { AgentId = Db.AgentId, ShipSymbol = "AGENT-1", EventType = "Test", Message = message, Timestamp = timestamp });
}
