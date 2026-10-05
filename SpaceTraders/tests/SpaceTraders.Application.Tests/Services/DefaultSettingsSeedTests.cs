using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>
/// B18, D10: the seed holds only settings that code which runs reads, so the settings page shows
/// only settings that work. A feature that needs a new setting adds it here, with its reader.
/// </summary>
public sealed class DefaultSettingsSeedTests
{
    private static readonly string[] SettingsThatWork =
    [
        // The kill switch and the plan switches: the tick, goal steps, startup recovery.
        "Automation.Enabled",
        "Automation.Plan.Scout.Enabled",
        "Automation.Plan.Explore.Enabled",
        "Automation.Plan.Roles.Enabled",
        "Automation.Plan.Contract.Enabled",
        "Automation.Plan.ProbeDeployment.Enabled",
        "Automation.Plan.Survey.Enabled",
        "Automation.Plan.Mining.Enabled",
        "Automation.Plan.Siphon.Enabled",
        "Automation.Plan.Construction.Enabled",
        "Automation.Plan.Trading.Enabled",
        "Automation.Plan.SpareTime.Enabled",

        // ShipGoalExecutorService, OutagePauseHandler, DatabaseSizeGuardService.
        "Automation.CircuitBreaker.MaxGoalStepsPerMinute",
        "Api.BadGatewayPauseMinutes",
        "Database.SoftLimitMegabytes",
        "Database.HardLimitMegabytes",

        // The health rules' thresholds (phase 3): ContractStalledRule, ContractDeadlineAtRiskRule,
        // ShipStuckRule, ShipLeftIdleRule, RepeatingErrorRule, CreditsUnchangedRule, ApiThrottledRule.
        "Health.Contract.MaxHoursWithoutProgress",
        "Health.Contract.DeadlineHours",
        "Health.Contract.MinDeliveredPercent",
        "Health.Ship.MaxMinutesWithoutChange",
        "Health.Ship.MaxIdleMinutes",
        "Health.Errors.MaxRepeatsIn10Minutes",
        "Health.Credits.MaxHoursUnchanged",
        "Health.Api.Max429sPerHour",

        // BudgetPolicy (with the credit reserve's growth, D51), MiningAutomationService (with the jump gate's miners, D92),
        // DataRetention, WebhookAlertNotifier.
        "FleetExpansion.MinCreditReserve",
        "FleetExpansion.ReservePerTradingCargoUnit",
        "Mining.MaxDrones",
        "Mining.GateMinerIntervalMinutes",
        "ActivityLog.RetentionDays",
        "Alerts.WebhookUrl",

        // Trading (slice 6.5): TradeContextReader (the trading plan and the trade executor), and
        // MarketWatchService; slice 6.4: TradeContextReader (D24) and TradingAutomationService (D21, D88).
        "Trade.MinProfitPerUnit",
        "Market.RefreshMinutes",
        "Trade.FuelReserveCredits",
        "Trade.ShipPurchases",
        "Trade.ShipPurchaseMinRouteProfit",
        "Trade.ShipPurchaseWaitMinutes",

        // Surveying (slice 6.4): SurveyPlanService's stock of usable surveys per ore (D27).
        "Survey.StockPerOre",

        // Siphoning (slice 6.7): SiphonAutomationService's cap on siphon drones (D32).
        "Siphon.MaxDrones",

        // The role board (slice 6.9): RoleSettings, read by RolePlanService and RoleAdvisor (D39, D41); how many ships build
        // the jump gate, read by RolePlanService and ConstructionPlanService (slice 6.6, D65).
        "Roles.ReconsiderMinutes",
        "Roles.HeadStartPercent",
        "Roles.ChainValueSharePercent",
        "Construction.Ships",

        // Read, without changing what the bot does: the run's strategy label (RunLifecycleService)
        // and the market views (MarketsEndpoints, QueryHandlers).
        "FleetExpansion.PreferredShipType",
        "Automation.MiningShipPercentage",
        "Trade.MaxHaulDistance",
    ];

    [Fact]
    public async Task SeedAsync_SeedsOnlySettingsThatWork_AndTheRuntimeFlags()
    {
        // B18: 26 seeded settings were read by nothing, or only by code that never runs.
        await using var db = TestDbContextFactory.Create();

        await DefaultSettingsSeed.SeedAsync(db);

        var keys = await db.Settings.AsNoTracking().Select(s => s.Key).ToListAsync();
        keys.Where(key => !key.StartsWith("Runtime.", StringComparison.Ordinal)).Should().BeEquivalentTo(SettingsThatWork);
    }

    [Fact]
    public void RunSettings_AreTheSettingsThatWork()
    {
        // D69: a run can be given a value of its own for every setting but the status flags.
        DefaultSettingsSeed.RunSettings.Select(setting => setting.Key).Should().BeEquivalentTo(SettingsThatWork);
        DefaultSettingsSeed.IsRunSetting("Automation.MiningShipPercentage").Should().BeTrue();
        DefaultSettingsSeed.IsRunSetting("Runtime.ApiUnavailable").Should().BeFalse();
        DefaultSettingsSeed.IsRunSetting("Navigation.MaxJumps").Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_StartsANewAgent_WithTheValuesChosenForTheNextRuns_AndTheDefaultsForTheRest()
    {
        // D69: values chosen for the next run, which differ from the defaults.
        await using var db = TestDbContextFactory.Create();
        db.NextRunSettings.Add(new NextRunSetting { Key = "Automation.MiningShipPercentage", Value = "0.5" });
        db.NextRunSettings.Add(new NextRunSetting { Key = "Automation.Plan.Trading.Enabled", Value = "false" });
        await db.SaveChangesAsync();

        await DefaultSettingsSeed.SeedAsync(db);

        var settings = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key);
        settings["Automation.MiningShipPercentage"].Value.Should().Be("0.5");
        settings["Automation.MiningShipPercentage"].FollowsDefault.Should().BeFalse();
        settings["Automation.Plan.Trading.Enabled"].Value.Should().Be("false");
        settings["Automation.Plan.Mining.Enabled"].Value.Should().Be("true");
        settings["Automation.Plan.Mining.Enabled"].FollowsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task SeedAsync_GivesTheAgentThatRuns_TheDefaultAsThisVersionHasIt_WhereItStillFollowsTheDefault()
    {
        // D69: the agent the reset of 2026-10-04 registered was seeded with most plans off, D9's old default. Nobody set
        // them since, so the next start switches them on, each a SettingChanged journal line.
        var log = new LogRecorder();
        await using var db = TestDbContextFactory.Create();
        db.Settings.Add(Stored("Automation.Plan.Trading.Enabled", "false", followsDefault: true));
        await db.SaveChangesAsync();

        await DefaultSettingsSeed.SeedAsync(db, log.For<DefaultSettingsSeedTests>());

        (await db.Settings.AsNoTracking().SingleAsync(s => s.Key == "Automation.Plan.Trading.Enabled")).Value.Should().Be("true");
        log.Journal.Select(entry => entry.Message).Should().Equal("SettingChanged: Automation.Plan.Trading.Enabled changed from false to true.");
    }

    [Fact]
    public async Task SeedAsync_KeepsASettingSomeoneSet_AndTheStatusFlags()
    {
        // You switched the plan off, the size guard switched automation off, a status flag is up: a restart leaves them.
        await using var db = TestDbContextFactory.Create();
        db.Settings.Add(Stored("Automation.Plan.Trading.Enabled", "false", followsDefault: false));
        db.Settings.Add(Stored("Automation.Enabled", "false", followsDefault: false));
        db.Settings.Add(Stored("Runtime.ApiUnavailable", "true", followsDefault: true));
        await db.SaveChangesAsync();

        await DefaultSettingsSeed.SeedAsync(db);

        var settings = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        settings["Automation.Plan.Trading.Enabled"].Should().Be("false");
        settings["Automation.Enabled"].Should().Be("false");
        settings["Runtime.ApiUnavailable"].Should().Be("true");
    }

    [Fact]
    public async Task SeedAsync_LeavesTheAgentThatRuns_ItsValues_WhenANextRunValueIsChosen()
    {
        // D69: a value chosen for the next runs only reaches the agent the next reset registers, not the one a restart
        // brings back.
        var databaseName = Guid.NewGuid().ToString("N");
        await using (var running = TestDbContextFactory.Create(databaseName))
        {
            await DefaultSettingsSeed.SeedAsync(running);
            running.NextRunSettings.Add(new NextRunSetting { Key = "Automation.MiningShipPercentage", Value = "0.5" });
            await running.SaveChangesAsync();
            await DefaultSettingsSeed.SeedAsync(running);
            (await running.Settings.AsNoTracking().SingleAsync(s => s.Key == "Automation.MiningShipPercentage")).Value.Should().Be("0.25");
        }

        await using var next = TestDbContextFactory.Create(databaseName, "APPLICATION-TEST@2026-10-11");
        await DefaultSettingsSeed.SeedAsync(next);
        (await next.Settings.AsNoTracking().SingleAsync(s => s.Key == "Automation.MiningShipPercentage")).Value.Should().Be("0.5");
    }

    private static AgentSetting Stored(string key, string value, bool followsDefault) => new()
    {
        AgentId = TestDbContextFactory.AgentId,
        Key = key,
        Value = value,
        Type = "bool",
        Description = string.Empty,
        FollowsDefault = followsDefault,
    };
}
