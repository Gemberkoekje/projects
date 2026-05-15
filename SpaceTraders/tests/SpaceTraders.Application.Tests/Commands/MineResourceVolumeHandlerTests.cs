using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class MineResourceVolumeHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_ExtractsAndJettisonsNonTargetCargo_WhenAtSourceInOrbit()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var refuel = Substitute.For<IRefuelSubCommand>();
        var orbit = Substitute.For<IOrbitSubCommand>();
        var navigate = Substitute.For<INavigateSubCommand>();
        var bus = Substitute.For<Wolverine.IMessageBus>();

        var ship = new ShipModel(
            Symbol: "SHIP-1",
            SystemSymbol: "X1-AB",
            WaypointSymbol: "X1-AB-AST",
            Status: "IN_ORBIT",
            FlightMode: "CRUISE",
            FuelCurrent: 100,
            FuelCapacity: 100,
            CargoCurrent: 5,
            CargoCapacity: 40,
            CargoInventory:
            [
                new CargoItemModel("ICE_WATER", 5),
            ]);

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship, ship with
        {
            CargoInventory =
            [
                new CargoItemModel("IRON_ORE", 8),
                new CargoItemModel("ICE_WATER", 2),
            ],
            CargoCurrent = 10,
            CooldownExpiresAt = DateTimeOffset.UtcNow.AddSeconds(10),
        });

        port.ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ExtractionActionResult(
                YieldSymbol: "IRON_ORE",
                YieldUnits: 8,
                Cargo: new CargoModel(10, 40,
                [
                    new CargoItemModel("IRON_ORE", 8),
                    new CargoItemModel("ICE_WATER", 2),
                ]),
                CooldownSeconds: 10,
                CooldownExpiresAt: DateTimeOffset.UtcNow.AddSeconds(10)));

        port.JettisonCargoAsync("SHIP-1", "ICE_WATER", 2, Arg.Any<CancellationToken>())
            .Returns(new JettisonActionResult(new CargoModel(8, 40, [new CargoItemModel("IRON_ORE", 8)])));

        var sut = new MineResourceVolumeHandler(port, ships, waypoints, refuel, orbit, navigate, bus, NullLogger<MineResourceVolumeHandler>.Instance);

        var result = await sut.ExecuteAsync(new MineResourceVolumeCommand("SHIP-1", "IRON_ORE", "X1-AB-AST", 20), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await port.Received(1).ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>());
        await port.Received(1).JettisonCargoAsync("SHIP-1", "ICE_WATER", 2, Arg.Any<CancellationToken>());
        await navigate.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_NavigatesToSource_WhenNotAtSource()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var refuel = Substitute.For<IRefuelSubCommand>();
        var orbit = Substitute.For<IOrbitSubCommand>();
        var navigate = Substitute.For<INavigateSubCommand>();
        var bus = Substitute.For<Wolverine.IMessageBus>();

        var ship = new ShipModel(
            Symbol: "SHIP-2",
            SystemSymbol: "X1-AB",
            WaypointSymbol: "X1-AB-MKT",
            Status: "IN_ORBIT",
            FlightMode: "CRUISE",
            FuelCurrent: 100,
            FuelCapacity: 100,
            CargoCurrent: 0,
            CargoCapacity: 40);

        ships.FindAsync("SHIP-2", Arg.Any<CancellationToken>()).Returns(ship);

        var sut = new MineResourceVolumeHandler(port, ships, waypoints, refuel, orbit, navigate, bus, NullLogger<MineResourceVolumeHandler>.Instance);

        var result = await sut.ExecuteAsync(new MineResourceVolumeCommand("SHIP-2", "IRON_ORE", "X1-AB-AST", 15), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await navigate.Received(1).ExecuteAsync("SHIP-2", "X1-AB-AST", Guid.Empty, Arg.Any<CancellationToken>());
        await port.DidNotReceiveWithAnyArgs().ExtractResourcesAsync(default!, default);
    }
}
