using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using static SpaceTraders.Application.Tests.Siphoning.SiphonFixture;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>
/// Slice 6.7: one siphon at a gas giant, without a survey (the API's siphon call takes none), keeping every gas
/// a market buys (D33). What it yields counts as extracted units for the dashboard, but not as an extraction: the
/// survey statistics count extractions with and without a survey.
/// </summary>
public sealed class SiphonResourcesHandlerTests
{
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly Wolverine.IMessageBus _bus = Substitute.For<Wolverine.IMessageBus>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly LogRecorder _log = new();

    public SiphonResourcesHandlerTests()
    {
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _waypoints.FindAsync(C38, Arg.Any<CancellationToken>()).Returns(Waypoints.Single(waypoint => waypoint.Symbol == C38));
    }

    [Fact]
    public async Task InOrbitAtTheGasGiant_ItSiphons_KeepsEveryGas_AndStartsTheCooldown()
    {
        var cooldown = DateTimeOffset.UtcNow.AddSeconds(70);
        Ship(SiphonDrone(waypoint: C38, status: "IN_ORBIT", cargo: [new CargoItemModel("HYDROCARBON", 4)]));
        _port.SiphonResourcesAsync("SHIP-5", Arg.Any<CancellationToken>()).Returns(new SiphonActionResult(
            "LIQUID_NITROGEN",
            5,
            new CargoModel(9, 15, [new CargoItemModel("HYDROCARBON", 4), new CargoItemModel("LIQUID_NITROGEN", 5)]),
            70,
            cooldown));

        var result = await Handler().ExecuteAsync(new SiphonResourcesCommand("SHIP-5", "LIQUID_HYDROGEN", C38), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.CargoCurrent.Should().Be(9);
        await _ships.Received(1).UpdateCargoAsync("SHIP-5", Arg.Is<CargoModel>(cargo => cargo.Units == 9), Arg.Any<CancellationToken>());
        await _ships.Received(1).UpdateCooldownAsync("SHIP-5", cooldown, Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().JettisonCargoAsync(default!, default!, default, default);
        await _port.DidNotReceiveWithAnyArgs().ExtractResourcesAsync(default!, default);

        // What the dashboard shows as extracted; not an extraction for the survey statistics.
        _metrics.Received(1).Extracted("SHIP-5", "LIQUID_NITROGEN", 5);
        _metrics.DidNotReceiveWithAnyArgs().Extraction(default!, default);

        var siphoned = _log.Journal.Should().ContainSingle().Subject;
        siphoned.EventKind.Should().Be("Siphoned");
        siphoned.Properties["TradeSymbol"].Should().Be("LIQUID_NITROGEN");
        siphoned.Properties["Target"].Should().Be("LIQUID_HYDROGEN");
    }

    [Fact]
    public async Task WhatNoMarketBuys_IsJettisoned()
    {
        // D33 keeps what can be sold; anything else would fill the hold for good.
        Ship(SiphonDrone(waypoint: C38, status: "IN_ORBIT"));
        _port.SiphonResourcesAsync("SHIP-5", Arg.Any<CancellationToken>()).Returns(new SiphonActionResult(
            "LIQUID_HYDROGEN",
            5,
            new CargoModel(8, 15, [new CargoItemModel("LIQUID_HYDROGEN", 5), new CargoItemModel("EXOTIC_MATTER", 3)]),
            70));
        _port.JettisonCargoAsync("SHIP-5", "EXOTIC_MATTER", 3, Arg.Any<CancellationToken>())
            .Returns(new JettisonActionResult(new CargoModel(5, 15, [new CargoItemModel("LIQUID_HYDROGEN", 5)])));

        var result = await Handler().ExecuteAsync(new SiphonResourcesCommand("SHIP-5", "LIQUID_HYDROGEN", C38), CancellationToken.None);

        result.CargoCurrent.Should().Be(5);
        await _port.Received(1).JettisonCargoAsync("SHIP-5", "EXOTIC_MATTER", 3, Arg.Any<CancellationToken>());
        _metrics.Received(1).Jettisoned("SHIP-5", "EXOTIC_MATTER", 3);
    }

    [Fact]
    public async Task AGasThatOnlyAMarketBeyondItsReachBuys_IsJettisoned()
    {
        // Only F48 buys nitrogen here, and a drone's 80-unit tank doesn't get there from C38 in this fixture.
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(
            Market(C39, Good("HYDROCARBON", "EXCHANGE", 70, 60, 60, "MODERATE"), Good("FUEL", "EXCHANGE", 80, 70, 180, "MODERATE")),
            Market(C40, Good("FUEL", "EXCHANGE", 75, 66, 180, "MODERATE")),
            Market(F48, Good("LIQUID_NITROGEN", "IMPORT", 120, 60, 60, "SCARCE"), Good("FUEL", "EXCHANGE", 79, 70, 180, "MODERATE"))));
        Ship(SiphonDrone(waypoint: C38, status: "IN_ORBIT"));
        _port.SiphonResourcesAsync("SHIP-5", Arg.Any<CancellationToken>()).Returns(new SiphonActionResult(
            "LIQUID_NITROGEN",
            5,
            new CargoModel(9, 15, [new CargoItemModel("HYDROCARBON", 4), new CargoItemModel("LIQUID_NITROGEN", 5)]),
            70));
        _port.JettisonCargoAsync("SHIP-5", "LIQUID_NITROGEN", 5, Arg.Any<CancellationToken>())
            .Returns(new JettisonActionResult(new CargoModel(4, 15, [new CargoItemModel("HYDROCARBON", 4)])));

        var result = await Handler().ExecuteAsync(new SiphonResourcesCommand("SHIP-5", "HYDROCARBON", C38), CancellationToken.None);

        result.CargoCurrent.Should().Be(4);
        await _port.Received(1).JettisonCargoAsync("SHIP-5", "LIQUID_NITROGEN", 5, Arg.Any<CancellationToken>());
        await _port.DidNotReceive().JettisonCargoAsync("SHIP-5", "HYDROCARBON", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Docked_ItOrbitsFirst()
    {
        // An arrival docks the ship.
        _ships.FindAsync("SHIP-5", Arg.Any<CancellationToken>())
            .Returns(SiphonDrone(waypoint: C38, status: "DOCKED"), SiphonDrone(waypoint: C38, status: "IN_ORBIT"));
        _port.SiphonResourcesAsync("SHIP-5", Arg.Any<CancellationToken>())
            .Returns(new SiphonActionResult("HYDROCARBON", 4, new CargoModel(4, 15, [new CargoItemModel("HYDROCARBON", 4)]), 70));

        var result = await Handler().ExecuteAsync(new SiphonResourcesCommand("SHIP-5", "HYDROCARBON", C38), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await _orbit.Received(1).ExecuteAsync("SHIP-5", Arg.Any<CancellationToken>());
        await _port.Received(1).SiphonResourcesAsync("SHIP-5", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnCooldown_OrWithAFullHold_ItWaits()
    {
        Ship(SiphonDrone(waypoint: C38, status: "IN_ORBIT") with { CooldownExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30) });
        (await Handler().ExecuteAsync(new SiphonResourcesCommand("SHIP-5", "HYDROCARBON", C38), CancellationToken.None)).Accepted.Should().BeTrue();

        Ship(SiphonDrone(waypoint: C38, status: "IN_ORBIT", cargo: [new CargoItemModel("HYDROCARBON", 15)]));
        (await Handler().ExecuteAsync(new SiphonResourcesCommand("SHIP-5", "HYDROCARBON", C38), CancellationToken.None)).Accepted.Should().BeTrue();

        await _port.DidNotReceiveWithAnyArgs().SiphonResourcesAsync(default!, default);
    }

    [Fact]
    public async Task AwayFromTheGasGiant_OrAtAWaypointThatIsntOne_ItRefuses()
    {
        Ship(SiphonDrone(waypoint: C39, status: "IN_ORBIT"));
        (await Handler().ExecuteAsync(new SiphonResourcesCommand("SHIP-5", "HYDROCARBON", C38), CancellationToken.None)).Accepted.Should().BeFalse();

        _waypoints.FindAsync(C39, Arg.Any<CancellationToken>()).Returns(Waypoints.Single(waypoint => waypoint.Symbol == C39));
        (await Handler().ExecuteAsync(new SiphonResourcesCommand("SHIP-5", "HYDROCARBON", C39), CancellationToken.None)).Accepted.Should().BeFalse();

        await _port.DidNotReceiveWithAnyArgs().SiphonResourcesAsync(default!, default);
    }

    private void Ship(ShipModel ship) => _ships.FindAsync(ship.Symbol, Arg.Any<CancellationToken>()).Returns(ship);

    private SiphonResourcesHandler Handler()
        => new(
            _port,
            _ships,
            _waypoints,
            _tradeContexts,
            _orbit,
            _bus,
            _metrics,
            _log.For<SiphonResourcesHandler>());
}
