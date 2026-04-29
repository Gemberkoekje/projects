using System.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class Phase2DataModelTests : IntegrationTestBase
{
    // --- Migration tests: new columns exist after InitializeAsync ---

    [SkippableFact]
    public async Task InitializeAsync_AddsPhase2ShipColumns()
    {
        await SpaceTradersDatabaseInitializer.InitializeAsync(Db);

        var columns = await GetColumnNamesAsync(Db, "cached_ships");

        columns.Should().Contain("ModulesJson");
        columns.Should().Contain("FrameJson");
        columns.Should().Contain("ReactorJson");
        columns.Should().Contain("EngineJson");
        columns.Should().Contain("CooldownExpiresAt");
    }

    [SkippableFact]
    public async Task InitializeAsync_AddsPhase2WaypointColumns()
    {
        await SpaceTradersDatabaseInitializer.InitializeAsync(Db);

        var columns = await GetColumnNamesAsync(Db, "cached_waypoints");

        columns.Should().Contain("TraitsJson");
        columns.Should().Contain("ModifiersJson");
        columns.Should().Contain("OrbitalsJson");
        columns.Should().Contain("ParentSymbol");
        columns.Should().Contain("IsUnderConstruction");
        columns.Should().Contain("ChartJson");
    }

    [SkippableFact]
    public async Task InitializeAsync_AddsPhase2ShipyardColumns()
    {
        await SpaceTradersDatabaseInitializer.InitializeAsync(Db);

        var columns = await GetColumnNamesAsync(Db, "cached_shipyards");

        columns.Should().Contain("ShipsDetailJson");
    }

    // --- Repository round-trip tests ---

    [SkippableFact]
    public async Task ShipRepository_UpsertAsync_PersistsComponentFields()
    {
        var repo = new ShipRepository(Db);
        var ship = new ShipModel(
            Symbol: "SHIP-P2-1",
            SystemSymbol: "X1-SYS",
            WaypointSymbol: "X1-WP1",
            Status: "IN_ORBIT",
            FlightMode: "CRUISE",
            FuelCurrent: 300,
            FuelCapacity: 400,
            FrameJson: "{\"symbol\":\"FRAME_MINER\",\"condition\":1.0,\"integrity\":1.0}",
            ReactorJson: "{\"symbol\":\"REACTOR_SOLAR_I\",\"condition\":0.9,\"integrity\":0.9}",
            EngineJson: "{\"symbol\":\"ENGINE_IMPULSE_DRIVE_I\",\"condition\":0.8,\"integrity\":0.8}",
            ModulesJson: "[{\"symbol\":\"MODULE_CARGO_HOLD_I\"}]",
            CooldownExpiresAt: DateTimeOffset.UtcNow.AddSeconds(30));

        await repo.UpsertAsync(ship);

        await using var fresh = CreateFreshContext();
        var result = await new ShipRepository(fresh).FindAsync("SHIP-P2-1");

        result.Should().NotBeNull();
        result!.FrameJson.Should().Contain("FRAME_MINER");
        result.ReactorJson.Should().Contain("REACTOR_SOLAR_I");
        result.EngineJson.Should().Contain("ENGINE_IMPULSE_DRIVE_I");
        result.ModulesJson.Should().Contain("MODULE_CARGO_HOLD_I");
        result.CooldownExpiresAt.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task WaypointRepository_UpsertRangeAsync_PersistsEnrichedFields()
    {
        var repo = new WaypointRepository(Db);
        var waypoint = new WaypointCacheModel(
            Symbol: "X1-WP-ENRICH",
            SystemSymbol: "X1-SYS",
            Type: "ASTEROID",
            X: 10,
            Y: 20,
            HasMarket: false,
            HasShipyard: false,
            LastObservedAt: DateTimeOffset.UtcNow,
            TraitsJson: "[{\"symbol\":\"COMMON_METAL_DEPOSITS\"}]",
            ModifiersJson: "[{\"symbol\":\"STRIPPED\"}]",
            OrbitalsJson: "[{\"symbol\":\"X1-WP-ENRICH-A\"}]",
            ParentSymbol: "X1-WP-PARENT",
            IsUnderConstruction: false,
            ChartJson: "{\"submittedBy\":\"AGENT-1\"}");

        await repo.UpsertRangeAsync([waypoint]);

        await using var fresh = CreateFreshContext();
        var result = await new WaypointRepository(fresh).FindAsync("X1-WP-ENRICH");

        result.Should().NotBeNull();
        result!.TraitsJson.Should().Contain("COMMON_METAL_DEPOSITS");
        result.ModifiersJson.Should().Contain("STRIPPED");
        result.OrbitalsJson.Should().Contain("X1-WP-ENRICH-A");
        result.ParentSymbol.Should().Be("X1-WP-PARENT");
        result.IsUnderConstruction.Should().BeFalse();
        result.ChartJson.Should().Contain("AGENT-1");
    }

    [SkippableFact]
    public async Task ShipyardRepository_UpsertAsync_PersistsShipsDetailJson()
    {
        var repo = new ShipyardRepository(Db);
        var shipyard = new ShipyardDataModel(
            WaypointSymbol: "X1-WP-SY",
            SystemSymbol: "X1-SYS",
            ShipTypesJson: "[{\"type\":\"SHIP_MINING_DRONE\"}]",
            ShipsDetailJson: "[{\"type\":\"SHIP_MINING_DRONE\",\"purchasePrice\":50000}]");

        await repo.UpsertAsync(shipyard);

        await using var fresh = CreateFreshContext();
        var entity = await fresh.Shipyards.FirstOrDefaultAsync(s => s.WaypointSymbol == "X1-WP-SY");

        entity.Should().NotBeNull();
        entity!.ShipsDetailJson.Should().Contain("50000");
    }

    private static async Task<List<string>> GetColumnNamesAsync(SpaceTradersDbContext ctx, string tableName)
    {
        await using var command = ctx.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"""
SELECT column_name
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name = '{tableName}';
""";

        if (command.Connection is not null && command.Connection.State != ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }
}
