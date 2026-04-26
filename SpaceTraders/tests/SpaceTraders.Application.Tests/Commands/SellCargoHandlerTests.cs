using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class SellCargoHandlerTests
{
    private static void AddWaypoint(
        SpaceTraders.Infrastructure.Persistence.SpaceTradersDbContext db,
        string symbol = "X1-AB-001",
        bool hasMarket = true)
    {
        db.Waypoints.Add(new CachedWaypoint
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = symbol,
            SystemSymbol = "X1-AB",
            Type = "ORBITAL_STATION",
            X = 0,
            Y = 0,
            HasMarket = hasMarket,
            HasShipyard = false,
            LastObservedAt = DateTimeOffset.UtcNow,
        });
    }

    [Fact]
    public async Task SellCargo_AppliesCargoAndCreditsFromResponse()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT-1", 55_000, new CargoModel(0, 60), 5_000));

        var assignments = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = "SHIP-1",
            WaypointSymbol = "X1-AB-001",
            Status = "DOCKED",
            CargoCurrent = 10,
            CargoCapacity = 60,
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        AddWaypoint(db);
        db.Agents.Add(new CachedAgent { AgentToken = TestDbContextFactory.AgentToken, Symbol = "AGENT-1", StartingFaction = "COSMIC", Credits = 50_000, ShipCount = 1, LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);
        var agents = new AgentRepository(db);
        var waypoints = new WaypointRepository(db);

        await new SellCargoHandler(port, ships, agents, waypoints, assignments, bus, NullLogger<SellCargoHandler>.Instance)
            .Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        var ship = await db.Ships.FindAsync(TestDbContextFactory.AgentToken, "SHIP-1");
        ship!.CargoCurrent.Should().Be(0);

        var agent = await db.Agents.FindAsync(TestDbContextFactory.AgentToken, "AGENT-1");
        agent!.Credits.Should().Be(55_000);
    }

    [Fact]
    public async Task SellCargo_PublishesShipCargoSoldEvent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT-1", 55_000, new CargoModel(0, 60), 5_000));

        var assignments = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = "SHIP-1",
            WaypointSymbol = "X1-AB-001",
            Status = "DOCKED",
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        AddWaypoint(db);
        db.Agents.Add(new CachedAgent { AgentToken = TestDbContextFactory.AgentToken, Symbol = "AGENT-1", StartingFaction = "COSMIC", Credits = 50_000, ShipCount = 1, LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);
        var agents = new AgentRepository(db);
        var waypoints = new WaypointRepository(db);

        await new SellCargoHandler(port, ships, agents, waypoints, assignments, bus, NullLogger<SellCargoHandler>.Instance)
            .Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task SellCargo_PublishesAssignmentCompletedEvent_WhenAssignmentWasMarkedComplete()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT-1", 55_000, new CargoModel(0, 60), 5_000));

        var assignments = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipAssignmentRepository>();
        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Trade", "X1-AB-001", "X1-AB-002", "IRON_ORE", null, 5, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow));
        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = "SHIP-1",
            WaypointSymbol = "X1-AB-001",
            Status = "DOCKED",
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        AddWaypoint(db);
        db.Agents.Add(new CachedAgent { AgentToken = TestDbContextFactory.AgentToken, Symbol = "AGENT-1", StartingFaction = "COSMIC", Credits = 50_000, ShipCount = 1, LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);
        var agents = new AgentRepository(db);
        var waypoints = new WaypointRepository(db);

        await new SellCargoHandler(port, ships, agents, waypoints, assignments, bus, NullLogger<SellCargoHandler>.Instance)
            .Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        await bus.Received().PublishAsync(
            Arg.Is<object>(m => m.GetType().Name == "ShipAssignmentCompletedEvent"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task SellCargo_DoesNotIssueFollowUpGets()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT-1", 55_000, new CargoModel(0, 60), 5_000));

        var assignments = Substitute.For<SpaceTraders.Application.Interfaces.Repositories.IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = "SHIP-1",
            WaypointSymbol = "X1-AB-001",
            Status = "DOCKED",
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        AddWaypoint(db);
        db.Agents.Add(new CachedAgent { AgentToken = TestDbContextFactory.AgentToken, Symbol = "AGENT-1", StartingFaction = "COSMIC", Credits = 50_000, ShipCount = 1, LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);
        var agents = new AgentRepository(db);
        var waypoints = new WaypointRepository(db);

        await new SellCargoHandler(port, ships, agents, waypoints, assignments, bus, NullLogger<SellCargoHandler>.Instance)
            .Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        await port.Received(1).SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
