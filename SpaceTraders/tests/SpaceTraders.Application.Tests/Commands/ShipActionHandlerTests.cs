using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class ShipActionHandlerTests
{
    private static NavModel MakeNav(string waypoint = "X1-AB-001", string status = "DOCKED") =>
        new(status, "X1-AB", waypoint, "CRUISE", null, null);

    private static void AddShip(
        SpaceTraders.Infrastructure.Persistence.SpaceTradersDbContext db,
        string symbol,
        string waypoint = "X1-AB-001",
        string status = "DOCKED")
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
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    private static void AddWaypoint(
        SpaceTraders.Infrastructure.Persistence.SpaceTradersDbContext db,
        string symbol = "X1-AB-001",
        string type = "ORBITAL_STATION",
        bool hasMarket = true)
    {
        db.Waypoints.Add(new CachedWaypoint
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = symbol,
            SystemSymbol = "X1-AB",
            Type = type,
            X = 0,
            Y = 0,
            HasMarket = hasMarket,
            HasShipyard = false,
            LastObservedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    private static void AddAgent(SpaceTraders.Infrastructure.Persistence.SpaceTradersDbContext db, string symbol = "AGENT-1", long credits = 50_000)
    {
        db.Agents.Add(new CachedAgent
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = symbol,
            StartingFaction = "COSMIC",
            Credits = credits,
            ShipCount = 1,
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    // ─── DockShipHandler ──────────────────────────────────────────────────────

    [Fact]
    public async Task DockShip_UpdatesStatusInCache()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.DockShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(MakeNav(status: "DOCKED"));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1", status: "IN_ORBIT");
        var ships = new ShipRepository(db);

        await new DockShipHandler(port, ships, bus, NullLogger<DockShipHandler>.Instance)
            .Handle(new DockShipCommand("SHIP-1"), CancellationToken.None);

        var cached = await db.Ships.FindAsync(TestDbContextFactory.AgentToken, "SHIP-1");
        cached!.Status.Should().Be("DOCKED");
    }

    [Fact]
    public async Task DockShip_DoesNotIssueFollowUpGets()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.DockShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(MakeNav(status: "DOCKED"));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1", status: "IN_ORBIT");
        var ships = new ShipRepository(db);

        await new DockShipHandler(port, ships, bus, NullLogger<DockShipHandler>.Instance)
            .Handle(new DockShipCommand("SHIP-1"), CancellationToken.None);

        await port.Received(1).DockShipAsync("SHIP-1", Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DockShip_PublishesShipDockedEvent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.DockShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(MakeNav(status: "DOCKED"));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1", status: "IN_ORBIT");
        var ships = new ShipRepository(db);

        await new DockShipHandler(port, ships, bus, NullLogger<DockShipHandler>.Instance)
            .Handle(new DockShipCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipDockedEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.SystemSymbol == "X1-AB" &&
                e.WaypointSymbol == "X1-AB-001"),
            Arg.Any<DeliveryOptions>());
    }

    // ─── OrbitShipHandler ─────────────────────────────────────────────────────

    [Fact]
    public async Task OrbitShip_UpdatesStatusInCache()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(MakeNav(status: "IN_ORBIT"));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");
        var ships = new ShipRepository(db);

        await new OrbitShipHandler(port, ships, bus, NullLogger<OrbitShipHandler>.Instance)
            .Handle(new OrbitShipCommand("SHIP-1"), CancellationToken.None);

        var cached = await db.Ships.FindAsync(TestDbContextFactory.AgentToken, "SHIP-1");
        cached!.Status.Should().Be("IN_ORBIT");
    }

    [Fact]
    public async Task OrbitShip_PublishesShipEnteredOrbitEvent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(MakeNav(status: "IN_ORBIT"));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");
        var ships = new ShipRepository(db);

        await new OrbitShipHandler(port, ships, bus, NullLogger<OrbitShipHandler>.Instance)
            .Handle(new OrbitShipCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<object>(message => message is SpaceTraders.Domain.Events.ShipEnteredOrbitEvent),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task OrbitShip_PublishesShipUndockedEvent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(MakeNav(status: "IN_ORBIT"));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");
        var ships = new ShipRepository(db);

        await new OrbitShipHandler(port, ships, bus, NullLogger<OrbitShipHandler>.Instance)
            .Handle(new OrbitShipCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipUndockedEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.SystemSymbol == "X1-AB" &&
                e.WaypointSymbol == "X1-AB-001"),
            Arg.Any<DeliveryOptions>());
    }

    // ─── RefuelShipHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task RefuelShip_AppliesFuelAndCreditsFromResponse()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.RefuelShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new RefuelActionResult(48_000, new FuelModel(100, 100), 2_000));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");
        AddWaypoint(db);
        AddAgent(db, credits: 50_000);
        var ships = new ShipRepository(db);
        var agents = new AgentRepository(db);
        var waypoints = new WaypointRepository(db);

        await new RefuelShipHandler(port, ships, agents, waypoints, bus, NullLogger<RefuelShipHandler>.Instance)
            .Handle(new RefuelShipCommand("SHIP-1"), CancellationToken.None);

        var ship = await db.Ships.FindAsync(TestDbContextFactory.AgentToken, "SHIP-1");
        ship!.FuelCurrent.Should().Be(100);

        var agent = await db.Agents.FindAsync(TestDbContextFactory.AgentToken, "AGENT-1");
        agent!.Credits.Should().Be(48_000);
    }

    [Fact]
    public async Task RefuelShip_DoesNotIssueFollowUpGets()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.RefuelShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new RefuelActionResult(48_000, new FuelModel(100, 100), 2_000));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");
        AddWaypoint(db);
        AddAgent(db);
        var ships = new ShipRepository(db);
        var agents = new AgentRepository(db);
        var waypoints = new WaypointRepository(db);

        await new RefuelShipHandler(port, ships, agents, waypoints, bus, NullLogger<RefuelShipHandler>.Instance)
            .Handle(new RefuelShipCommand("SHIP-1"), CancellationToken.None);

        await port.Received(1).RefuelShipAsync("SHIP-1", Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefuelShip_PublishesShipRefueledEvent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.RefuelShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new RefuelActionResult(48_000, new FuelModel(100, 100), 2_000));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");
        AddWaypoint(db);
        AddAgent(db, credits: 50_000);
        var ships = new ShipRepository(db);
        var agents = new AgentRepository(db);
        var waypoints = new WaypointRepository(db);

        await new RefuelShipHandler(port, ships, agents, waypoints, bus, NullLogger<RefuelShipHandler>.Instance)
            .Handle(new RefuelShipCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipRefueledEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.FuelCurrent == 100 &&
                e.FuelCapacity == 100 &&
                e.CostPaid == 2_000),
            Arg.Any<DeliveryOptions>());
    }

    // ─── BuyCargoHandler ──────────────────────────────────────────────────────

    [Fact]
    public async Task BuyCargo_AppliesCargoAndCreditsFromResponse()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.BuyCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT-1", 45_000, new CargoModel(10, 60), 5_000));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");
        AddWaypoint(db);
        AddAgent(db, credits: 50_000);
        var ships = new ShipRepository(db);
        var agents = new AgentRepository(db);
        var waypoints = new WaypointRepository(db);

        await new BuyCargoHandler(port, ships, agents, waypoints, bus, NullLogger<BuyCargoHandler>.Instance)
            .Handle(new BuyCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        var ship = await db.Ships.FindAsync(TestDbContextFactory.AgentToken, "SHIP-1");
        ship!.CargoCurrent.Should().Be(10);
        ship.CargoCapacity.Should().Be(60);

        var agent = await db.Agents.FindAsync(TestDbContextFactory.AgentToken, "AGENT-1");
        agent!.Credits.Should().Be(45_000);
    }

    // ─── AssignShipHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task AssignShip_CreatesNewRecord_WhenNoneExists()
    {
        await using var db = TestDbContextFactory.Create();
        var assignments = new ShipAssignmentRepository(db);
        var bus = Substitute.For<Wolverine.IMessageBus>();

        await new AssignShipHandler(assignments, bus, NullLogger<AssignShipHandler>.Instance)
            .Handle(new AssignShipCommand("SHIP-1", "TRADE", "X1-AB-001", "X1-AB-002", "IRON_ORE"), CancellationToken.None);

        var record = await db.ShipAssignments.FindAsync(TestDbContextFactory.AgentToken, "SHIP-1");
        record.Should().NotBeNull();
        record!.Type.Should().Be("TRADE");
        record.CargoSymbol.Should().Be("IRON_ORE");
        record.StepIndex.Should().Be(0);
        record.CompletedAt.Should().BeNull();
        await bus.Received(1).PublishAsync(Arg.Any<object>(), Arg.Any<Wolverine.DeliveryOptions>());
    }

    [Fact]
    public async Task AssignShip_ReplacesExistingRecord()
    {
        await using var db = TestDbContextFactory.Create();
        db.ShipAssignments.Add(new ShipAssignmentRecord
        {
            AgentToken = TestDbContextFactory.AgentToken,
            ShipSymbol = "SHIP-1",
            Type = "IDLE",
            StepIndex = 3,
            AssignedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var assignments = new ShipAssignmentRepository(db);
        var bus = Substitute.For<Wolverine.IMessageBus>();

        await new AssignShipHandler(assignments, bus, NullLogger<AssignShipHandler>.Instance)
            .Handle(new AssignShipCommand("SHIP-1", "MINE"), CancellationToken.None);

        var record = await db.ShipAssignments.FindAsync(TestDbContextFactory.AgentToken, "SHIP-1");
        record!.Type.Should().Be("MINE");
        record.StepIndex.Should().Be(0);
        await bus.Received(1).PublishAsync(Arg.Any<object>(), Arg.Any<Wolverine.DeliveryOptions>());
    }
}
