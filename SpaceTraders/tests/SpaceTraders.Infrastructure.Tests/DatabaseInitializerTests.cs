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
        Skip.IfNot(System.IO.File.Exists("/var/run/docker.sock") || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")), "Docker is not available – skipping integration tests.");

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
