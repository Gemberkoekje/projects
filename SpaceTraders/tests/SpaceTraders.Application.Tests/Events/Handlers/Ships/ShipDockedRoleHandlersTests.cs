using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipDockedRoleHandlersTests
{
    [Fact]
    public async Task MinerDockedHandler_SellsCargo_RefuelsWhenLow_AndUndocks()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var docked = Substitute.For<IDockedCommandAcceptor>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new SpaceTraders.Application.DTOs.ShipAssignmentDto(
                "SHIP-1",
                "Mine",
                "X1-AB-AST",
                "X1-AB-MKT",
                "IRON_ORE",
                null,
                0,
                DateTimeOffset.UtcNow,
                null,
                0));

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel(
                "SHIP-1",
                "X1-AB",
                "X1-AB-MKT",
                "DOCKED",
                "CRUISE",
                10,
                100,
                CargoCurrent: 25,
                CargoCapacity: 40,
                CargoInventory: [new CargoItemModel("IRON_ORE", 25)]));

        var handler = new ShipMinerDockedEventHandler(assignments, ships, docked);
        var @event = new ShipDockedEvent("SHIP-1", "X1-AB", "X1-AB-MKT", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow);

        var result = await handler.HandleAsync(@event, CancellationToken.None);

        result.Should().NotBeNull();
        await docked.Received(1).SellCargoAsync("SHIP-1", "IRON_ORE", 25, Arg.Any<CancellationToken>());
        await docked.Received(1).RefuelAsync("SHIP-1", Arg.Any<CancellationToken>());
        await docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TraderDockedHandler_BuysAtOrigin_ThenUndocks()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var docked = Substitute.For<IDockedCommandAcceptor>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new SpaceTraders.Application.DTOs.ShipAssignmentDto(
                "SHIP-1",
                "Trade",
                "X1-AB-BUY",
                "X1-AB-SELL",
                "IRON_ORE",
                null,
                0,
                DateTimeOffset.UtcNow,
                null,
                0));

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-BUY", "DOCKED", "CRUISE", 100, 100, CargoCurrent: 0, CargoCapacity: 30));

        var handler = new ShipTraderDockedEventHandler(assignments, ships, docked);
        var @event = new ShipDockedEvent("SHIP-1", "X1-AB", "X1-AB-BUY", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow);

        var result = await handler.HandleAsync(@event, CancellationToken.None);

        result.Should().NotBeNull();
        await docked.Received(1).BuyCargoAsync("SHIP-1", "IRON_ORE", 30, Arg.Any<CancellationToken>());
        await docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScoutDockedHandler_RefreshesAndUndocks()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var waypointVisit = Substitute.For<IWaypointVisitService>();
        var markets = Substitute.For<IMarketRefreshService>();
        var shipyards = Substitute.For<IShipyardRefreshService>();
        var docked = Substitute.For<IDockedCommandAcceptor>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new SpaceTraders.Application.DTOs.ShipAssignmentDto(
                "SHIP-1",
                "Scout",
                "X1-AB-SCOUT",
                null,
                null,
                null,
                0,
                DateTimeOffset.UtcNow,
                null,
                0));

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-SCOUT", "DOCKED", "CRUISE", 100, 100));

        var handler = new ShipScoutDockedEventHandler(assignments, ships, waypointVisit, markets, shipyards, docked);
        var @event = new ShipDockedEvent("SHIP-1", "X1-AB", "X1-AB-SCOUT", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow);

        var result = await handler.HandleAsync(@event, CancellationToken.None);

        result.Should().NotBeNull();
        await waypointVisit.Received(1).MarkVisitedAsync("X1-AB-SCOUT", Arg.Any<CancellationToken>());
        await markets.Received(1).RefreshIfApplicableAsync("X1-AB-SCOUT", Arg.Any<CancellationToken>());
        await shipyards.Received(1).RefreshIfApplicableAsync("X1-AB-SCOUT", Arg.Any<CancellationToken>());
        await docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }
}
