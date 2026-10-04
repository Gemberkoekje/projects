using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Infrastructure.Persistence;

/// <summary>
/// How long each table keeps its rows. Every table is listed: with a policy that prunes it, or
/// with the reason it can't grow without bound. <c>DataRetentionTests</c> fails for a table that
/// isn't listed, so a new table can't slip through. Pruning covers every agent's rows, so it
/// doesn't wait for agent bootstrap. The bounded tables hold the active agent's state; earlier
/// agents' rows are deleted at startup (<see cref="AgentDataCleanup"/>).
/// </summary>
public sealed class DataRetention(SpaceTradersDbContext db) : IDataRetention
{
    public const string ActivityLogRetentionSetting = "ActivityLog.RetentionDays";
    public const int DefaultActivityLogDays = 30;
    public const int RawSampleDays = 7;
    public const int HourlySampleDays = 90;
    public const int LedgerEntryDays = 30;
    public const int ShipTaskRecordDays = 30;
    public const int StartupSnapshotsKept = 10;
    public const int RunDays = 365;
    public const int ShipGoalHistoryDays = 30;
    public const int CompletedFleetGoalDays = 30;

    private static readonly IReadOnlyList<TablePolicy> Tables =
    [
        // First: a pod that keeps failing during startup adds a snapshot at every start.
        Pruned("startup_snapshots", $"the agent's first snapshot and the last {StartupSnapshotsKept}", (db, _, ct) => PruneStartupSnapshotsAsync(db, ct)),
        Pruned("activity_logs", $"{ActivityLogRetentionSetting} days (default {DefaultActivityLogDays})", PruneActivityLogsAsync),
        Pruned("ledger_entries", $"{LedgerEntryDays} days", (db, now, ct) => db.LedgerEntries.IgnoreQueryFilters().Where(e => e.OccurredAt < now.AddDays(-LedgerEntryDays)).ExecuteDeleteAsync(ct)),
        Pruned("ship_task_records", $"{ShipTaskRecordDays} days", (db, now, ct) => db.ShipTaskRecords.IgnoreQueryFilters().Where(r => r.StartedAt < now.AddDays(-ShipTaskRecordDays)).ExecuteDeleteAsync(ct)),
        Pruned("ship_goal_history", $"{ShipGoalHistoryDays} days", (db, now, ct) => db.ShipGoalHistory.IgnoreQueryFilters().Where(h => h.EndedAt < now.AddDays(-ShipGoalHistoryDays)).ExecuteDeleteAsync(ct)),
        Pruned("fleet_goals", $"completed goals for {CompletedFleetGoalDays} days", (db, now, ct) => db.FleetGoals.IgnoreQueryFilters().Where(g => g.CompletedAt < now.AddDays(-CompletedFleetGoalDays)).ExecuteDeleteAsync(ct)),
        Pruned("runs", $"{RunDays} days, for every agent", (db, now, ct) => db.Runs.IgnoreQueryFilters().Where(r => r.StartedAt < now.AddDays(-RunDays)).ExecuteDeleteAsync(ct)),
        Pruned("run_credit_highlights", $"{RunDays} days", (db, now, ct) => db.RunCreditHighlights.IgnoreQueryFilters().Where(h => h.OccurredAt < now.AddDays(-RunDays)).ExecuteDeleteAsync(ct)),
        Pruned("agent_credits_samples", $"every sample for {RawSampleDays} days, then the first per hour up to {HourlySampleDays} days", PruneAgentCreditsSamplesAsync),
        Pruned("market_price_samples", $"every sample for {RawSampleDays} days, then the first per waypoint, good and hour up to {HourlySampleDays} days", PruneMarketPriceSamplesAsync),

        Bounded("stored_credentials", "the active agent's token"),
        Bounded("cached_agents", "one row for the active agent"),
        Bounded("cached_ships", "one row per ship"),
        Bounded("cached_contracts", "the active agent's contracts"),
        Bounded("cached_markets", "one row per market"),
        Bounded("cached_shipyards", "one row per shipyard"),
        Bounded("cached_waypoints", "the waypoints of the systems the fleet has been in"),
        Bounded("cached_systems", "the systems the fleet has been in"),
        Bounded("cached_construction_sites", "one row per construction site"),
        Bounded("cached_surveys", "expired surveys are deleted whenever new ones are saved"),
        Bounded("agent_settings", "one row per setting"),
        Bounded("next_run_settings", "at most one row per seeded setting, whatever the agent"),
        Bounded("ship_assignment_records", "one row per ship"),
        Bounded("plan_states", "one row per plan"),
        Bounded("leader_leases", "one row per lease"),
        Bounded("api_endpoint_usages", "one counter per endpoint"),
        Bounded("trade_opportunities", "replaced as a whole at every computation"),
        Bounded("scheduled_runs", "deleted when promoted"),
        Bounded("scheduled_ship_events", "one per ship and goal, deleted when it fires"),
    ];

    private static readonly IReadOnlyList<string> PrunedTableNames = Tables.Where(t => t.PruneAsync is not null).Select(t => t.Table).ToList();

    /// <summary>Every table, and how long it keeps its rows.</summary>
    public static IReadOnlyDictionary<string, string> Policies { get; } = Tables.ToDictionary(t => t.Table, t => t.Policy);

    public IReadOnlyList<string> PrunedTables => PrunedTableNames;

    public Task<int> PruneAsync(string table, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var prune = Tables.SingleOrDefault(t => t.Table == table)?.PruneAsync
            ?? throw new ArgumentException($"Table {table} has nothing to prune.", nameof(table));

        return prune(db, now, cancellationToken);
    }

    private static TablePolicy Pruned(string table, string policy, Func<SpaceTradersDbContext, DateTimeOffset, CancellationToken, Task<int>> prune)
        => new(table, policy, prune);

    private static TablePolicy Bounded(string table, string why) => new(table, $"bounded: {why}", null);

    private static Task<int> PruneStartupSnapshotsAsync(SpaceTradersDbContext db, CancellationToken cancellationToken)
    {
        var latest = db.StartupSnapshots
            .OrderByDescending(s => s.CapturedAt)
            .ThenByDescending(s => s.Id)
            .Take(StartupSnapshotsKept)
            .Select(s => s.Id);

        return db.StartupSnapshots
            .Where(s => !s.IsInitialSnapshot && !latest.Contains(s.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<int> PruneActivityLogsAsync(SpaceTradersDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The setting is per agent, and pruning doesn't wait for agent bootstrap: take the longest
        // retention any stored agent asks for.
        var values = await db.Settings
            .IgnoreQueryFilters()
            .Where(s => s.Key == ActivityLogRetentionSetting)
            .Select(s => s.Value)
            .ToListAsync(cancellationToken);
        var days = values.Select(value => int.TryParse(value, out var parsed) ? parsed : 0).DefaultIfEmpty(0).Max();
        if (days <= 0)
        {
            days = DefaultActivityLogDays;
        }

        return await db.ActivityLogs
            .IgnoreQueryFilters()
            .Where(l => l.Timestamp < now.AddDays(-days))
            .ExecuteDeleteAsync(cancellationToken);
    }

    // The first sample of each hour stays. Ranking the window's rows in one pass replaces a NOT IN
    // over the first id of every hour: once that list outgrows work_mem, Postgres can't hash it and
    // compares every row with the whole list, which doesn't fit the 30 s command timeout.
    private static async Task<int> PruneAgentCreditsSamplesAsync(SpaceTradersDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rawCutoff = now.AddDays(-RawSampleDays);
        var hourlyCutoff = now.AddDays(-HourlySampleDays);
        var downsampled = await db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM agent_credits_samples
            WHERE "Id" IN (
                SELECT "Id"
                FROM (
                    SELECT "Id", row_number() OVER (
                        PARTITION BY "AgentId", date_trunc('hour', "ObservedAt")
                        ORDER BY "Id") AS "Rank"
                    FROM agent_credits_samples
                    WHERE "ObservedAt" >= {hourlyCutoff} AND "ObservedAt" < {rawCutoff}) AS ranked
                WHERE "Rank" > 1)
            """,
            cancellationToken);
        var expired = await db.AgentCreditsSamples
            .Where(s => s.ObservedAt < hourlyCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return downsampled + expired;
    }

    private static async Task<int> PruneMarketPriceSamplesAsync(SpaceTradersDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rawCutoff = now.AddDays(-RawSampleDays);
        var hourlyCutoff = now.AddDays(-HourlySampleDays);
        var downsampled = await db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM market_price_samples
            WHERE "Id" IN (
                SELECT "Id"
                FROM (
                    SELECT "Id", row_number() OVER (
                        PARTITION BY "AgentId", "WaypointSymbol", "GoodSymbol", date_trunc('hour', "ObservedAt")
                        ORDER BY "Id") AS "Rank"
                    FROM market_price_samples
                    WHERE "ObservedAt" >= {hourlyCutoff} AND "ObservedAt" < {rawCutoff}) AS ranked
                WHERE "Rank" > 1)
            """,
            cancellationToken);
        var expired = await db.MarketPriceSamples
            .IgnoreQueryFilters()
            .Where(s => s.ObservedAt < hourlyCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return downsampled + expired;
    }

    private sealed record TablePolicy(
        string Table,
        string Policy,
        Func<SpaceTradersDbContext, DateTimeOffset, CancellationToken, Task<int>>? PruneAsync);
}
