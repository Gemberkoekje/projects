using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipDockedFuelEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_Refuels_WhenDockedAtFuelMarket_AndNotFull()
    {
        var ships = Substitute.For<IShipRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var docked = Substitute.For<IDockedCommandAcceptor>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE", 10, 100));
        waypoints.FindAsync("X1-AB-MKT", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-MKT", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow));
        markets.FindSnapshotByWaypointAsync("X1-AB-MKT", Arg.Any<CancellationToken>())
            .Returns(new MarketSnapshot(
                "X1-AB-MKT",
                "X1-AB",
                [new TradeGoodSnapshot("FUEL", "EXCHANGE", 10, 12, 100, "HIGH")],
                ["FUEL"],
                [],
                ["FUEL"]));

        var handler = new ShipDockedFuelEventHandler(ships, waypoints, markets, docked, NullLogger<ShipDockedFuelEventHandler>.Instance);

        var result = await handler.HandleAsync(
            new ShipDockedEvent("SHIP-1", "X1-AB", "X1-AB-MKT", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow),
            CancellationToken.None);

        result.Should().BeSameAs(ChainOfCommandHandlerResult.Handled());
        await docked.Received(1).RefuelAsync("SHIP-1", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Skips_WhenFuelMarketUnavailable()
    {
        var ships = Substitute.For<IShipRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var docked = Substitute.For<IDockedCommandAcceptor>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE", 10, 100));
        waypoints.FindAsync("X1-AB-MKT", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-MKT", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow));
        markets.FindSnapshotByWaypointAsync("X1-AB-MKT", Arg.Any<CancellationToken>())
            .Returns(new MarketSnapshot(
                "X1-AB-MKT",
                "X1-AB",
                [new TradeGoodSnapshot("IRON_ORE", "EXPORT", 10, 12, 100, "HIGH")],
                [],
                ["IRON_ORE"],
                []));

        var handler = new ShipDockedFuelEventHandler(ships, waypoints, markets, docked, NullLogger<ShipDockedFuelEventHandler>.Instance);

        var result = await handler.HandleAsync(
            new ShipDockedEvent("SHIP-1", "X1-AB", "X1-AB-MKT", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow),
            CancellationToken.None);

        result.Should().BeSameAs(ChainOfCommandHandlerResult.Skipped());
        await docked.DidNotReceive().RefuelAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
