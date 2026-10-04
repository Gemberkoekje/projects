using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using SpaceTraders.Infrastructure.Persistence.Seed;
using Testcontainers.PostgreSql;

namespace SpaceTraders.Infrastructure.Tests;

/// <summary>
/// B21: the startup chain initialises the schema of a database that may be empty, or may already
/// hold tables the app doesn't own (Wolverine's, until slice 1.3).
/// </summary>
[Trait("Category", "Integration")]
public sealed class DatabaseInitializerTests : IAsyncLifetime
{
    private PostgreSqlContainer _pg = default!;
    private bool _started;

    public async Task InitializeAsync()
    {
        // Testcontainers finds Docker the way it will start the container: DOCKER_HOST, the Unix socket, or
        // Docker Desktop's named pipe on Windows (B36).
        Skip.IfNot(TestcontainersSettings.OS.DockerEndpointAuthConfig is not null, "Docker is not available – skipping integration tests.");

        _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _pg.StartAsync();
        _started = true;
    }

    public async Task DisposeAsync()
    {
        if (_started)
        {
            await _pg.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task InitializeAsync_OnAnEmptyDatabase_CreatesEveryTable()
    {
        await using var db = CreateContext();

        await SpaceTradersDatabaseInitializer.InitializeAsync(db);

        (await MissingTablesAsync(db)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task InitializeAsync_CreatesEveryTable_WhenTheDatabaseAlreadyHoldsAnotherTable()
    {
        await ExecuteAsync("CREATE SCHEMA other; CREATE TABLE other.unrelated (id integer PRIMARY KEY);");
        await using var db = CreateContext();

        await SpaceTradersDatabaseInitializer.InitializeAsync(db);

        (await MissingTablesAsync(db)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task InitializeAsync_CanRunAgainOnItsOwnSchema()
    {
        await using (var first = CreateContext())
        {
            await SpaceTradersDatabaseInitializer.InitializeAsync(first);
        }

        await using var second = CreateContext();
        await SpaceTradersDatabaseInitializer.InitializeAsync(second);

        (await MissingTablesAsync(second)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task InitializeAsync_LeavesRoomForInPlaceUpdates_InTheShipTable()
    {
        // B32, seen in the soak test (1.14): cached_ships holds a few wide rows (about 2 kB of ship
        // JSON each) that change every minute or so. Postgres pruned their old versions in place,
        // which kept the dead-tuple count under the autovacuum trigger, so VACUUM never ran, and every
        // update that didn't fit its page extended the table: about 250 kB an hour with one busy
        // ship, without end. The growth itself needs concurrent snapshots and autovacuum's timing to
        // show; this checks the table is created with the remedy.
        await using var db = CreateContext();

        await SpaceTradersDatabaseInitializer.InitializeAsync(db);

        (await TableOptionsAsync("cached_ships")).Should().BeEquivalentTo("fillfactor=50", "autovacuum_vacuum_threshold=10");
    }

    [SkippableFact]
    public async Task InitializeAsync_AddsTheSurveysUseCount_ToATableFromBeforeIt()
    {
        // Slice 6.4: the cluster's cached_surveys was created without "Extractions", and a table that
        // exists is never created again; without the column, every survey query fails.
        await using (var first = CreateContext())
        {
            await SpaceTradersDatabaseInitializer.InitializeAsync(first);
        }

        await ExecuteAsync("""ALTER TABLE cached_surveys DROP COLUMN "Extractions";""");
        await ExecuteAsync("""
            INSERT INTO cached_surveys ("AgentId", "Signature", "ShipSymbol", "WaypointSymbol", "DepositsJson", "Expiration", "Size", "RecordedAt")
            VALUES ('INITIALIZER-TEST@2026-09-27', 'SIG-1', 'SHIP-1', 'X1-AB-XB5C', '[{"Symbol":"COPPER_ORE"}]', now() + interval '1 hour', 'SMALL', now());
            """);

        await using var db = CreateContext();
        await SpaceTradersDatabaseInitializer.InitializeAsync(db);

        var surveys = new SpaceTraders.Infrastructure.Persistence.Repositories.SurveyRepository(db);
        await surveys.RecordExtractionAsync("SIG-1");
        (await surveys.GetActiveAsync()).Should().ContainSingle().Which.Extractions.Should().Be(1);
    }

    [SkippableFact]
    public async Task InitializeAsync_AddsWhatD69Needs_ToADatabaseFromBeforeIt_AsTheModelWouldCreateIt()
    {
        // D69: the cluster's agent_settings was created without "FollowsDefault", and without next_run_settings; a table
        // that exists is never created again.
        await using (var first = CreateContext())
        {
            await SpaceTradersDatabaseInitializer.InitializeAsync(first);
        }

        var asTheModelCreatesThem = await ColumnsAsync("next_run_settings", "agent_settings");
        var keyAsTheModelCreatesIt = await PrimaryKeyAsync("next_run_settings");
        await ExecuteAsync("""ALTER TABLE agent_settings DROP COLUMN "FollowsDefault"; DROP TABLE next_run_settings;""");

        await using var db = CreateContext();
        await SpaceTradersDatabaseInitializer.InitializeAsync(db);

        (await ColumnsAsync("next_run_settings", "agent_settings")).Should().Equal(asTheModelCreatesThem);
        (await PrimaryKeyAsync("next_run_settings")).Should().Equal(keyAsTheModelCreatesIt);
        db.NextRunSettings.Add(new NextRunSetting { Key = "Mining.MaxDrones", Value = "5" });
        await db.SaveChangesAsync();
        (await db.NextRunSettings.SingleAsync()).Value.Should().Be("5");
    }

    [SkippableFact]
    public async Task InitializeAsync_AddsWhySnapshotsWereTaken_ToATableFromBeforeIt_AsTheModelWouldCreateIt()
    {
        // Slice 2.15: the cluster's startup_snapshots was created without "Reason" and "Discovered", and a table that exists
        // is never created again. Every snapshot it holds was a startup's.
        await using (var first = CreateContext())
        {
            await SpaceTradersDatabaseInitializer.InitializeAsync(first);
        }

        var asTheModelCreatesThem = await ColumnsAsync("startup_snapshots");
        await ExecuteAsync("""ALTER TABLE startup_snapshots DROP COLUMN "Reason", DROP COLUMN "Discovered";""");
        await ExecuteAsync("""
            INSERT INTO startup_snapshots ("AgentId", "SnapshotJson", "CapturedAt", "IsInitialSnapshot")
            VALUES ('INITIALIZER-TEST@2026-09-27', '{}', now(), true);
            """);

        await using var db = CreateContext();
        await SpaceTradersDatabaseInitializer.InitializeAsync(db);

        (await ColumnsAsync("startup_snapshots")).Should().Equal(asTheModelCreatesThem);
        var before = await db.StartupSnapshots.AsNoTracking().SingleAsync();
        before.Reason.Should().Be(StartupSnapshot.StartupReason);
        before.Discovered.Should().BeNull();
    }

    [SkippableFact]
    public async Task InitializeAsync_D69_KeepsWhatWasSetBeforeIt_AndSwitchesOnThePlansThatWereOff()
    {
        // The agent the reset of 2026-10-04 13:00Z registered has most plans off (D9's defaults). Before the deploy you
        // may set a setting, or switch automation off: those keep their values. The plans come on at the deploy's start.
        await using (var first = CreateContext())
        {
            await SpaceTradersDatabaseInitializer.InitializeAsync(first);
        }

        await ExecuteAsync("""ALTER TABLE agent_settings DROP COLUMN "FollowsDefault"; DROP TABLE next_run_settings;""");
        await ExecuteAsync("""
            INSERT INTO agent_settings ("AgentId", "Key", "Value", "Type", "Description") VALUES
                ('INITIALIZER-TEST@2026-09-27', 'Automation.Plan.Trading.Enabled', 'false', 'bool', ''),
                ('INITIALIZER-TEST@2026-09-27', 'Automation.Enabled', 'false', 'string', ''),
                ('INITIALIZER-TEST@2026-09-27', 'Mining.MaxDrones', '5', 'string', ''),
                ('INITIALIZER-TEST@2026-09-27', 'Trade.MinProfitPerUnit', '200', 'int', '');
            """);

        await using var db = CreateContext();
        await SpaceTradersDatabaseInitializer.InitializeAsync(db);
        await DefaultSettingsSeed.SeedAsync(db);

        var settings = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key);
        settings["Automation.Plan.Trading.Enabled"].Should().Match<AgentSetting>(s => s.Value == "true" && s.FollowsDefault);
        settings["Automation.Enabled"].Should().Match<AgentSetting>(s => s.Value == "false" && !s.FollowsDefault);
        settings["Mining.MaxDrones"].Should().Match<AgentSetting>(s => s.Value == "5" && !s.FollowsDefault);
        settings["Trade.MinProfitPerUnit"].Should().Match<AgentSetting>(s => s.Value == "200" && s.FollowsDefault);
    }

    [SkippableFact]
    public async Task InitializeAsync_JudgesTheSettingsOnlyWhenItAddsTheColumn()
    {
        // Later, a setting that follows its default may differ from the running version's default (a new version
        // changed it): it must go on following, so the next seed gives it the new default.
        await using (var first = CreateContext())
        {
            await SpaceTradersDatabaseInitializer.InitializeAsync(first);
        }

        await ExecuteAsync("""
            INSERT INTO agent_settings ("AgentId", "Key", "Value", "Type", "Description", "FollowsDefault")
            VALUES ('INITIALIZER-TEST@2026-09-27', 'Mining.MaxDrones', '15', 'int', '', TRUE);
            """);

        await using var db = CreateContext();
        await SpaceTradersDatabaseInitializer.InitializeAsync(db);

        (await db.Settings.AsNoTracking().SingleAsync()).FollowsDefault.Should().BeTrue();
    }

    /// <summary>Each column of the tables, with its type, length and nullability, in table and column order.</summary>
    private async Task<IReadOnlyList<string>> ColumnsAsync(params string[] tables)
    {
        await using var connection = new NpgsqlConnection(_pg.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select table_name || '.' || column_name || ' ' || data_type || coalesce('(' || character_maximum_length || ')', '') || ' ' || is_nullable
            from information_schema.columns where table_schema = 'public' and table_name = any(@tables)
            order by table_name, column_name
            """,
            connection);
        command.Parameters.AddWithValue("tables", tables);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    /// <summary>The table's primary key: its name and columns.</summary>
    private async Task<IReadOnlyList<string>> PrimaryKeyAsync(string table)
    {
        await using var connection = new NpgsqlConnection(_pg.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select c.conname || ' ' || pg_get_constraintdef(c.oid)
            from pg_constraint c join pg_class t on t.oid = c.conrelid
            where t.relname = @table and c.contype = 'p'
            """,
            connection);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync();
        var keys = new List<string>();
        while (await reader.ReadAsync())
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    private async Task<string[]> TableOptionsAsync(string table)
    {
        await using var connection = new NpgsqlConnection(_pg.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select coalesce(reloptions, '{}') from pg_class where relname = @table", connection);
        command.Parameters.AddWithValue("table", table);
        return (string[])(await command.ExecuteScalarAsync())!;
    }

    private SpaceTradersDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SpaceTradersDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;
        var scope = new AgentDataScope();
        scope.Set("INITIALIZER-TEST@2026-09-27");
        return new SpaceTradersDbContext(options, scope);
    }

    private async Task<IReadOnlyList<string>> MissingTablesAsync(SpaceTradersDbContext db)
    {
        var modelTables = db.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .OfType<string>()
            .Distinct()
            .ToList();

        await using var connection = new NpgsqlConnection(_pg.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select table_name from information_schema.tables where table_schema = 'public'", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var existing = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            existing.Add(reader.GetString(0));
        }

        return modelTables.Where(table => !existing.Contains(table)).ToList();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_pg.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
