using FluentAssertions;
using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>Slice 2.3: every setting that changes is a <c>SettingChanged</c> journal line, whoever changed it.</summary>
public sealed class SettingsJournalTests
{
    private readonly LogRecorder _log = new();

    [Fact]
    public async Task SetAsync_JournalsTheKeyAndBothValues()
    {
        await using var db = TestDbContextFactory.Create();
        await DefaultSettingsSeed.SeedAsync(db);

        await new SettingsRepository(db, _log.For<SettingsRepository>()).SetAsync("Automation.Enabled", "false");

        var changed = _log.Journal.Should().ContainSingle().Subject;
        changed.Level.Should().Be(LogLevel.Information);
        changed.EventKind.Should().Be("SettingChanged");
        changed.Message.Should().Be("SettingChanged: Automation.Enabled changed from true to false.");
        changed.Properties["Setting"].Should().Be("Automation.Enabled");
        changed.Properties["OldValue"].Should().Be("true");
        changed.Properties["NewValue"].Should().Be("false");
    }

    [Fact]
    public async Task SetAsync_JournalsNothing_WhenTheValueStaysTheSame()
    {
        await using var db = TestDbContextFactory.Create();
        await DefaultSettingsSeed.SeedAsync(db);

        await new SettingsRepository(db, _log.For<SettingsRepository>()).SetAsync("Automation.Enabled", "true");

        _log.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task SetAsync_HidesAUrl_WhichMayHoldASecret()
    {
        await using var db = TestDbContextFactory.Create();
        await DefaultSettingsSeed.SeedAsync(db);

        await new SettingsRepository(db, _log.For<SettingsRepository>()).SetAsync("Alerts.WebhookUrl", "https://hooks.example.com/services/T000/B000/s3cr3t");

        var changed = _log.Journal.Should().ContainSingle().Subject;
        changed.Properties["NewValue"].Should().Be("(hidden)");
        changed.Message.Should().NotContain("s3cr3t");
    }

    [Fact]
    public async Task ResetToDefaultsAsync_JournalsEachSettingItChanges()
    {
        await using var db = TestDbContextFactory.Create();
        await DefaultSettingsSeed.SeedAsync(db);
        var settings = new SettingsRepository(db, _log.For<SettingsRepository>());
        await settings.SetAsync("Mining.MaxDrones", "5");

        await settings.ResetToDefaultsAsync();

        _log.Journal.Select(entry => entry.Message).Should().Equal(
            "SettingChanged: Mining.MaxDrones changed from 20 to 5.",
            "SettingChanged: Mining.MaxDrones changed from 5 to 20.");
    }
}
