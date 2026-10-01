using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Application.DTOs;
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
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Shipyards;

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
        shipyard!.ShipTypes.Should().Equal("SHIP_MINING_DRONE");
        shipyard.Ships.Should().ContainSingle(s => s.Type == "SHIP_MINING_DRONE" && s.PurchasePrice == 42_940);
    }

    [Fact]
    public async Task StartAsync_KeepsAContractsTerms()
    {
        // B31: startup sync stored contracts without their terms, so every restart blanked the
        // deliverables and the deadline that the contract plan works from, until the next delivery.
        var deadline = new DateTimeOffset(2026, 10, 8, 7, 9, 22, TimeSpan.Zero);
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
