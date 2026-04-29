using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipInOrbitFuelRecoveryEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_PatchesToDrift_AndNavigatesToFuelMarket_WhenFuelIsZero()
    {
        var ships = Substitute.For<IShipRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 0, 100));
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                new WaypointCacheModel("X1-AB-001", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow),
                new WaypointCacheModel("X1-AB-002", "X1-AB", "PLANET", 5, 5, true, false, DateTimeOffset.UtcNow)
            ]);
        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                new MarketSnapshot(
                    "X1-AB-002",
                    "X1-AB",
                    [new TradeGoodSnapshot("FUEL", "EXCHANGE", 10, 12, 100, "HIGH")],
                    ["FUEL"],
                    [],
                    ["FUEL"])
            ]);

        var handler = new ShipInOrbitFuelRecoveryEventHandler(ships, waypoints, markets, inOrbit, bus, NullLogger<ShipInOrbitFuelRecoveryEventHandler>.Instance);

        var result = await handler.HandleAsync(
            new ShipInOrbitEvent("SHIP-1", "X1-AB", "X1-AB-001", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow),
            CancellationToken.None);

        result.Should().BeSameAs(ChainOfCommandHandlerResult.Handled());
        await bus.Received(1).InvokeAsync(Arg.Is<PatchShipNavCommand>(c => c.ShipSymbol == "SHIP-1" && c.FlightMode == "DRIFT"), Arg.Any<CancellationToken>());
        await inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Docks_WhenAlreadyAtFuelMarket()
    {
        var ships = Substitute.For<IShipRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-002", "IN_ORBIT", "DRIFT", 0, 100));
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                new WaypointCacheModel("X1-AB-002", "X1-AB", "PLANET", 5, 5, true, false, DateTimeOffset.UtcNow)
            ]);
        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                new MarketSnapshot(
                    "X1-AB-002",
                    "X1-AB",
                    [new TradeGoodSnapshot("FUEL", "EXCHANGE", 10, 12, 100, "HIGH")],
                    ["FUEL"],
                    [],
                    ["FUEL"])
            ]);

        var handler = new ShipInOrbitFuelRecoveryEventHandler(ships, waypoints, markets, inOrbit, bus, NullLogger<ShipInOrbitFuelRecoveryEventHandler>.Instance);

        var result = await handler.HandleAsync(
            new ShipInOrbitEvent("SHIP-1", "X1-AB", "X1-AB-002", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow),
            CancellationToken.None);

        result.Should().BeSameAs(ChainOfCommandHandlerResult.Handled());
        await bus.DidNotReceive().InvokeAsync(Arg.Any<PatchShipNavCommand>(), Arg.Any<CancellationToken>());
        await inOrbit.Received(1).DockAsync("SHIP-1", Arg.Any<CancellationToken>());
    }
}
