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
    public async Task TraderDockedHandler_BuysAndOrbits_WhenAtBuyWaypoint()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var docked = Substitute.For<IDockedCommandAcceptor>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new SpaceTraders.Application.DTOs.ShipAssignmentDto("SHIP-1", "Trade", "X1-AB-BUY", "X1-AB-SELL", "IRON_ORE", null, 0, DateTimeOffset.UtcNow, null, 0));
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-BUY", "DOCKED", "CRUISE", 100, 100, CargoCurrent: 0, CargoCapacity: 30));

        var handler = new ShipDockedTraderEventHandler(assignments, ships, docked);
        await handler.HandleAsync(new ShipDockedEvent("SHIP-1", "X1-AB", "X1-AB-BUY", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow), CancellationToken.None);

        await docked.Received(1).BuyCargoAsync("SHIP-1", "IRON_ORE", 30, Arg.Any<CancellationToken>());
        await docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScoutDockedHandler_Undocks_WhenNotAtTarget()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var waypointVisit = Substitute.For<IWaypointVisitService>();
        var markets = Substitute.For<IMarketRefreshService>();
        var shipyards = Substitute.For<IShipyardRefreshService>();
        var docked = Substitute.For<IDockedCommandAcceptor>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new SpaceTraders.Application.DTOs.ShipAssignmentDto("SHIP-1", "Scout", "X1-AB-SCOUT", null, null, null, 0, DateTimeOffset.UtcNow, null, 0));
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-HOME", "DOCKED", "CRUISE", 100, 100));

        var handler = new ShipDockedScoutEventHandler(assignments, ships, waypointVisit, markets, shipyards, docked);
        await handler.HandleAsync(new ShipDockedEvent("SHIP-1", "X1-AB", "X1-AB-HOME", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow), CancellationToken.None);

        await docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }
}

