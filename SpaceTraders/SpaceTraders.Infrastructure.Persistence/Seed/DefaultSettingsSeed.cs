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
        new AgentSetting { Key = "Automation.Trade.MaxLossPerUnitBeforeReroute", Value = "50",            Type = "int",     Description = "Max accepted per-unit loss before trying an alternate sell market" },
        new AgentSetting { Key = "Automation.Enabled",                    Value = "true",                Type = "bool",    Description = "Master kill-switch for automation" },
        new AgentSetting { Key = "ActivityLog.RetentionDays",             Value = "30",                  Type = "int",     Description = "Days to retain activity log entries" },
        new AgentSetting { Key = "Alerts.WebhookUrl",                     Value = "",                    Type = "string",  Description = "Slack/webhook URL for operator alerts (empty = disabled)" },
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
