using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipDockedMineEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_SellsSurplus_ButKeepsContractCargo_AndHydrocarbonReserve()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var settings = Substitute.For<ISettingsRepository>();
        var docked = Substitute.For<IDockedCommandAcceptor>();
        var bus = Substitute.For<IMessageBus>();

        settings.GetAsync<int>("Mining.ReserveHydrocarbonUnits", Arg.Any<CancellationToken>()).Returns(10);
        settings.GetAsync<int>("Mining.MinimumSellPriceToKeepCargo", Arg.Any<CancellationToken>()).Returns(25);
        settings.GetAsync<bool>("Mining.JettisonLowValueWhenFull", Arg.Any<CancellationToken>()).Returns(true);
        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", "IRON_ORE", null, 0, DateTimeOffset.UtcNow, null));
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel(
                "SHIP-1",
                "X1-AB",
                "X1-AB-MKT",
                "DOCKED",
                "CRUISE",
                10,
                100,
                CargoCurrent: 35,
                CargoCapacity: 40,
                CargoInventory:
                [
                    new CargoItemModel("IRON_ORE", 12),
                    new CargoItemModel("COPPER_ORE", 8),
                    new CargoItemModel("HYDROCARBON", 15)
                ]));
        contracts.GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns([new ContractDto("C1", "COSMIC", "PROCUREMENT", true, false, null, null, DateTimeOffset.UtcNow.AddHours(1), "[{\"TradeSymbol\":\"IRON_ORE\",\"DestinationSymbol\":\"X1-AB-HQ\",\"UnitsRequired\":20,\"UnitsFulfilled\":5}]")]);
        markets.FindSnapshotByWaypointAsync("X1-AB-MKT", Arg.Any<CancellationToken>())
            .Returns(new MarketSnapshot("X1-AB-MKT", "X1-AB",
                [
                    new TradeGoodSnapshot("COPPER_ORE", "EXPORT", 10, 50, 10, "HIGH"),
                    new TradeGoodSnapshot("HYDROCARBON", "EXPORT", 10, 40, 10, "HIGH")
                ], [], [], []));

        var handler = new ShipDockedMineEventHandler(assignments, ships, contracts, markets, settings, docked, bus);
        var result = await handler.HandleAsync(new ShipDockedEvent("SHIP-1", "X1-AB", "X1-AB-MKT", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow), CancellationToken.None);

        result.Should().BeSameAs(ChainOfCommandHandlerResult.Handled());
        await docked.DidNotReceive().SellCargoAsync("SHIP-1", "IRON_ORE", Arg.Any<int>(), Arg.Any<CancellationToken>());
        await docked.Received(1).SellCargoAsync("SHIP-1", "COPPER_ORE", 8, Arg.Any<CancellationToken>());
        await docked.Received(1).SellCargoAsync("SHIP-1", "HYDROCARBON", 5, Arg.Any<CancellationToken>());
        await docked.Received(1).RefuelAsync("SHIP-1", true, Arg.Any<CancellationToken>());
        await docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_JettisonsLowValueCargo_WhenFull_AndNotProtected()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var settings = Substitute.For<ISettingsRepository>();
        var docked = Substitute.For<IDockedCommandAcceptor>();
        var bus = Substitute.For<IMessageBus>();

        settings.GetAsync<int>("Mining.ReserveHydrocarbonUnits", Arg.Any<CancellationToken>()).Returns(10);
        settings.GetAsync<int>("Mining.MinimumSellPriceToKeepCargo", Arg.Any<CancellationToken>()).Returns(25);
        settings.GetAsync<bool>("Mining.JettisonLowValueWhenFull", Arg.Any<CancellationToken>()).Returns(true);
        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", "IRON_ORE", null, 0, DateTimeOffset.UtcNow, null));
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel(
                "SHIP-1",
                "X1-AB",
                "X1-AB-MKT",
                "DOCKED",
                "CRUISE",
                50,
                100,
                CargoCurrent: 40,
                CargoCapacity: 40,
                CargoInventory:
                [
                    new CargoItemModel("SAND", 12)
                ]));
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        markets.FindSnapshotByWaypointAsync("X1-AB-MKT", Arg.Any<CancellationToken>())
            .Returns(new MarketSnapshot("X1-AB-MKT", "X1-AB", [], [], [], []));

        var handler = new ShipDockedMineEventHandler(assignments, ships, contracts, markets, settings, docked, bus);
        var result = await handler.HandleAsync(new ShipDockedEvent("SHIP-1", "X1-AB", "X1-AB-MKT", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow), CancellationToken.None);

        result.Should().BeSameAs(ChainOfCommandHandlerResult.Handled());
        await bus.Received(1).InvokeAsync(Arg.Is<JettisonCargoCommand>(c => c.ShipSymbol == "SHIP-1" && c.TradeSymbol == "SAND" && c.Units == 12), Arg.Any<CancellationToken>());
        await docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }
}
