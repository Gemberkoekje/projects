using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class ShipActionHandlerTests
{
    private static ShipNav MakeNav(string waypoint = "X1-AB-001", string status = "DOCKED") =>
        new() { SystemSymbol = "X1-AB", WaypointSymbol = waypoint, Status = status, FlightMode = "CRUISE" };

    private static Agent MakeAgent(string symbol = "AGENT-1", long credits = 80_000) =>
        new() { Symbol = symbol, Credits = credits, StartingFaction = "COSMIC", ShipCount = 2 };

    private static CachedShip AddShip(SpaceTraders.Infrastructure.Persistence.SpaceTradersDbContext dbContext, string symbol, string waypoint = "X1-AB-001")
    {
        var ship = new CachedShip
        {
            Symbol = symbol,
            SystemSymbol = "X1-AB",
            WaypointSymbol = waypoint,
            Status = "DOCKED",
            FlightMode = "CRUISE",
            FuelCurrent = 80,
            FuelCapacity = 100,
            LastSyncedAt = DateTimeOffset.UtcNow
        };
        dbContext.Ships.Add(ship);
        dbContext.SaveChanges();
        return ship;
    }

    private static CachedAgent AddAgent(SpaceTraders.Infrastructure.Persistence.SpaceTradersDbContext dbContext, string symbol = "AGENT-1", long credits = 50_000)
    {
        var agent = new CachedAgent
        {
            Symbol = symbol,
            StartingFaction = "COSMIC",
            Credits = credits,
            ShipCount = 1,
            LastSyncedAt = DateTimeOffset.UtcNow
        };
        dbContext.Agents.Add(agent);
        dbContext.SaveChanges();
        return agent;
    }

    // ─── DockShipHandler ──────────────────────────────────────────────────────

    [Fact]
    public async Task DockShip_UpdatesStatusInCache()
    {
        var api = Substitute.For<ISpaceTradersApiClient>();
        api.DockShipAsync("SHIP-1", Arg.Any<CancellationToken>())
           .Returns(new ShipNavResult { Nav = MakeNav(status: "DOCKED") });

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");

        await new DockShipHandler(api, db, NullLogger<DockShipHandler>.Instance)
            .Handle(new DockShipCommand("SHIP-1"), CancellationToken.None);

        var cached = await db.Ships.FindAsync("SHIP-1");
        cached!.Status.Should().Be("DOCKED");
    }

    [Fact]
    public async Task DockShip_DoesNotIssueFollowUpGets()
    {
        var api = Substitute.For<ISpaceTradersApiClient>();
        api.DockShipAsync("SHIP-1", Arg.Any<CancellationToken>())
           .Returns(new ShipNavResult { Nav = MakeNav(status: "DOCKED") });

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");

        await new DockShipHandler(api, db, NullLogger<DockShipHandler>.Instance)
            .Handle(new DockShipCommand("SHIP-1"), CancellationToken.None);

        await api.Received(1).DockShipAsync("SHIP-1", Arg.Any<CancellationToken>());
        await api.DidNotReceive().GetMyShipAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await api.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
    }

    // ─── OrbitShipHandler ─────────────────────────────────────────────────────

    [Fact]
    public async Task OrbitShip_UpdatesStatusInCache()
    {
        var api = Substitute.For<ISpaceTradersApiClient>();
        api.OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>())
           .Returns(new ShipNavResult { Nav = MakeNav(status: "IN_ORBIT") });

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");

        await new OrbitShipHandler(api, db, NullLogger<OrbitShipHandler>.Instance)
            .Handle(new OrbitShipCommand("SHIP-1"), CancellationToken.None);

        var cached = await db.Ships.FindAsync("SHIP-1");
        cached!.Status.Should().Be("IN_ORBIT");
    }

    // ─── RefuelShipHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task RefuelShip_AppliesFuelAndCreditsFromResponse()
    {
        var api = Substitute.For<ISpaceTradersApiClient>();
        api.RefuelShipAsync("SHIP-1", Arg.Any<CancellationToken>())
           .Returns(new RefuelResult
           {
               Agent = MakeAgent(credits: 48_000),
               Fuel = new ShipFuel { Current = 100, Capacity = 100 },
               Transaction = new MarketTransaction
               {
                   WaypointSymbol = "X1-AB-001", ShipSymbol = "SHIP-1",
                   TradeSymbol = "FUEL", Type = "PURCHASE",
                   Units = 20, PricePerUnit = 100, TotalPrice = 2_000,
                   Timestamp = DateTimeOffset.UtcNow
               }
           });

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");
        AddAgent(db, credits: 50_000);

        await new RefuelShipHandler(api, db, NullLogger<RefuelShipHandler>.Instance)
            .Handle(new RefuelShipCommand("SHIP-1"), CancellationToken.None);

        var ship = await db.Ships.FindAsync("SHIP-1");
        ship!.FuelCurrent.Should().Be(100);

        var agent = await db.Agents.FindAsync("AGENT-1");
        agent!.Credits.Should().Be(48_000);
    }

    [Fact]
    public async Task RefuelShip_DoesNotIssueFollowUpGets()
    {
        var api = Substitute.For<ISpaceTradersApiClient>();
        api.RefuelShipAsync("SHIP-1", Arg.Any<CancellationToken>())
           .Returns(new RefuelResult
           {
               Agent = MakeAgent(credits: 48_000),
               Fuel = new ShipFuel { Current = 100, Capacity = 100 },
               Transaction = new MarketTransaction
               {
                   WaypointSymbol = "X1-AB-001", ShipSymbol = "SHIP-1",
                   TradeSymbol = "FUEL", Type = "PURCHASE",
                   Units = 20, PricePerUnit = 100, TotalPrice = 2_000,
                   Timestamp = DateTimeOffset.UtcNow
               }
           });

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");
        AddAgent(db);

        await new RefuelShipHandler(api, db, NullLogger<RefuelShipHandler>.Instance)
            .Handle(new RefuelShipCommand("SHIP-1"), CancellationToken.None);

        await api.Received(1).RefuelShipAsync("SHIP-1", Arg.Any<CancellationToken>());
        await api.DidNotReceive().GetMyShipAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await api.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
    }

    // ─── BuyCargoHandler ──────────────────────────────────────────────────────

    [Fact]
    public async Task BuyCargo_AppliesCargoAndCreditsFromResponse()
    {
        var api = Substitute.For<ISpaceTradersApiClient>();
        api.BuyCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
           .Returns(new BuyCargoResult
           {
               Agent = MakeAgent(credits: 45_000),
               Cargo = new ShipCargo { Capacity = 60, Units = 10 },
               Transaction = new MarketTransaction
               {
                   WaypointSymbol = "X1-AB-001", ShipSymbol = "SHIP-1",
                   TradeSymbol = "IRON_ORE", Type = "PURCHASE",
                   Units = 10, PricePerUnit = 500, TotalPrice = 5_000,
                   Timestamp = DateTimeOffset.UtcNow
               }
           });

        await using var db = TestDbContextFactory.Create();
        AddShip(db, "SHIP-1");
        AddAgent(db, credits: 50_000);

        await new BuyCargoHandler(api, db, NullLogger<BuyCargoHandler>.Instance)
            .Handle(new BuyCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        var ship = await db.Ships.FindAsync("SHIP-1");
        ship!.CargoCurrent.Should().Be(10);
        ship.CargoCapacity.Should().Be(60);

        var agent = await db.Agents.FindAsync("AGENT-1");
        agent!.Credits.Should().Be(45_000);
    }

    // ─── AssignShipHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task AssignShip_CreatesNewRecord_WhenNoneExists()
    {
        await using var db = TestDbContextFactory.Create();

        await new AssignShipHandler(db, NullLogger<AssignShipHandler>.Instance)
            .Handle(new AssignShipCommand("SHIP-1", "TRADE", "X1-AB-001", "X1-AB-002", "IRON_ORE"), CancellationToken.None);

        var record = await db.ShipAssignments.FindAsync("SHIP-1");
        record.Should().NotBeNull();
        record!.Type.Should().Be("TRADE");
        record.CargoSymbol.Should().Be("IRON_ORE");
        record.StepIndex.Should().Be(0);
        record.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task AssignShip_ReplacesExistingRecord()
    {
        await using var db = TestDbContextFactory.Create();
        db.ShipAssignments.Add(new ShipAssignmentRecord
        {
            ShipSymbol = "SHIP-1",
            Type = "IDLE",
            StepIndex = 3,
            AssignedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        await new AssignShipHandler(db, NullLogger<AssignShipHandler>.Instance)
            .Handle(new AssignShipCommand("SHIP-1", "MINE"), CancellationToken.None);

        var record = await db.ShipAssignments.FindAsync("SHIP-1");
        record!.Type.Should().Be("MINE");
        record.StepIndex.Should().Be(0);
    }
}
