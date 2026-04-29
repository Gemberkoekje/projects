using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class Phase1CommandHandlerTests
{
    // GetShipCargo

    [Fact]
    public async Task GetShipCargo_CallsPort_AndUpdatesCargo()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var cargo = new CargoModel(10, 40, [new CargoItemModel("IRON_ORE", 10)]);
        port.GetShipCargoAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(cargo);

        var handler = new GetShipCargoHandler(port, ships, NullLogger<GetShipCargoHandler>.Instance);
        await handler.Handle(new GetShipCargoCommand("SHIP-1"), CancellationToken.None);

        await ships.Received(1).UpdateCargoAsync("SHIP-1", cargo, Arg.Any<CancellationToken>());
    }

    // JettisonCargo

    [Fact]
    public async Task JettisonCargo_CallsPort_AndUpdatesCargo()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var cargo = new CargoModel(0, 40);
        port.JettisonCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new JettisonActionResult(cargo));

        var handler = new JettisonCargoHandler(port, ships, NullLogger<JettisonCargoHandler>.Instance);
        await handler.Handle(new JettisonCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        await ships.Received(1).UpdateCargoAsync("SHIP-1", cargo, Arg.Any<CancellationToken>());
    }

    // PatchShipNav

    [Fact]
    public async Task PatchShipNav_CallsPort_AndUpdatesNav()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var nav = new NavModel("IN_ORBIT", "X1-AB", "X1-AB-001", "BURN", null, null);
        port.PatchShipNavAsync("SHIP-1", "BURN", Arg.Any<CancellationToken>()).Returns(nav);

        var handler = new PatchShipNavHandler(port, ships, NullLogger<PatchShipNavHandler>.Instance);
        await handler.Handle(new PatchShipNavCommand("SHIP-1", "BURN"), CancellationToken.None);

        await ships.Received(1).UpdateNavAsync("SHIP-1", nav, null, Arg.Any<CancellationToken>());
    }

    // Survey

    [Fact]
    public async Task Survey_WhenNotInOrbit_PublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var surveys = Substitute.For<ISurveyRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));

        var handler = new SurveyHandler(port, ships, surveys, bus, NullLogger<SurveyHandler>.Instance);
        await handler.Handle(new SurveyCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
        await port.DidNotReceive().SurveyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Survey_WhenInOrbit_CallsPort()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var surveys = Substitute.For<ISurveyRepository>();
        var bus = Substitute.For<IMessageBus>();
        var result = new SurveyActionResult([], 70);
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));
        port.SurveyAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(result);

        var handler = new SurveyHandler(port, ships, surveys, bus, NullLogger<SurveyHandler>.Instance);
        await handler.Handle(new SurveyCommand("SHIP-1"), CancellationToken.None);

        await port.Received(1).SurveyAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    // Siphon

    [Fact]
    public async Task SiphonResources_WhenNotInOrbit_PublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));

        var handler = new SiphonResourcesHandler(port, ships, waypoints, bus, NullLogger<SiphonResourcesHandler>.Instance);
        await handler.Handle(new SiphonResourcesCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
        await port.DidNotReceive().SiphonResourcesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SiphonResources_WhenInOrbit_CallsPort_AndUpdatesCargo()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-GG", "IN_ORBIT", "CRUISE", 80, 100, ModulesJson: "MODULE_GAS_PROCESSOR_I", MountSymbols: ["MOUNT_GAS_SIPHON_I"]));
        waypoints.FindAsync("X1-AB-GG", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-GG", "X1-AB", "GAS_GIANT", 0, 0, false, false, DateTimeOffset.UtcNow));
        var cargo = new CargoModel(5, 40);
        port.SiphonResourcesAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new SiphonActionResult("HYDROCARBON", 5, cargo, 60));

        var handler = new SiphonResourcesHandler(port, ships, waypoints, bus, NullLogger<SiphonResourcesHandler>.Instance);
        await handler.Handle(new SiphonResourcesCommand("SHIP-1"), CancellationToken.None);

        await port.Received(1).SiphonResourcesAsync("SHIP-1", Arg.Any<CancellationToken>());
        await ships.Received(1).UpdateCargoAsync("SHIP-1", cargo, Arg.Any<CancellationToken>());
    }

    // Warp

    [Fact]
    public async Task WarpShip_WhenNotInOrbit_PublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));

        var handler = new WarpShipHandler(port, ships, bus, NullLogger<WarpShipHandler>.Instance);
        await handler.Handle(new WarpShipCommand("SHIP-1", "X1-CD-001"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
        await port.DidNotReceive().WarpShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WarpShip_WhenInOrbit_CallsPort_AndPublishesInTransit()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));
        var arrival = DateTimeOffset.UtcNow.AddMinutes(5);
        port.WarpShipAsync("SHIP-1", "X1-CD-001", Arg.Any<CancellationToken>())
            .Returns(new WarpActionResult(
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-001", "CRUISE", "X1-CD-001", arrival),
                new FuelModel(50, 100)));

        var handler = new WarpShipHandler(port, ships, bus, NullLogger<WarpShipHandler>.Instance);
        await handler.Handle(new WarpShipCommand("SHIP-1", "X1-CD-001"), CancellationToken.None);

        await port.Received(1).WarpShipAsync("SHIP-1", "X1-CD-001", Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Any<ShipInTransitEvent>(), Arg.Any<DeliveryOptions>());
    }

    // Jump

    [Fact]
    public async Task JumpShip_WhenNotInOrbit_PublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));

        var handler = new JumpShipHandler(port, ships, bus, NullLogger<JumpShipHandler>.Instance);
        await handler.Handle(new JumpShipCommand("SHIP-1", "X1-CD"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
        await port.DidNotReceive().JumpShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JumpShip_WhenInOrbit_CallsPort_AndPublishesInTransit()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));
        var arrival = DateTimeOffset.UtcNow.AddSeconds(5);
        port.JumpShipAsync("SHIP-1", "X1-CD", Arg.Any<CancellationToken>())
            .Returns(new JumpActionResult(
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-001", "CRUISE", "X1-CD-001", arrival),
                60));

        var handler = new JumpShipHandler(port, ships, bus, NullLogger<JumpShipHandler>.Instance);
        await handler.Handle(new JumpShipCommand("SHIP-1", "X1-CD"), CancellationToken.None);

        await port.Received(1).JumpShipAsync("SHIP-1", "X1-CD", Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Any<ShipInTransitEvent>(), Arg.Any<DeliveryOptions>());
    }

    // Chart

    [Fact]
    public async Task CreateChart_CallsPort()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        port.CreateChartAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ChartActionResult("X1-AB-001", "ASTEROID"));

        var handler = new CreateChartHandler(port, ships, NullLogger<CreateChartHandler>.Instance);
        await handler.Handle(new CreateChartCommand("SHIP-1"), CancellationToken.None);

        await port.Received(1).CreateChartAsync("SHIP-1", Arg.Any<CancellationToken>());
    }
}
