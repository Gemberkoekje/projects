using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Seed;

/// <summary>
/// The settings every agent starts with. Each one is read by code that runs (B18, D10); the
/// <c>Runtime.*</c> keys are status flags. A feature that needs a new setting adds it here.
/// </summary>
public static class DefaultSettingsSeed
{
    private static readonly IReadOnlyList<AgentSetting> Defaults =
    [
        new AgentSetting { Key = "FleetExpansion.MinCreditReserve",       Value = "100000",              Type = "long",    Description = "Credits to always keep in bank" },
        new AgentSetting { Key = "FleetExpansion.PreferredShipType",      Value = "SHIP_MINING_DRONE",   Type = "string",  Description = "Default ship type to buy" },
        new AgentSetting { Key = "Trade.MinProfitPerUnit",                Value = "200",                 Type = "int",     Description = "Credits per unit, after fuel, a trade trip must earn to be started, and to be carried on when prices change (0 = any profit)" },
        new AgentSetting { Key = "Trade.FuelReserveCredits",              Value = "5000",                Type = "long",    Description = "Credits a cargo purchase must leave, so ships can always buy fuel: below them only fuel is bought (D24)" },
        new AgentSetting { Key = "Trade.ShipPurchases",                   Value = "SHIP_LIGHT_SHUTTLE,SHIP_LIGHT_HAULER,SHIP_LIGHT_HAULER", Type = "string", Description = "Cargo ships the trading plan buys, in order: the Nth while the fleet has fewer than N cargo ships, above the credit reserve (D21; empty = none)" },
        new AgentSetting { Key = "Trade.MaxHaulDistance",                 Value = "5",                   Type = "int",     Description = "Max jumps between buy/sell waypoints" },
        new AgentSetting { Key = "Automation.Enabled",                    Value = "true",                Type = "bool",    Description = "Master kill-switch for automation" },
        new AgentSetting { Key = "Automation.Plan.Scout.Enabled",           Value = "true",                Type = "bool",    Description = "Run the scout plan: visit every marketplace in the starting system once" },
        new AgentSetting { Key = "Automation.Plan.Contract.Enabled",        Value = "true",                Type = "bool",    Description = "Run the contract plan: take one mineral contract and fulfil it (may buy a mining drone)" },
        new AgentSetting { Key = "Automation.Plan.ProbeDeployment.Enabled", Value = "false",               Type = "bool",    Description = "Run the probe plan: a probe for every market in the HQ system, roaming between the stalest nearby markets until there are enough (buys SHIP_PROBE above the credit reserve, D29)" },
        new AgentSetting { Key = "Automation.Plan.Survey.Enabled",          Value = "false",               Type = "bool",    Description = "Run the survey plan: ships that can survey survey, and only that, the contract's ore first, then ores the markets buy (D20)" },
        new AgentSetting { Key = "Automation.Plan.Mining.Enabled",          Value = "false",               Type = "bool",    Description = "Run the mining plan: miners mine surveyed ores, else ores in low supply, and sell them (buys mining drones)" },
        new AgentSetting { Key = "Automation.Plan.Trading.Enabled",         Value = "false",               Type = "bool",    Description = "Run the trading plan: idle ships with a cargo hold carry goods between markets for profit (buys cargo ships, Trade.ShipPurchases)" },
        new AgentSetting { Key = "Market.RefreshMinutes",                   Value = "5",                   Type = "int",     Description = "Minutes between price refreshes of a market where one of our ships is (0 = off)" },
        new AgentSetting { Key = "Automation.CircuitBreaker.MaxGoalStepsPerMinute", Value = "60",         Type = "int",     Description = "Goal steps per ship per minute above which the ship's goal is blocked as a runaway (the tick alone takes 12)" },
        new AgentSetting { Key = "Database.SoftLimitMegabytes",             Value = "1024",                Type = "int",     Description = "Database size (MB) above which the bot logs a warning (D8)" },
        new AgentSetting { Key = "Database.HardLimitMegabytes",             Value = "3072",                Type = "int",     Description = "Database size (MB) above which the bot switches automation off (D8)" },
        new AgentSetting { Key = "Api.BadGatewayPauseMinutes",              Value = "3",                   Type = "int",     Description = "Minutes without any API call after a 502 (the API's DDoS protection), as the API guide asks" },
        new AgentSetting { Key = "Health.Contract.MaxHoursWithoutProgress", Value = "4",                   Type = "int",     Description = "Hours the contract the bot works on may go without a delivery before it is an anomaly (ContractStalled)" },
        new AgentSetting { Key = "Health.Contract.DeadlineHours",           Value = "24",                  Type = "int",     Description = "Hours before a contract's deadline from which Health.Contract.MinDeliveredPercent must be delivered (ContractDeadlineAtRisk)" },
        new AgentSetting { Key = "Health.Contract.MinDeliveredPercent",     Value = "50",                  Type = "int",     Description = "Percentage of a contract's units that must be delivered by Health.Contract.DeadlineHours before its deadline (ContractDeadlineAtRisk)" },
        new AgentSetting { Key = "Health.Ship.MaxMinutesWithoutChange",     Value = "30",                  Type = "int",     Description = "Minutes a ship with work, not in transit, may go without the bot updating it (ShipStuck)" },
        new AgentSetting { Key = "Health.Ship.MaxIdleMinutes",              Value = "10",                  Type = "int",     Description = "Minutes a ship may stay without a goal or assignment while a plan has work for it (ShipLeftIdle)" },
        new AgentSetting { Key = "Health.Errors.MaxRepeatsIn10Minutes",     Value = "5",                   Type = "int",     Description = "Times one log statement may log a warning or error in 10 minutes (RepeatingError)" },
        new AgentSetting { Key = "Health.Credits.MaxHoursUnchanged",        Value = "24",                  Type = "int",     Description = "Hours the credits may stay the same while the fleet has work (CreditsUnchanged)" },
        new AgentSetting { Key = "Health.Api.Max429sPerHour",               Value = "10",                  Type = "int",     Description = "429 answers from the SpaceTraders API allowed in an hour (ApiThrottled)" },
        new AgentSetting { Key = "ActivityLog.RetentionDays",             Value = "30",                  Type = "int",     Description = "Days to retain activity log entries" },
        new AgentSetting { Key = "Alerts.WebhookUrl",                     Value = "",                    Type = "string",  Description = "Slack/webhook URL for operator alerts (empty = disabled)" },
        new AgentSetting { Key = "Automation.MiningShipPercentage",              Value = "0.25",                Type = "decimal", Description = "Fraction of mining-capable ships assigned to resource extraction roles" },
        new AgentSetting { Key = "Survey.StockPerOre",                           Value = "2",                   Type = "int",     Description = "Usable surveys the survey plan keeps of each ore at its asteroid; with that many for every ore, the surveyors wait until one runs out (D27)" },
        new AgentSetting { Key = "Mining.MaxDrones",                             Value = "20",                  Type = "int",     Description = "Maximum number of mining drones to keep in service for mining automation" },
        new AgentSetting { Key = "Runtime.Reset.Next",                            Value = "",                    Type = "string",  Description = "Last observed server reset timestamp (ISO-8601)" },
        new AgentSetting { Key = "Runtime.Reset.Warning",                         Value = "false",               Type = "bool",    Description = "True when a near-term reset warning is active" },
        new AgentSetting { Key = "Runtime.ApiUnavailable",                        Value = "false",               Type = "bool",    Description = "True when API probes detect upstream unavailability" },
        new AgentSetting { Key = "Runtime.CacheDivergenceDetected",               Value = "false",               Type = "bool",    Description = "True when cache divergence health checks fail" },
        new AgentSetting { Key = "Runtime.TokenResetMismatchDetected",            Value = "false",               Type = "bool",    Description = "True when token reset-date mismatch has been detected" },
        new AgentSetting { Key = "Runtime.AutomationPausedByReset",               Value = "false",               Type = "bool",    Description = "True when automation was auto-paused for an imminent reset" },
        new AgentSetting { Key = "Runtime.Alert.ApiUnavailable",                  Value = "false",               Type = "bool",    Description = "Dashboard alert: API currently unavailable" },
        new AgentSetting { Key = "Runtime.Alert.TokenResetMismatch",              Value = "false",               Type = "bool",    Description = "Dashboard alert: token reset mismatch encountered" },
        new AgentSetting { Key = "Runtime.Alert.CacheDivergence",                 Value = "false",               Type = "bool",    Description = "Dashboard alert: cache divergence detected" },
        new AgentSetting { Key = "Runtime.Alert.AutomationDisabled",              Value = "false",               Type = "bool",    Description = "Dashboard alert: automation disabled" },
        new AgentSetting { Key = "Runtime.Alert.ContractDeadlinesApproaching",    Value = "false",               Type = "bool",    Description = "Dashboard alert: contract deadlines within 6 hours" },
        new AgentSetting { Key = "Runtime.Alert.ResetUpcoming",                   Value = "false",               Type = "bool",    Description = "Dashboard alert: server reset is approaching" },
    ];

    /// <summary>
    /// Seeds missing settings (does not overwrite existing values).
    /// </summary>
    public static async Task SeedAsync(SpaceTradersDbContext db, CancellationToken cancellationToken = default)
    {
        var existingKeys = await db.Settings
            .AsNoTracking()
            .Select(s => s.Key)
            .ToHashSetAsync(cancellationToken);

        foreach (var setting in Defaults)
        {
            if (!existingKeys.Contains(setting.Key))
            {
                db.Settings.Add(new AgentSetting
                {
                    AgentId = db.AgentId,
                    Key = setting.Key,
                    Value = setting.Value,
                    Type = setting.Type,
                    Description = setting.Description,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Overwrites all settings with their original default values.
    /// </summary>
    public static async Task ResetAsync(SpaceTradersDbContext db, CancellationToken cancellationToken = default)
    {
        foreach (var defaultSetting in Defaults)
        {
            var existing = await db.Settings
                .FirstOrDefaultAsync(s => s.Key == defaultSetting.Key, cancellationToken);

            if (existing is null)
            {
                db.Settings.Add(new AgentSetting
                {
                    AgentId = db.AgentId,
                    Key = defaultSetting.Key,
                    Value = defaultSetting.Value,
                    Type = defaultSetting.Type,
                    Description = defaultSetting.Description,
                });
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(new AgentSetting
                {
                    AgentId = db.AgentId,
                    Key = existing.Key,
                    Value = defaultSetting.Value,
                    Type = existing.Type,
                    Description = existing.Description,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
