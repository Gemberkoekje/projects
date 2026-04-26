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

public sealed class NavigateShipHandlerTests
{
    [Fact]
    public async Task NavigateShip_WhenDocked_OrbitsBeforeNavigation()
    {
        var arrival = DateTimeOffset.UtcNow.AddMinutes(3);
        var port = Substitute.For<ISpaceTradersPort>();
        port.OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new NavModel("IN_ORBIT", "X1-AB", "X1-AB-001", "CRUISE", null, null));
        port.NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>())
            .Returns(new NavigateActionResult(
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-001", "CRUISE", "X1-AB-002", arrival),
                new FuelModel(70, 100)));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = "SHIP-1",
            SystemSymbol = "X1-AB",
            WaypointSymbol = "X1-AB-001",
            Status = "DOCKED",
            FlightMode = "CRUISE",
            FuelCurrent = 100,
            FuelCapacity = 100,
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);

        await new NavigateShipHandler(port, ships, bus, NullLogger<NavigateShipHandler>.Instance)
            .Handle(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        Received.InOrder(async () =>
        {
            await port.OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>());
            await port.NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task NavigateShip_UpdatesNavAndArrivesAt_FromResponse()
    {
        var arrival = DateTimeOffset.UtcNow.AddMinutes(3);
        var port = Substitute.For<ISpaceTradersPort>();
        port.NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>())
            .Returns(new NavigateActionResult(
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-001", "CRUISE", "X1-AB-002", arrival),
                new FuelModel(70, 100)));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = "SHIP-1",
            SystemSymbol = "X1-AB",
            WaypointSymbol = "X1-AB-001",
            Status = "IN_ORBIT",
            FlightMode = "CRUISE",
            FuelCurrent = 100,
            FuelCapacity = 100,
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);

        await new NavigateShipHandler(port, ships, bus, NullLogger<NavigateShipHandler>.Instance)
            .Handle(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        var cached = await db.Ships.FindAsync(TestDbContextFactory.AgentToken, "SHIP-1");
        cached!.Status.Should().Be("IN_TRANSIT");
        cached.ArrivesAt.Should().BeCloseTo(arrival, TimeSpan.FromSeconds(1));
        cached.DestWaypointSymbol.Should().Be("X1-AB-002");
        cached.FuelCurrent.Should().Be(70);
    }

    [Fact]
    public async Task NavigateShip_DoesNotIssueFollowUpGets()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>())
            .Returns(new NavigateActionResult(
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-001", "CRUISE", "X1-AB-002", DateTimeOffset.UtcNow.AddMinutes(3)),
                null));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip { AgentToken = TestDbContextFactory.AgentToken, Symbol = "SHIP-1", LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);

        await new NavigateShipHandler(port, ships, bus, NullLogger<NavigateShipHandler>.Instance)
            .Handle(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        await port.Received(1).NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>());
        await port.DidNotReceive().OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NavigateShip_PublishesShipMovingEvent()
    {
        var arrival = DateTimeOffset.UtcNow.AddMinutes(3);
        var port = Substitute.For<ISpaceTradersPort>();
        port.NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>())
            .Returns(new NavigateActionResult(
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-001", "CRUISE", "X1-AB-002", arrival),
                new FuelModel(70, 100)));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = "SHIP-1",
            SystemSymbol = "X1-AB",
            WaypointSymbol = "X1-AB-001",
            Status = "IN_ORBIT",
            FlightMode = "CRUISE",
            FuelCurrent = 100,
            FuelCapacity = 100,
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);

        await new NavigateShipHandler(port, ships, bus, NullLogger<NavigateShipHandler>.Instance)
            .Handle(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipMovingEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.OriginWaypointSymbol == "X1-AB-001" &&
                e.DestinationWaypointSymbol == "X1-AB-002" &&
                e.ArrivalTime == arrival &&
                e.FuelConsumed == 30),
            Arg.Any<DeliveryOptions>());
    }
}
