using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.Infrastructure.Persistence;

/// <summary>
/// Creates the schema from the model. There are no migrations: a database from before the agent
/// id (slice 1.4) doesn't fit the model and has to be dropped first. The cluster's database starts
/// empty. A column or table added to the model after the cluster's tables exist is added by
/// <see cref="AddedSchema"/>.
/// </summary>
public static class SpaceTradersDatabaseInitializer
{
    /// <summary>
    /// Columns and tables the model gained after the cluster's tables were created, added where
    /// they are missing. Each statement must be safe to run on every start.
    /// </summary>
    internal static readonly IReadOnlyList<string> AddedSchema =
    [
        // Slice 6.4: how often each survey was used, for the survey dashboard.
        """ALTER TABLE cached_surveys ADD COLUMN IF NOT EXISTS "Extractions" integer NOT NULL DEFAULT 0""",

        // D69: whether a setting follows its default. A setting stored before follows it, unless
        // KeepSettingsSetBeforeD69Async finds it was set.
        """ALTER TABLE agent_settings ADD COLUMN IF NOT EXISTS "FollowsDefault" boolean NOT NULL DEFAULT TRUE""",

        // D69: the values chosen for the next runs.
        """CREATE TABLE IF NOT EXISTS next_run_settings ("Key" character varying(200) NOT NULL, "Value" text NOT NULL, CONSTRAINT "PK_next_run_settings" PRIMARY KEY ("Key"))""",

        // Slice 2.15: why a snapshot was taken, and for a discovery what was new. Every snapshot before was a startup's.
        """ALTER TABLE startup_snapshots ADD COLUMN IF NOT EXISTS "Reason" character varying(20) NOT NULL DEFAULT 'Startup'""",
        """ALTER TABLE startup_snapshots ADD COLUMN IF NOT EXISTS "Discovered" text""",
    ];

    public static Task InitializeAsync(
        SpaceTradersDbContext dbContext,
        CancellationToken cancellationToken = default)
        => CreateTablesIfMissingAsync(dbContext, cancellationToken);

    /// <summary>
    /// Creates the database and the model's tables when they don't exist yet. <c>EnsureCreated</c>
    /// isn't enough: it does nothing as soon as the database holds any table, even one the app
    /// doesn't own (B21).
    /// </summary>
    private static async Task CreateTablesIfMissingAsync(SpaceTradersDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        var creator = dbContext.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken))
        {
            await creator.CreateAsync(cancellationToken);
        }

        var modelTables = dbContext.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .OfType<string>()
            .Distinct()
            .ToArray();

        var existingModelTables = await dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = ANY({modelTables})")
            .SingleAsync(cancellationToken);

        if (existingModelTables == 0)
        {
            await creator.CreateTablesAsync(cancellationToken);
        }

        // Read before AddedSchema adds the column, so only the settings stored before D69 are judged, and in one
        // transaction with it: if judging them fails, the column isn't added either, and the next start tries both.
        var settingsFollowDefaults = await ColumnExistsAsync(dbContext, "agent_settings", nameof(AgentSetting.FollowsDefault), cancellationToken);
        await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            foreach (var statement in AddedSchema)
            {
                await dbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
            }

            if (!settingsFollowDefaults)
            {
                await KeepSettingsSetBeforeD69Async(dbContext, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        });
    }

    /// <summary>
    /// D69, once, when <c>agent_settings</c> gains <c>"FollowsDefault"</c>, which is true for every setting stored before:
    /// a setting whose value isn't its default was set, by you or by the bot (the kill switch, the size guard), and
    /// keeps its value. A plan switch that is off follows, as D69 switched those defaults on (D9 had most of them off).
    /// </summary>
    private static async Task KeepSettingsSetBeforeD69Async(SpaceTradersDbContext dbContext, CancellationToken cancellationToken)
    {
        var stored = await dbContext.Settings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(setting => new { setting.AgentId, setting.Key, setting.Value })
            .ToListAsync(cancellationToken);

        foreach (var setting in stored.Where(setting => WasSet(setting.Key, setting.Value)))
        {
            await dbContext.Settings
                .IgnoreQueryFilters()
                .Where(s => s.AgentId == setting.AgentId && s.Key == setting.Key)
                .ExecuteUpdateAsync(update => update.SetProperty(s => s.FollowsDefault, false), cancellationToken);
        }

        static bool WasSet(string key, string value)
            => DefaultSettingsSeed.DefaultOf(key) is { } defaultValue
                && !string.Equals(value, defaultValue, StringComparison.Ordinal)
                && !(key.StartsWith("Automation.Plan.", StringComparison.Ordinal) && string.Equals(value, "false", StringComparison.Ordinal));
    }

    private static Task<bool> ColumnExistsAsync(SpaceTradersDbContext dbContext, string table, string column, CancellationToken cancellationToken)
        => dbContext.Database
            .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = {table} AND column_name = {column}) AS \"Value\"")
            .SingleAsync(cancellationToken);
}
