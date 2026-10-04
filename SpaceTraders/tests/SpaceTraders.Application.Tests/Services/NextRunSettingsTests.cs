using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>
/// D69 (asked on 2026-10-04): "a separate set of endpoints to only affect future runs. So I can have a setting for this
/// run (e.g. 50% split between miners and traders) and change those settings for the next run to see if it gives an
/// improvement." The next run is the agent the next server reset registers.
/// </summary>
public sealed class NextRunSettingsTests
{
    private readonly LogRecorder _log = new();

    [Fact]
    public async Task SetNextRunSettingAsync_LeavesTheRunThatRunsNowAlone_AndTheNextRunStartsWithIt()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using (var running = TestDbContextFactory.Create(databaseName))
        {
            await DefaultSettingsSeed.SeedAsync(running);
            var settings = Repository(running);

            (await settings.SetNextRunSettingAsync("Automation.MiningShipPercentage", "0.6")).Should().BeTrue();

            (await settings.GetRawAsync("Automation.MiningShipPercentage")).Should().Be("0.25");
            (await settings.GetNextRunSettingsAsync()).Should().ContainSingle(s => s.Key == "Automation.MiningShipPercentage")
                .Which.Should().Be(("Automation.MiningShipPercentage", "0.6", "decimal", DefaultSettingsSeed.DescriptionOf("Automation.MiningShipPercentage")!, false));
        }

        await using var next = TestDbContextFactory.Create(databaseName, "APPLICATION-TEST@2026-10-11");
        await DefaultSettingsSeed.SeedAsync(next);
        (await Repository(next).GetRawAsync("Automation.MiningShipPercentage")).Should().Be("0.6");
    }

    [Fact]
    public async Task GetNextRunSettingsAsync_GivesTheDefault_WhereNoValueIsChosen()
    {
        await using var db = TestDbContextFactory.Create();

        var nextRun = await Repository(db).GetNextRunSettingsAsync();

        nextRun.Select(s => s.Key).Should().BeEquivalentTo(DefaultSettingsSeed.RunSettings.Select(s => s.Key));
        nextRun.Should().OnlyContain(s => s.IsDefault);
        nextRun.Should().ContainSingle(s => s.Key == "Automation.Plan.Trading.Enabled").Which.Value.Should().Be("true");
    }

    [Theory]
    [InlineData("Runtime.ApiUnavailable")]
    [InlineData("No.Such.Setting")]
    public async Task SetNextRunSettingAsync_RefusesAKeyARunDoesNotStartWith(string key)
    {
        // A status flag isn't a setting to tune, and a key the seed doesn't hold would never be used.
        await using var db = TestDbContextFactory.Create();
        var settings = Repository(db);

        (await settings.SetNextRunSettingAsync(key, "true")).Should().BeFalse();
        (await settings.RemoveNextRunSettingAsync(key)).Should().BeFalse();

        (await db.NextRunSettings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RemoveNextRunSettingAsync_GivesTheNextRunsTheDefaultAgain()
    {
        await using var db = TestDbContextFactory.Create();
        var settings = Repository(db);
        await settings.SetNextRunSettingAsync("Mining.MaxDrones", "5");

        (await settings.RemoveNextRunSettingAsync("Mining.MaxDrones")).Should().BeTrue();

        (await settings.GetNextRunSettingsAsync()).Should().ContainSingle(s => s.Key == "Mining.MaxDrones")
            .Which.Should().Match<(string Key, string Value, string Type, string Description, bool IsDefault)>(s => s.Value == "20" && s.IsDefault);
    }

    [Fact]
    public async Task NextRunChanges_AreJournaled_WithASecretHidden()
    {
        await using var db = TestDbContextFactory.Create();
        var settings = Repository(db);

        await settings.SetNextRunSettingAsync("Mining.MaxDrones", "5");
        await settings.SetNextRunSettingAsync("Mining.MaxDrones", "5");
        await settings.RemoveNextRunSettingAsync("Mining.MaxDrones");
        await settings.SetNextRunSettingAsync("Alerts.WebhookUrl", "https://hooks.example.com/services/T000/B000/s3cr3t");

        _log.Journal.Select(entry => entry.Message).Should().Equal(
            "NextRunSettingChanged: Mining.MaxDrones for the next runs changed from (default) to 5.",
            "NextRunSettingChanged: Mining.MaxDrones for the next runs changed from 5 to (default).",
            "NextRunSettingChanged: Alerts.WebhookUrl for the next runs changed from (default) to (hidden).");
        _log.Journal[0].Properties["Setting"].Should().Be("Mining.MaxDrones");
    }

    [Fact]
    public async Task SetAsync_StopsTheSettingFollowingItsDefault()
    {
        // Whoever sets it, you or the bot (the size guard, the reset monitor): a restart then keeps the value.
        await using var db = TestDbContextFactory.Create();
        await DefaultSettingsSeed.SeedAsync(db);

        await Repository(db).SetAsync("Automation.Enabled", "false");

        (await db.Settings.AsNoTracking().SingleAsync(s => s.Key == "Automation.Enabled")).FollowsDefault.Should().BeFalse();
        (await db.NextRunSettings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ResetToDefaultsAsync_ForgetsTheValuesChosenForTheNextRuns_AndTheSettingsFollowTheirDefaultsAgain()
    {
        await using var db = TestDbContextFactory.Create();
        await DefaultSettingsSeed.SeedAsync(db);
        var settings = Repository(db);
        await settings.SetAsync("Mining.MaxDrones", "5");
        await settings.SetNextRunSettingAsync("Mining.MaxDrones", "5");

        await settings.ResetToDefaultsAsync();

        (await db.NextRunSettings.CountAsync()).Should().Be(0);
        (await db.Settings.AsNoTracking().SingleAsync(s => s.Key == "Mining.MaxDrones")).Should()
            .Match<Infrastructure.Persistence.Entities.AgentSetting>(s => s.Value == "20" && s.FollowsDefault);
        _log.Journal.Select(entry => entry.Message).Should().EndWith("NextRunSettingChanged: Mining.MaxDrones for the next runs changed from 5 to (default).");
    }

    private SettingsRepository Repository(Infrastructure.Persistence.SpaceTradersDbContext db) => new(db, _log.For<SettingsRepository>());
}
