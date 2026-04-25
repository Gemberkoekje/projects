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
        new AgentSetting { Key = "Automation.Enabled",                    Value = "true",                Type = "bool",    Description = "Master kill-switch for automation" },
        new AgentSetting { Key = "ActivityLog.RetentionDays",             Value = "30",                  Type = "int",     Description = "Days to retain activity log entries" },
    ];

    public static async Task SeedAsync(SpaceTradersDbContext db, CancellationToken cancellationToken = default)
    {
        var existingKeys = await db.Settings
            .AsNoTracking()
            .Select(s => s.Key)
            .ToHashSetAsync(cancellationToken);

        foreach (var setting in Defaults)
        {
            if (!existingKeys.Contains(setting.Key))
                db.Settings.Add(setting);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
