using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.API.Tests.Services;

public sealed class StartupSnapshotServiceTests
{
    private const string AgentId = "AGENT@2026-09-27";

    private readonly ISpaceTradersApiClient _apiClient = Substitute.For<ISpaceTradersApiClient>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();

    public StartupSnapshotServiceTests()
    {
        _settings.GetAsync<bool>("Automation.Enabled", Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task StartAsync_BuildsTheSnapshotFromTheCache_WithoutCallingTheApi()
    {
        // B35: right after startup sync, the snapshot fetched the agent, the ships, the system, every
        // page of its waypoints and the markets and shipyards where ships were, all over again:
        // about 11 API calls on every start.
        var goal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-2" };
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Agents.Add(new CachedAgent { AgentId = AgentId, Symbol = "AGENT", StartingFaction = "COSMIC", HeadquartersSymbol = "X1-AB-1", Credits = 175_000, ShipCount = 1 });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-1",
                ShipType = "COMMAND",
                SystemSymbol = "X1-AB",
                WaypointSymbol = "X1-AB-2",
                Status = "DOCKED",
                FuelCurrent = 300,
                FuelCapacity = 400,
                MountsJson = """["MOUNT_MINING_LASER_I"]""",
                GoalId = goal.GoalId,
                GoalKind = goal.Kind.ToString(),
                GoalPayloadJson = JsonSerializer.Serialize<ShipGoal>(goal),
                GoalStatus = (int)GoalStatus.Assigned,
            });
            db.Contracts.Add(new CachedContract
            {
                AgentId = AgentId,
                Id = "C-1",
                FactionSymbol = "COSMIC",
                Type = "PROCUREMENT",
                IsAccepted = true,
                DeliverablesJson = JsonSerializer.Serialize(new List<ContractDeliverableDto> { new("IRON_ORE", "X1-AB-2", 42, 7) }),
            });
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-1", SystemSymbol = "X1-AB", Type = "PLANET" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "MOON", HasMarket = true, HasShipyard = true });
            db.Markets.Add(new CachedMarket
            {
                AgentId = AgentId,
                WaypointSymbol = "X1-AB-2",
                SystemSymbol = "X1-AB",
                TradeGoodsJson = """[{"symbol":"FUEL","type":"EXCHANGE","tradeVolume":100,"supply":"MODERATE","purchasePrice":72,"sellPrice":68}]""",
            });
            db.Shipyards.Add(new CachedShipyard
            {
                AgentId = AgentId,
                WaypointSymbol = "X1-AB-2",
                SystemSymbol = "X1-AB",
                ShipsDetailJson = """[{"type":"SHIP_MINING_DRONE","purchasePrice":42940}]""",
            });
            await db.SaveChangesAsync();
        }

        var service = StartupSnapshots(provider);
        await service.StartAsync(CancellationToken.None);

        _apiClient.ReceivedCalls().Should().BeEmpty();

        await using var scope = provider.CreateAsyncScope();
        var snapshot = await scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>().StartupSnapshots.SingleAsync();
        snapshot.IsInitialSnapshot.Should().BeTrue();
        snapshot.Reason.Should().Be(StartupSnapshot.StartupReason);
        snapshot.Discovered.Should().BeNull();
        var root = JsonNode.Parse(snapshot.SnapshotJson)!;
        root["Reason"]!.GetValue<string>().Should().Be("Startup");
        root["Discoveries"].Should().BeNull("a startup snapshot lists no discoveries");
        root["Agent"]!["Credits"]!.GetValue<long>().Should().Be(175_000);

        var ship = root["Ships"]!.AsArray().Should().ContainSingle().Subject!;
        ship["Symbol"]!.GetValue<string>().Should().Be("AGENT-1");
        ship["Mounts"]![0]!.GetValue<string>().Should().Be("MOUNT_MINING_LASER_I");
        ship["Goal"]!["Kind"]!.GetValue<string>().Should().Be("ScoutWaypoint");

        var contract = root["Contracts"]!.AsArray().Should().ContainSingle().Subject!;
        contract["Deliverables"]![0]!["UnitsFulfilled"]!.GetValue<int>().Should().Be(7);

        var system = root["Systems"]!.AsArray().Should().ContainSingle().Subject!;
        system["System"]!["Symbol"]!.GetValue<string>().Should().Be("X1-AB");
        var waypoints = system["Waypoints"]!.AsArray();
        waypoints.Select(w => w!["Waypoint"]!["Symbol"]!.GetValue<string>()).Should().Equal("X1-AB-1", "X1-AB-2");
        var shipWaypoint = waypoints.Single(w => w!["Waypoint"]!["Symbol"]!.GetValue<string>() == "X1-AB-2")!;
        shipWaypoint["Market"]!["TradeGoods"]![0]!["sellPrice"]!.GetValue<int>().Should().Be(68);
        shipWaypoint["Shipyard"]!["Ships"]![0]!["purchasePrice"]!.GetValue<int>().Should().Be(42_940);
    }

    [Fact]
    public async Task StartAsync_HoldsEveryCachedShipyardAndMarket_AlsoWhereNoShipIs()
    {
        // Asked on 2026-10-04: "If the snapshot is taken and data is in memory but there's no ship at the shipyard right
        // now, will the data from memory be in the snapshot or none at all?" None at all: the snapshot held only the market
        // and shipyard where a ship was. Now it holds every one the cache has, as last seen, in another system too.
        var seen = new DateTimeOffset(2026, 10, 04, 14, 02, 11, TimeSpan.Zero);
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Agents.Add(new CachedAgent { AgentId = AgentId, Symbol = "AGENT", StartingFaction = "COSMIC", HeadquartersSymbol = "X1-AB-1", Credits = 175_000, ShipCount = 1 });
            db.Ships.Add(new CachedShip { AgentId = AgentId, Symbol = "AGENT-1", ShipType = "COMMAND", SystemSymbol = "X1-AB", WaypointSymbol = "X1-AB-1", Status = "DOCKED" });
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-CD", SectorSymbol = "X1", Type = "BLUE_STAR" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-1", SystemSymbol = "X1-AB", Type = "PLANET" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "MOON", HasShipyard = true });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-CD-5", SystemSymbol = "X1-CD", Type = "ORBITAL_STATION", HasMarket = true });
            db.Shipyards.Add(new CachedShipyard
            {
                AgentId = AgentId,
                WaypointSymbol = "X1-AB-2",
                SystemSymbol = "X1-AB",
                ShipTypesJson = """[{"type":"SHIP_MINING_DRONE"},{"type":"SHIP_PROBE"}]""",
                ShipsDetailJson = """[{"type":"SHIP_MINING_DRONE","purchasePrice":42940,"frame":{"fuelCapacity":80}}]""",
                LastObservedAt = seen,
            });
            db.Markets.Add(new CachedMarket
            {
                AgentId = AgentId,
                WaypointSymbol = "X1-CD-5",
                SystemSymbol = "X1-CD",
                ExportsJson = """[{"symbol":"FAB_MATS"}]""",
                LastObservedAt = seen,
            });
            await db.SaveChangesAsync();
        }

        await StartupSnapshots(provider).StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var root = JsonNode.Parse((await scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>().StartupSnapshots.SingleAsync()).SnapshotJson)!;
        var systems = root["Systems"]!.AsArray();
        systems.Select(system => system!["System"]!["Symbol"]!.GetValue<string>()).Should().Equal(["X1-AB", "X1-CD"], "the ship's system first");
        var shipyard = Waypoint(systems, "X1-AB-2")["Shipyard"]!;
        shipyard["ShipTypes"]!.AsArray().Should().HaveCount(2);
        shipyard["Ships"]![0]!["purchasePrice"]!.GetValue<int>().Should().Be(42_940, "the listing the cache keeps, though no ship is there");
        shipyard["Ships"]![0]!["frame"]!["fuelCapacity"]!.GetValue<int>().Should().Be(80);
        shipyard["LastObservedAt"]!.GetValue<DateTimeOffset>().Should().Be(seen, "when it was seen");
        Waypoint(systems, "X1-CD-5")["Market"]!["Exports"]![0]!["symbol"]!.GetValue<string>().Should().Be("FAB_MATS");
        Waypoint(systems, "X1-AB-1")["Market"].Should().BeNull("a waypoint without a market has none");
    }

    [Fact]
    public async Task StartAsync_LeavesTheAutomationSwitchAlone()
    {
        // The snapshot switched automation off and on again around its API calls. With nothing left to
        // wait for, it has no reason to, and switching it back on could undo a switch-off made meanwhile.
        using var provider = BuildProvider();

        var service = StartupSnapshots(provider);
        await service.StartAsync(CancellationToken.None);

        await _settings.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static StartupSnapshotService StartupSnapshots(ServiceProvider provider)
        => new(
            new GameStateSnapshots(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<GameStateSnapshots>.Instance),
            NullLogger<StartupSnapshotService>.Instance);

    private static JsonNode Waypoint(JsonArray systems, string symbol)
        => systems
            .SelectMany(system => system!["Waypoints"]!.AsArray())
            .Single(waypoint => waypoint!["Waypoint"]!["Symbol"]!.GetValue<string>() == symbol)!;

    private ServiceProvider BuildProvider()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddSingleton<IAgentDataScope>(_ =>
        {
            var scope = new AgentDataScope();
            scope.Set(AgentId);
            return scope;
        });
        services.AddDbContext<SpaceTradersDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton(_apiClient);
        services.AddSingleton(_settings);
        return services.BuildServiceProvider();
    }
}
