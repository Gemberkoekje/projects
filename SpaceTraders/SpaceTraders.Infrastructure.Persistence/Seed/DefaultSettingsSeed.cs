using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Persistence.Seed;

/// <summary>
/// The settings every agent starts with. Each one is read by code that runs (B18, D10); the
/// <c>Runtime.*</c> keys are status flags. A feature that needs a new setting adds it here.
/// Every plan is on by default (D69). A run starts with the values chosen for the next runs where
/// there are any, else with these defaults; a setting nobody has set since follows its default.
/// </summary>
public static class DefaultSettingsSeed
{
    private const string StatusFlagPrefix = "Runtime.";

    private static readonly IReadOnlyList<AgentSetting> Defaults =
    [
        new AgentSetting { Key = "FleetExpansion.MinCreditReserve",       Value = "60000",               Type = "long",    Description = "Credits every ship purchase leaves with no ship that trades: the floor of the credit reserve, which grows by FleetExpansion.ReservePerTradingCargoUnit for every unit the trading ships can carry (D51)" },
        new AgentSetting { Key = "FleetExpansion.ReservePerTradingCargoUnit", Value = "1000",            Type = "long",    Description = "Credits the credit reserve grows by for every unit of hold on the ships that trade: cargo ships, the command ship and ships the role board has trading, so they can still buy their loads after a purchase (D51; 0 = the floor only)" },
        new AgentSetting { Key = "FleetExpansion.PreferredShipType",      Value = "SHIP_MINING_DRONE",   Type = "string",  Description = "Default ship type to buy" },
        new AgentSetting { Key = "Trade.MinProfitPerUnit",                Value = "200",                 Type = "int",     Description = "Credits per unit, after fuel, a trade trip must earn to be started, and to be carried on when prices change (0 = any profit)" },
        new AgentSetting { Key = "Trade.FuelReserveCredits",              Value = "5000",                Type = "long",    Description = "Credits a cargo purchase must leave, so ships can always buy fuel: below them only fuel is bought (D24)" },
        new AgentSetting { Key = "Trade.ShipPurchases",                   Value = "SHIP_LIGHT_SHUTTLE,SHIP_LIGHT_HAULER,SHIP_LIGHT_HAULER", Type = "string", Description = "Cargo ships the trading plan buys, in order: the Nth while the fleet has fewer than N cargo ships, above the credit reserve (D21; empty = none)" },
        new AgentSetting { Key = "Trade.ShipPurchaseMinRouteProfit",      Value = "10000",               Type = "long",    Description = "Credits a route must earn, from the shipyard, to count as work for a cargo ship beyond Trade.ShipPurchases: one is bought only once such a route, held by no trader, has waited Trade.ShipPurchaseWaitMinutes with every trader busy (D88)" },
        new AgentSetting { Key = "Trade.ShipPurchaseWaitMinutes",         Value = "30",                  Type = "int",     Description = "Minutes a route worth Trade.ShipPurchaseMinRouteProfit must have waited, every trader busy, before the trading plan buys a cargo ship beyond Trade.ShipPurchases (D88)" },
        new AgentSetting { Key = "Trade.MaxHaulDistance",                 Value = "5",                   Type = "int",     Description = "The trade reach, in jumps: a trade route buys within that many jumps of the trader's system and sells within that many of the buy market's, through built gates (D96); the explored systems within it of home get their probes before the drones and cargo ships that take turns, the others after them (slice 6.28, D97)" },
        new AgentSetting { Key = "Trade.MaxPriceAgeMinutes",              Value = "30",                  Type = "int",     Description = "Minutes a market's prices may be old for a trade route to buy or sell there, at home too (D96)" },
        new AgentSetting { Key = "Automation.Enabled",                    Value = "true",                Type = "bool",    Description = "Master kill-switch for automation" },
        new AgentSetting { Key = "Automation.Plan.Scout.Enabled",           Value = "true",                Type = "bool",    Description = "Run the scout plan: visit every marketplace in the starting system once" },
        new AgentSetting { Key = "Automation.Plan.Explore.Enabled",         Value = "true",                Type = "bool",    Description = "Run the explore plan: once its trip ends, the command ship jumps through active jump gates to every system not explored yet, scouts its markets and shipyards, and comes home; a jump keeps FleetExpansion.MinCreditReserve (asked on 2026-10-04)" },
        new AgentSetting { Key = "Automation.Plan.Roles.Enabled",           Value = "true",                Type = "bool",    Description = "Run the role board: every ship takes the role that earns the fleet most per hour, by what it and the others can do; surveys first, by the ship with the least to lose, and the contract before the rest (D38-D41)" },
        new AgentSetting { Key = "Automation.Plan.Contract.Enabled",        Value = "true",                Type = "bool",    Description = "Run the contract plan: take one mineral contract and fulfil it (may buy a mining drone)" },
        new AgentSetting { Key = "Automation.Plan.ProbeDeployment.Enabled", Value = "true",                Type = "bool",    Description = "Run the probe plan: a probe for every market, the HQ system's first, then those of each explored system the gates reach, the nearest first; roaming between the stalest nearby markets until there are enough (buys SHIP_PROBE above the credit reserve, never at SCARCE, D29, D97)" },
        new AgentSetting { Key = "Automation.Plan.Survey.Enabled",          Value = "true",                Type = "bool",    Description = "Run the survey plan: ships that can survey survey, and only that, the contract's ore first, then ores the markets buy (D20)" },
        new AgentSetting { Key = "Automation.Plan.Mining.Enabled",          Value = "true",                Type = "bool",    Description = "Run the mining plan: miners mine surveyed ores, else ores in low supply, and sell them (buys mining drones)" },
        new AgentSetting { Key = "Automation.Plan.Siphon.Enabled",          Value = "true",                Type = "bool",    Description = "Run the siphon plan: siphon drones siphon gases in low supply at gas giants, keep every gas, and sell them (buys siphon drones, Siphon.MaxDrones)" },
        new AgentSetting { Key = "Automation.Plan.Construction.Enabled",    Value = "true",                Type = "bool",    Description = "Run the construction plan: the ship with the construction role buys the jump gate's materials, a full hold at a time where they aren't SCARCE or LIMITED, after the cargo ships and above the credit reserve, and supplies the gate; it trades while there is nothing it may buy (D64-D67)" },
        new AgentSetting { Key = "Automation.Plan.Trading.Enabled",         Value = "true",                Type = "bool",    Description = "Run the trading plan: idle ships with a cargo hold carry goods between markets for profit (buys cargo ships, Trade.ShipPurchases)" },
        new AgentSetting { Key = "Automation.Plan.SpareTime.Enabled",       Value = "true",                Type = "bool",    Description = "Run the spare-time plan: a surveyor with nothing to survey or trade mines or siphons whatever sells at the nearest place it can, and sells it; a survey or a trade interrupts it (D34-D37)" },
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
        new AgentSetting { Key = "Mining.GateMinerIntervalMinutes",              Value = "30",                  Type = "int",     Description = "Minutes between the mining drones bought for one ore of the jump gate's smelters: while the gate needs materials and a smelter that makes a metal for them from the ore has it below HIGH, one per ore, within Mining.MaxDrones, after a load of the gate that can be bought now; each mines only that ore for the smelters until the gate needs nothing made from it (D92)" },
        new AgentSetting { Key = "Siphon.MaxDrones",                             Value = "10",                  Type = "int",     Description = "Maximum number of siphon drones the siphon plan keeps; it buys one only for a market short of a gas (D32)" },
        new AgentSetting { Key = "Construction.Ships",                           Value = "0",                   Type = "int",     Description = "The most ships that build the jump gate while it needs materials (0 = every ship that can): the largest holds that aren't drones or the surveyor; the role board gives them the construction role, or with the board off the construction plan picks them. Each takes a load only of what no other trip carries (D65, D93)" },
        new AgentSetting { Key = "Roles.ReconsiderMinutes",                      Value = "10",                  Type = "int",     Description = "Minutes between the role board's evaluations of the whole fleet; a new ship, a plan switched, or a ship left without work weighs the roles at once (D41)" },
        new AgentSetting { Key = "Roles.HeadStartPercent",                       Value = "20",                  Type = "int",     Description = "Percent more a ship's current role counts on the role board, so close calls don't flip back and forth (D41; 0 = none)" },
        new AgentSetting { Key = "Roles.ChainValueSharePercent",                 Value = "50",                  Type = "int",     Description = "Percent of the price difference to the pricier good a market makes from what a ship sells it that the role board counts, and that share again for the step after; fully while the market is SCARCE of it, not at ABUNDANT; at most what the trip earns on a unit (D39, D49; 0 = none)" },
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

    private static readonly Dictionary<string, string> Descriptions = Defaults.ToDictionary(setting => setting.Key, setting => setting.Description, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> DefaultValues = Defaults.ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal);

    /// <summary>
    /// The settings a run can be given a value of its own for, each with its default, type and description, in the
    /// seed's order: every seeded setting but the <c>Runtime.*</c> status flags (D69).
    /// </summary>
    public static IReadOnlyList<(string Key, string Value, string Type, string Description)> RunSettings { get; } =
        [.. Defaults.Where(setting => !IsStatusFlag(setting.Key)).Select(setting => (setting.Key, setting.Value, setting.Type, setting.Description))];

    /// <summary>
    /// What a seeded setting does, as this version describes it; null for a key the seed doesn't hold. A stored setting
    /// keeps the description it was seeded with, which an older version may have written (slice 2.9).
    /// </summary>
    /// <param name="key">The setting's key.</param>
    /// <returns>The description, or null.</returns>
    public static string? DescriptionOf(string key)
        => Descriptions.GetValueOrDefault(key);

    /// <summary>
    /// The value the next run starts with when none is chosen for it: the setting's default, as this version has it;
    /// null for a key that isn't a setting a run starts with (D69).
    /// </summary>
    /// <param name="key">The setting's key.</param>
    /// <returns>The default, or null.</returns>
    public static string? DefaultOf(string key)
        => IsRunSetting(key) ? DefaultValues[key] : null;

    /// <summary>
    /// Whether the runs to come can be given a value of their own for the setting: the seed holds it, and it isn't a
    /// <c>Runtime.*</c> status flag (D69).
    /// </summary>
    /// <param name="key">The setting's key.</param>
    /// <returns>True for a setting a run starts with.</returns>
    public static bool IsRunSetting(string key)
        => Descriptions.ContainsKey(key) && !IsStatusFlag(key);

    /// <summary>
    /// Seeds the settings the agent doesn't have yet: each with the value chosen for the next runs, else with its
    /// default. A setting that still follows its default gets the default as this version has it, so a default that
    /// changes reaches the agent that runs (D69); the status flags and the settings someone set keep their values.
    /// </summary>
    public static Task SeedAsync(SpaceTradersDbContext db, CancellationToken cancellationToken = default)
        => SeedAsync(db, NullLogger.Instance, cancellationToken);

    /// <inheritdoc cref="SeedAsync(SpaceTradersDbContext, CancellationToken)"/>
    /// <remarks>Each setting it changes is a <c>SettingChanged</c> journal line on <paramref name="logger"/>.</remarks>
    public static async Task SeedAsync(SpaceTradersDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var chosen = await db.NextRunSettings
            .AsNoTracking()
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal, cancellationToken);
        var stored = await db.Settings
            .ToDictionaryAsync(setting => setting.Key, StringComparer.Ordinal, cancellationToken);
        var changed = new List<(string Key, string OldValue, string NewValue)>();

        foreach (var setting in Defaults)
        {
            if (!stored.TryGetValue(setting.Key, out var existing))
            {
                var value = IsStatusFlag(setting.Key) ? null : chosen.GetValueOrDefault(setting.Key);
                db.Settings.Add(new AgentSetting
                {
                    AgentId = db.AgentId,
                    Key = setting.Key,
                    Value = value ?? setting.Value,
                    Type = setting.Type,
                    Description = setting.Description,
                    FollowsDefault = value is null,
                });
            }
            else if (existing.FollowsDefault
                && !IsStatusFlag(setting.Key)
                && !string.Equals(existing.Value, setting.Value, StringComparison.Ordinal))
            {
                changed.Add((setting.Key, existing.Value, setting.Value));
                db.Entry(existing).CurrentValues.SetValues(new AgentSetting
                {
                    AgentId = existing.AgentId,
                    Key = existing.Key,
                    Value = setting.Value,
                    Type = existing.Type,
                    Description = existing.Description,
                    FollowsDefault = true,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var (key, oldValue, newValue) in changed)
        {
            SettingsRepository.LogIfChanged(logger, key, oldValue, newValue);
        }
    }

    /// <summary>
    /// Gives every setting its default back, to follow it from then on, and forgets the values chosen for the next
    /// runs (D69).
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
                    FollowsDefault = true,
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
                    FollowsDefault = true,
                });
            }
        }

        db.NextRunSettings.RemoveRange(await db.NextRunSettings.ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsStatusFlag(string key)
        => key.StartsWith(StatusFlagPrefix, StringComparison.Ordinal);
}
