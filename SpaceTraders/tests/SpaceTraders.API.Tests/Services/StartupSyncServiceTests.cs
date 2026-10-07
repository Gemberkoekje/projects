using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Markets;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Shipyards;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems;

namespace SpaceTraders.API.Tests.Services;

public sealed class StartupSyncServiceTests
{
    private const string AgentId = "AGENT@2026-09-27";

    private readonly ISpaceTradersApiClient _apiClient = Substitute.For<ISpaceTradersApiClient>();

    public StartupSyncServiceTests()
    {
        _apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns(new Agent { Symbol = "AGENT", StartingFaction = "COSMIC", Credits = 175_000 });
        _apiClient.GetMyShipsAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedApiResponse<Ship>
            {
                Data =
                [
                    new Ship
                    {
                        Symbol = "AGENT-1",
                        Registration = new ShipRegistration { Role = "COMMAND" },
                        Nav = new ShipNav { SystemSymbol = "X1-AB", WaypointSymbol = "X1-AB-2", Status = "DOCKED", FlightMode = "CRUISE" },
                        Fuel = new ShipFuel { Current = 300, Capacity = 400 },
                        Cargo = new FleetShipCargo { Units = 0, Capacity = 40, Inventory = [] },
                    },
                ],
                Meta = new Meta { Total = 1, Page = 1, Limit = 20 },
            });
        _apiClient.GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedApiResponse<Contract> { Data = [], Meta = new Meta { Total = 0, Page = 1, Limit = 20 } });
        _apiClient.GetWaypointsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedApiResponse<Waypoint> { Data = [], Meta = new Meta { Total = 0, Page = 1, Limit = 20 } });
    }

    [Fact]
    public async Task StartAsync_UpdatesAShip_AndKeepsItsActiveGoal()
    {
        var goal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-2" };
        var goalJson = JsonSerializer.Serialize<ShipGoal>(goal);
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-1", SystemSymbol = "X1-AB", Type = "PLANET" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "MOON" });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-1",
                SystemSymbol = "X1-AB",
                WaypointSymbol = "X1-AB-1",
                Status = "IN_TRANSIT",
                FuelCurrent = 400,
                FuelCapacity = 400,
                GoalId = goal.GoalId,
                GoalKind = goal.Kind.ToString(),
                GoalPayloadJson = goalJson,
                GoalStatus = (int)GoalStatus.Assigned,
            });
            await db.SaveChangesAsync();
        }

        var sync = new StartupSyncService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StartupSyncService>.Instance);
        await sync.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var ship = await scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>().Ships.SingleAsync(s => s.Symbol == "AGENT-1");
        ship.Status.Should().Be("DOCKED");
        ship.WaypointSymbol.Should().Be("X1-AB-2");
        ship.FuelCurrent.Should().Be(300);
        ship.GoalId.Should().Be(goal.GoalId);
        ship.GoalKind.Should().Be("ScoutWaypoint");
        ship.GoalPayloadJson.Should().Be(goalJson);
        ship.GoalStatus.Should().Be((int)GoalStatus.Assigned);
    }

    [Fact]
    public async Task StartAsync_KeepsWhatAnArrivalStoredWhileTheShipsWereFetched()
    {
        // B77: the ship event scheduler starts first in the chain, so an arrival due during a restart is handled while the sync
        // fetches the ships' pages. On 2026-10-07 at 05:27:57Z SPECTER-121 jumped on to X1-FA16; at 05:28:01Z the sync saved it
        // back at the X1-AT30 gate from its page, fetched before the jump. Its next jump was refused, and the plan left that
        // gate alone for an hour. Here: the page has AGENT-1 docked at X1-AB-2, and meanwhile it flies on to X1-AB-1.
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-1", SystemSymbol = "X1-AB", Type = "PLANET" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "MOON" });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-1",
                SystemSymbol = "X1-AB",
                WaypointSymbol = "X1-AB-2",
                Status = "DOCKED",
                FuelCurrent = 300,
                FuelCapacity = 400,
                LastSyncedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            });
            await db.SaveChangesAsync();
        }

        var arrivesAt = DateTimeOffset.UtcNow.AddMinutes(1);
        var page = await _apiClient.GetMyShipsAsync(1, 20, CancellationToken.None);
        _apiClient.GetMyShipsAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await using var arrivalScope = provider.CreateAsyncScope();
            await new ShipRepository(arrivalScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>()).UpdateNavAsync(
                "AGENT-1",
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-1", "CRUISE", "X1-AB-1", arrivesAt),
                new FuelModel(250, 400));
            return page;
        });

        var sync = new StartupSyncService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StartupSyncService>.Instance);
        await sync.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var ship = await scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>().Ships.SingleAsync(s => s.Symbol == "AGENT-1");
        ship.Should().BeEquivalentTo(
            new { Status = "IN_TRANSIT", WaypointSymbol = "X1-AB-1", DestWaypointSymbol = "X1-AB-1", ArrivesAt = (DateTimeOffset?)arrivesAt, FuelCurrent = 250 },
            "what the arrival stored is newer than the page");
        ship.ShipType.Should().Be("COMMAND", "what doesn't change with a flight comes from the page all the same");
    }

    [Fact]
    public async Task StartAsync_CachesTheShipyardAShipIsAt_WithItsPrices()
    {
        // A purchase reads the price from the cached shipyard. Startup sync stored the priced ships
        // where the ship types belong, so the price looked unknown and every purchase at that
        // shipyard failed until a ship arrived there again.
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "MOON", HasShipyard = true });
            await db.SaveChangesAsync();
        }

        _apiClient.GetShipyardAsync("X1-AB", "X1-AB-2", Arg.Any<CancellationToken>())
            .Returns(new Shipyard
            {
                Symbol = "X1-AB-2",
                ShipTypes = [new ShipyardShipType { Type = "SHIP_MINING_DRONE" }],
                Ships = [new ShipyardShip { Type = "SHIP_MINING_DRONE", PurchasePrice = 42_940 }],
            });

        var sync = new StartupSyncService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StartupSyncService>.Instance);
        await sync.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var shipyard = await new ShipyardRepository(scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>())
            .FindByWaypointAsync("X1-AB-2");
        shipyard.Should().NotBeNull();
        shipyard.ShipTypes.Should().Equal("SHIP_MINING_DRONE");
        shipyard.Ships.Should().ContainSingle(s => s.Type == "SHIP_MINING_DRONE" && s.PurchasePrice == 42_940);
    }

    [Fact]
    public async Task StartAsync_KeepsTheCachedPricesOfAMarket_TheApiAnswersWithout()
    {
        // B62, seen on the cluster on 2026-10-04: a market answered without prices was stored as it came, and the cache lost
        // the prices it had, a fuel price among them. Startup sync stores what it fetches the same way.
        const string cachedGoods = "[{\"symbol\":\"FUEL\",\"tradeVolume\":180,\"supply\":\"MODERATE\",\"purchasePrice\":72,\"sellPrice\":70}]";
        var observed = new DateTimeOffset(2026, 10, 04, 19, 20, 29, TimeSpan.Zero);
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "ORBITAL_STATION", HasMarket = true });
            db.Markets.Add(new CachedMarket
            {
                AgentId = AgentId,
                WaypointSymbol = "X1-AB-2",
                SystemSymbol = "X1-AB",
                TradeGoodsJson = cachedGoods,
                ExchangeJson = "[{\"symbol\":\"FUEL\"}]",
                LastObservedAt = observed,
            });
            await db.SaveChangesAsync();
        }

        _apiClient.GetMarketAsync("X1-AB", "X1-AB-2", Arg.Any<CancellationToken>())
            .Returns(new Market { Symbol = "X1-AB-2", Exchange = [new TradeGoodSymbol { Symbol = "FUEL" }], Imports = [], Exports = [] });

        var sync = new StartupSyncService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StartupSyncService>.Instance);
        await sync.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var market = await scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>().Markets.SingleAsync(m => m.WaypointSymbol == "X1-AB-2");
        market.TradeGoodsJson.Should().Be(cachedGoods, "an answer without prices says nothing about them");
        market.LastObservedAt.Should().Be(observed, "the prices weren't seen again");
    }

    [Fact]
    public async Task StartAsync_KeepsTheCachedListingsOfAShipyard_TheApiAnswersWithout()
    {
        // B64: the API lists a shipyard's ships, with their prices, only while one of our ships is there. Startup sync
        // stored an answer without them as it came, which wiped the listings the cache had, and with them the prices a
        // purchase reads (B28), as an answer without prices did to a market's (B62).
        const string cachedShips = "[{\"type\":\"SHIP_MINING_DRONE\",\"purchasePrice\":42940,\"supply\":\"MODERATE\"}]";
        var observed = new DateTimeOffset(2026, 10, 04, 19, 20, 29, TimeSpan.Zero);
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "MOON", HasShipyard = true });
            db.Shipyards.Add(new CachedShipyard
            {
                AgentId = AgentId,
                WaypointSymbol = "X1-AB-2",
                SystemSymbol = "X1-AB",
                ShipTypesJson = "[{\"type\":\"SHIP_MINING_DRONE\"}]",
                ShipsDetailJson = cachedShips,
                LastObservedAt = observed,
            });
            await db.SaveChangesAsync();
        }

        _apiClient.GetShipyardAsync("X1-AB", "X1-AB-2", Arg.Any<CancellationToken>())
            .Returns(new Shipyard { Symbol = "X1-AB-2", ShipTypes = [new ShipyardShipType { Type = "SHIP_MINING_DRONE" }] });

        var sync = new StartupSyncService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StartupSyncService>.Instance);
        await sync.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var shipyard = await scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>().Shipyards.SingleAsync(s => s.WaypointSymbol == "X1-AB-2");
        shipyard.ShipsDetailJson.Should().Be(cachedShips, "an answer without listings says nothing about them");
        shipyard.LastObservedAt.Should().Be(observed, "the listings weren't seen again");
    }

    [Fact]
    public async Task StartAsync_KeepsAContractsTerms()
    {
        // B31: startup sync stored contracts without their terms, so every restart blanked the
        // deliverables and the deadline that the contract plan works from, until the next delivery.
        var deadline = new DateTimeOffset(2026, 10, 08, 07, 09, 22, TimeSpan.Zero);
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "MOON" });
            db.Contracts.Add(new CachedContract
            {
                AgentId = AgentId,
                Id = "C-1",
                FactionSymbol = "COSMIC",
                Type = "PROCUREMENT",
                IsAccepted = true,
                TermsDeadline = deadline,
                DeliverablesJson = JsonSerializer.Serialize(new List<ContractDeliverableDto> { new("IRON_ORE", "X1-AB-MKT", 42, 5) }),
            });
            await db.SaveChangesAsync();
        }

        _apiClient.GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedApiResponse<Contract>
            {
                Data =
                [
                    new Contract
                    {
                        Id = "C-1",
                        FactionSymbol = "COSMIC",
                        Type = "PROCUREMENT",
                        Accepted = true,
                        Terms = new ContractTerms
                        {
                            Deadline = deadline,
                            Deliver = [new ContractDeliverGood { TradeSymbol = "IRON_ORE", DestinationSymbol = "X1-AB-MKT", UnitsRequired = 42, UnitsFulfilled = 7 }],
                        },
                    },
                ],
                Meta = new Meta { Total = 1, Page = 1, Limit = 20 },
            });

        var sync = new StartupSyncService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StartupSyncService>.Instance);
        await sync.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var contract = await scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>().Contracts.SingleAsync(c => c.Id == "C-1");
        contract.TermsDeadline.Should().Be(deadline);
        JsonSerializer.Deserialize<List<ContractDeliverableDto>>(contract.DeliverablesJson ?? "[]")
            .Should().Equal(new ContractDeliverableDto("IRON_ORE", "X1-AB-MKT", 42, 7));
    }

    [Fact]
    public async Task StartAsync_StoresEachWaypointsTraitsAndModifiers()
    {
        // B34: sync stored only a waypoint's market and shipyard flags, so nothing could tell which
        // asteroid yields which ore.
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            await db.SaveChangesAsync();
        }

        _apiClient.GetWaypointsAsync("X1-AB", 1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SystemWaypoints());

        var sync = new StartupSyncService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StartupSyncService>.Instance);
        await sync.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var asteroid = await new WaypointRepository(scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>()).FindAsync("X1-AB-1");
        asteroid.Should().NotBeNull();
        asteroid.Type.Should().Be("ASTEROID");
        asteroid.TraitsJson.Should().Contain("COMMON_METAL_DEPOSITS");
        asteroid.ModifiersJson.Should().Contain("STRIPPED");
        asteroid.ParentSymbol.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_FillsInTheTraitsOfWaypointsCachedWithoutThem()
    {
        // B34 on the cluster: the system's waypoints were cached before sync stored traits, and a
        // cached system was never fetched again. Filling them in keeps when each was last observed,
        // which scouting reads.
        var observed = new DateTimeOffset(2026, 10, 02, 08, 51, 00, TimeSpan.Zero);
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-1", SystemSymbol = "X1-AB", Type = "ASTEROID", X = 5, Y = 7, LastObservedAt = observed });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "MOON", LastObservedAt = observed });
            await db.SaveChangesAsync();
        }

        _apiClient.GetWaypointsAsync("X1-AB", 1, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SystemWaypoints());

        var sync = new StartupSyncService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StartupSyncService>.Instance);
        await sync.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var waypoints = new WaypointRepository(scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>());
        var asteroid = await waypoints.FindAsync("X1-AB-1");
        asteroid!.TraitsJson.Should().Contain("COMMON_METAL_DEPOSITS");
        asteroid.ModifiersJson.Should().Contain("STRIPPED");
        asteroid.LastObservedAt.Should().Be(observed);
        (await waypoints.GetBySystemAsync("X1-AB")).Should().HaveCount(2);
    }

    [Fact]
    public async Task StartAsync_DoesNotFetchTheWaypointsAgain_WhenTheirTraitsAreCached()
    {
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Systems.Add(new CachedSystem { AgentId = AgentId, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-1", SystemSymbol = "X1-AB", Type = "ASTEROID", TraitsJson = """[{"symbol":"COMMON_METAL_DEPOSITS"}]""" });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "MOON", TraitsJson = "[]" });
            await db.SaveChangesAsync();
        }

        var sync = new StartupSyncService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StartupSyncService>.Instance);
        await sync.StartAsync(CancellationToken.None);

        await _apiClient.DidNotReceive().GetWaypointsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static PagedApiResponse<Waypoint> SystemWaypoints() => new()
    {
        Data =
        [
            new Waypoint
            {
                Symbol = "X1-AB-1",
                SystemSymbol = "X1-AB",
                Type = "ASTEROID",
                X = 5,
                Y = 7,
                Traits = [new WaypointTrait { Symbol = "COMMON_METAL_DEPOSITS" }],
                Modifiers = [new WaypointModifier { Symbol = "STRIPPED" }],
            },
            new Waypoint { Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "MOON", Orbits = "X1-AB-0", Traits = [] },
        ],
        Meta = new Meta { Total = 2, Page = 1, Limit = 20 },
    };

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
        return services.BuildServiceProvider();
    }
}
