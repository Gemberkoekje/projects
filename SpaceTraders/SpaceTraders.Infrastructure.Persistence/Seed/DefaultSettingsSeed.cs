using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Seed;

public static class DefaultSettingsSeed
{
    private static readonly IReadOnlyList<AgentSetting> Defaults =
    [
        new AgentSetting { Key = "FleetExpansion.MinCreditReserve",       Value = "100000",              Type = "long",    Description = "Credits to always keep in bank" },
        new AgentSetting { Key = "FleetExpansion.MinCreditRatioForShip",  Value = "0.5",                 Type = "decimal", Description = "Fraction of ship price that must remain after purchase" },
        new AgentSetting { Key = "FleetExpansion.MaxShips",               Value = "20",                  Type = "int",     Description = "Hard cap on fleet size" },
        new AgentSetting { Key = "FleetExpansion.PreferredShipType",      Value = "SHIP_MINING_DRONE",   Type = "string",  Description = "Default ship type to buy" },
        new AgentSetting { Key = "Trade.MinProfitPerUnit",                Value = "200",                 Type = "int",     Description = "Skip trade routes below this margin" },
        new AgentSetting { Key = "Trade.MaxHaulDistance",                 Value = "5",                   Type = "int",     Description = "Max jumps between buy/sell waypoints" },
        new AgentSetting { Key = "Contract.AutoAccept",                   Value = "true",                Type = "bool",    Description = "Auto-accept contracts when profitable" },
        new AgentSetting { Key = "Scout.MarketRefreshIntervalMinutes",    Value = "10",                  Type = "int",     Description = "How often to re-poll markets with a ship present" },
        new AgentSetting { Key = "Scout.ShipyardRefreshIntervalMinutes",  Value = "30",                  Type = "int",     Description = "How often to re-poll shipyards with a ship present" },
        new AgentSetting { Key = "Navigation.CriticalFuelRatio",          Value = "0.15",                Type = "decimal", Description = "Fuel ratio at or below which ships force DRIFT mode" },
        new AgentSetting { Key = "Navigation.LowFuelRatioForDrift",       Value = "0.35",                Type = "decimal", Description = "Fuel ratio below which ships prefer DRIFT mode" },
        new AgentSetting { Key = "Navigation.BurnFuelRatioMinimum",       Value = "0.50",                Type = "decimal", Description = "Minimum fuel ratio before selecting BURN mode" },
        new AgentSetting { Key = "Navigation.BurnDistanceThreshold",      Value = "35",                  Type = "decimal", Description = "Distance threshold for selecting BURN mode" },
        new AgentSetting { Key = "Automation.Trade.MaxLossPerUnitBeforeReroute", Value = "50",            Type = "int",     Description = "Max accepted per-unit loss before trying an alternate sell market" },
        new AgentSetting { Key = "Automation.Enabled",                    Value = "true",                Type = "bool",    Description = "Master kill-switch for automation" },
        new AgentSetting { Key = "ActivityLog.RetentionDays",             Value = "30",                  Type = "int",     Description = "Days to retain activity log entries" },
        new AgentSetting { Key = "Alerts.WebhookUrl",                     Value = "",                    Type = "string",  Description = "Slack/webhook URL for operator alerts (empty = disabled)" },
        new AgentSetting { Key = "Automation.MiningShipPercentage",              Value = "0.25",                Type = "decimal", Description = "Fraction of mining-capable ships assigned to resource extraction roles" },
        new AgentSetting { Key = "Mining.SurveyMinimumCooldownSeconds",          Value = "0",                   Type = "int",     Description = "Only refresh surveys when cooldown remaining is above this threshold or none are cached" },
        new AgentSetting { Key = "Mining.JettisonLowValueWhenFull",              Value = "true",                Type = "bool",    Description = "Jettison low-value surplus cargo when a mining ship is full and cannot sell it efficiently" },
        new AgentSetting { Key = "Mining.MinimumSellPriceToKeepCargo",           Value = "25",                  Type = "int",     Description = "Minimum observed sell price before non-contract cargo is kept for sale instead of jettison" },
        new AgentSetting { Key = "Mining.ReserveHydrocarbonUnits",               Value = "10",                  Type = "int",     Description = "Hydrocarbon units to reserve for cargo-based refueling logistics" },
        new AgentSetting { Key = "Maintenance.RepairConditionThreshold",        Value = "0.70",                Type = "decimal", Description = "Repair ships when average component condition drops below this threshold" },
        new AgentSetting { Key = "Maintenance.MinIntegrityForLongRoutes",        Value = "0.55",                Type = "decimal", Description = "Avoid assigning long routes to ships with lower average integrity" },
        new AgentSetting { Key = "Maintenance.ScrapIntegrityThreshold",          Value = "0.20",                Type = "decimal", Description = "Scrap ships when average integrity falls at or below this threshold" },
        new AgentSetting { Key = "Maintenance.MinScrapValue",                    Value = "5000",                Type = "long",    Description = "Minimum scrap quote required before scrapping an eligible ship" },
        new AgentSetting { Key = "Maintenance.LongRouteJumpThreshold",           Value = "6",                   Type = "int",     Description = "Route jump count treated as long-haul for integrity gating" },
        new AgentSetting { Key = "Outfitting.PreferredMiningMount",              Value = "MOUNT_MINING_LASER_I", Type = "string",  Description = "Preferred mining mount for mining roles" },
        new AgentSetting { Key = "Outfitting.PreferredMineralProcessor",         Value = "MODULE_MINERAL_PROCESSOR_I", Type = "string", Description = "Preferred mineral processor module for mining roles" },
        new AgentSetting { Key = "Outfitting.PreferredSiphonMount",              Value = "MOUNT_GAS_SIPHON_I",  Type = "string",  Description = "Preferred gas siphon mount for siphon roles" },
        new AgentSetting { Key = "Outfitting.PreferredGasProcessor",             Value = "MODULE_GAS_PROCESSOR_I", Type = "string", Description = "Preferred gas processor module for siphon roles" },
        new AgentSetting { Key = "Outfitting.PreferredScoutSensorMount",         Value = "MOUNT_SENSOR_ARRAY_I", Type = "string",  Description = "Preferred sensor mount for scout/probe roles" },
        new AgentSetting { Key = "Outfitting.TraderCargoModule",                 Value = "MODULE_CARGO_HOLD_I", Type = "string",  Description = "Preferred cargo module for trader roles" },
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
                    AgentToken = db.AgentToken,
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
                    AgentToken = db.AgentToken,
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
                    AgentToken = db.AgentToken,
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
