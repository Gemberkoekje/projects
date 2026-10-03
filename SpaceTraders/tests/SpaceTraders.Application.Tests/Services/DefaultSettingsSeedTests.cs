using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
        "Automation.Plan.Roles.Enabled",
        "Automation.Plan.Contract.Enabled",
        "Automation.Plan.ProbeDeployment.Enabled",
        "Automation.Plan.Survey.Enabled",
        "Automation.Plan.Mining.Enabled",
        "Automation.Plan.Siphon.Enabled",
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

        // BudgetPolicy, MiningAutomationService, DataRetention, WebhookAlertNotifier.
        "FleetExpansion.MinCreditReserve",
        "Mining.MaxDrones",
        "ActivityLog.RetentionDays",
        "Alerts.WebhookUrl",

        // Trading (slice 6.5): TradeContextReader (the trading plan and the trade executor), and
        // MarketWatchService; slice 6.4: TradeContextReader (D24) and TradingAutomationService (D21).
        "Trade.MinProfitPerUnit",
        "Market.RefreshMinutes",
        "Trade.FuelReserveCredits",
        "Trade.ShipPurchases",

        // Surveying (slice 6.4): SurveyPlanService's stock of usable surveys per ore (D27).
        "Survey.StockPerOre",

        // Siphoning (slice 6.7): SiphonAutomationService's cap on siphon drones (D32).
        "Siphon.MaxDrones",

        // The role board (slice 6.9): RoleSettings, read by RolePlanService and RoleAdvisor (D39, D41).
        "Roles.ReconsiderMinutes",
        "Roles.HeadStartPercent",
        "Roles.ChainValueSharePercent",

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
}
