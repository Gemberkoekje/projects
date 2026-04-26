using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class ExtractResourcesHandlerTests
{
    private static void AddShip(
        SpaceTraders.Infrastructure.Persistence.SpaceTradersDbContext db,
        string symbol,
        string waypoint = "X1-AB-001",
        string status = "IN_ORBIT",
        int cargoCurrent = 0,
        int cargoCapacity = 60)
    {
        db.Ships.Add(new CachedShip
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = symbol,
            SystemSymbol = "X1-AB",
            WaypointSymbol = waypoint,
            Status = status,
            FlightMode = "CRUISE",
            FuelCurrent = 80,
            FuelCapacity = 100,
            CargoCurrent = cargoCurrent,
            CargoCapacity = cargoCapacity,
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    private static void AddWaypoint(
        SpaceTraders.Infrastructure.Persistence.SpaceTradersDbContext db,
        string symbol = "X1-AB-001",
        string type = "ASTEROID")
    {
        db.Waypoints.Add(new CachedWaypoint
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = symbol,
            SystemSymbol = "X1-AB",
            Type = type,
            X = 0,
            Y = 0,
            HasMarket = false,
            HasShipyard = false,
            LastObservedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task ExtractResources_UpdatesCargoFromResponse()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ExtractionActionResult("IRON_ORE", 10, new CargoModel(10, 60), 60));

        var bus = Substitute.For<Wolverine.IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1", cargoCurrent: 0, cargoCapacity: 60);
        AddWaypoint(db);
        var ships = new ShipRepository(db);
        var waypoints = new WaypointRepository(db);

        await new ExtractResourcesHandler(port, ships, waypoints, bus, NullLogger<ExtractResourcesHandler>.Instance)
            .Handle(new ExtractResourcesCommand("SHIP-1"), CancellationToken.None);

        var cached = await db.Ships.FindAsync(TestDbContextFactory.AgentToken, "SHIP-1");
        cached!.CargoCurrent.Should().Be(10);
        cached.CargoCapacity.Should().Be(60);
    }

    [Fact]
    public async Task ExtractResources_DoesNotIssueFollowUpGets()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ExtractionActionResult("IRON_ORE", 5, new CargoModel(5, 60), 60));

        var bus = Substitute.For<Wolverine.IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");
        AddWaypoint(db);
        var ships = new ShipRepository(db);
        var waypoints = new WaypointRepository(db);

        await new ExtractResourcesHandler(port, ships, waypoints, bus, NullLogger<ExtractResourcesHandler>.Instance)
            .Handle(new ExtractResourcesCommand("SHIP-1"), CancellationToken.None);

        await port.Received(1).ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractResources_AccumulatesCargo_AcrossMultipleExtractions()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(
                new ExtractionActionResult("IRON_ORE", 10, new CargoModel(10, 60), 60),
                new ExtractionActionResult("IRON_ORE", 15, new CargoModel(25, 60), 60));

        var bus = Substitute.For<Wolverine.IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1", cargoCurrent: 0, cargoCapacity: 60);
        AddWaypoint(db);
        var ships = new ShipRepository(db);
        var waypoints = new WaypointRepository(db);
        var handler = new ExtractResourcesHandler(port, ships, waypoints, bus, NullLogger<ExtractResourcesHandler>.Instance);

        await handler.Handle(new ExtractResourcesCommand("SHIP-1"), CancellationToken.None);
        await handler.Handle(new ExtractResourcesCommand("SHIP-1"), CancellationToken.None);

        var cached = await db.Ships.FindAsync(TestDbContextFactory.AgentToken, "SHIP-1");
        cached!.CargoCurrent.Should().Be(25);
    }
}
