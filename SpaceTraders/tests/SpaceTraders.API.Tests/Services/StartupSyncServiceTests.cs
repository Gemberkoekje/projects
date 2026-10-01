using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

namespace SpaceTraders.API.Tests.Services;

public sealed class StartupSyncServiceTests
{
    private const string AgentToken = "startup-sync-test-token";

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
            db.Systems.Add(new CachedSystem { AgentToken = AgentToken, Symbol = "X1-AB", SectorSymbol = "X1", Type = "RED_STAR" });
            db.Waypoints.Add(new CachedWaypoint { AgentToken = AgentToken, Symbol = "X1-AB-1", SystemSymbol = "X1-AB", Type = "PLANET" });
            db.Waypoints.Add(new CachedWaypoint { AgentToken = AgentToken, Symbol = "X1-AB-2", SystemSymbol = "X1-AB", Type = "MOON" });
            db.Ships.Add(new CachedShip
            {
                AgentToken = AgentToken,
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

    private ServiceProvider BuildProvider()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddSingleton<IAgentDataScope>(_ =>
        {
            var scope = new AgentDataScope();
            scope.Set(AgentToken);
            return scope;
        });
        services.AddDbContext<SpaceTradersDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton(_apiClient);
        return services.BuildServiceProvider();
    }
}
