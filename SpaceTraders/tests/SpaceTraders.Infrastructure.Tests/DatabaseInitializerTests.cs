using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Scoping;
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
