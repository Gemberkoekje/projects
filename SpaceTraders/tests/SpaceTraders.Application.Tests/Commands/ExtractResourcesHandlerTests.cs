using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.SpareTime.SpareTimeFixture;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>
/// Slice 6.8: one extraction of a spare-time trip, without a survey (the surveys stay for the drones, D35), keeping
/// whatever a market buys. What it yields counts as extracted units for the dashboard, but not as an extraction: the
/// survey statistics would read it as a sign of too few surveys.
/// </summary>
public sealed class ExtractResourcesHandlerTests
{
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly Wolverine.IMessageBus _bus = Substitute.For<Wolverine.IMessageBus>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly GatheringRates _rates = new();
    private readonly LogRecorder _log = new();

    public ExtractResourcesHandlerTests()
    {
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _waypoints.FindAsync(XB5C, Arg.Any<CancellationToken>()).Returns(Waypoints.Single(waypoint => waypoint.Symbol == XB5C));
    }

    [Fact]
    public async Task InOrbitAtTheAsteroid_ItExtractsWithoutASurvey_KeepsWhatSells_AndStartsTheCooldown()
    {
        var cooldown = DateTimeOffset.UtcNow.AddSeconds(70);
        Ship(CommandShip(XB5C, cargo: [new CargoItemModel("COPPER_ORE", 4)]));
        _port.ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(new ExtractionActionResult(
            "QUARTZ_SAND",
            6,
            new CargoModel(10, 40, [new CargoItemModel("COPPER_ORE", 4), new CargoItemModel("QUARTZ_SAND", 6)]),
            70,
            cooldown));

        var result = await Handler().ExecuteAsync(new ExtractResourcesCommand("SHIP-1", XB5C), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.CargoCurrent.Should().Be(10);
        await _ships.Received(1).UpdateCargoAsync("SHIP-1", Arg.Is<CargoModel>(cargo => cargo.Units == 10), Arg.Any<CancellationToken>());
        await _ships.Received(1).UpdateCooldownAsync("SHIP-1", cooldown, Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().ExtractWithSurveyAsync(default!, default!, default);
        await _port.DidNotReceiveWithAnyArgs().JettisonCargoAsync(default!, default!, default, default);

        // What the dashboard shows as mined; not an extraction for the survey statistics.
        _metrics.Received(1).Extracted("SHIP-1", "QUARTZ_SAND", 6);
        _metrics.DidNotReceiveWithAnyArgs().Extraction(default!, default);

        // How fast the ship fills its hold, for the role board's estimates (slice 6.9).
        _rates.For("SHIP-1", GatheringKind.Mining).Should().Be(new GatheringRate(6, 70, Observed: true));

        var extracted = _log.Journal.Should().ContainSingle().Subject;
        extracted.EventKind.Should().Be("Extracted");
        extracted.Properties["TradeSymbol"].Should().Be("QUARTZ_SAND");
        extracted.Properties["Target"].Should().Be("whatever sells");
        extracted.Properties["Signature"].Should().Be(string.Empty);
    }

    [Fact]
    public async Task WhatNoMarketBuys_IsJettisoned()
    {
        // No market here buys ICE_WATER: it would fill the hold for good.
        Ship(CommandShip(XB5C));
        _port.ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(new ExtractionActionResult(
            "ICE_WATER",
            7,
            new CargoModel(7, 40, [new CargoItemModel("ICE_WATER", 7)]),
            70));
        _port.JettisonCargoAsync("SHIP-1", "ICE_WATER", 7, Arg.Any<CancellationToken>())
            .Returns(new JettisonActionResult(new CargoModel(0, 40, [])));

        var result = await Handler().ExecuteAsync(new ExtractResourcesCommand("SHIP-1", XB5C), CancellationToken.None);

        result.CargoCurrent.Should().Be(0);
        _metrics.Received(1).Jettisoned("SHIP-1", "ICE_WATER", 7);
    }

    [Fact]
    public async Task Docked_ItOrbitsFirst()
    {
        // An arrival docks the ship.
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(CommandShip(XB5C, "DOCKED"), CommandShip(XB5C, "IN_ORBIT"));
        _port.ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ExtractionActionResult("IRON_ORE", 5, new CargoModel(5, 40, [new CargoItemModel("IRON_ORE", 5)]), 70));

        var result = await Handler().ExecuteAsync(new ExtractResourcesCommand("SHIP-1", XB5C), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await _orbit.Received(1).ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _port.Received(1).ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnCooldown_OrWithAFullHold_ItWaits()
    {
        Ship(CommandShip(XB5C) with { CooldownExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30) });
        (await Handler().ExecuteAsync(new ExtractResourcesCommand("SHIP-1", XB5C), CancellationToken.None)).Accepted.Should().BeTrue();

        Ship(CommandShip(XB5C, cargo: [new CargoItemModel("COPPER_ORE", 40)]));
        (await Handler().ExecuteAsync(new ExtractResourcesCommand("SHIP-1", XB5C), CancellationToken.None)).Accepted.Should().BeTrue();

        await _port.DidNotReceiveWithAnyArgs().ExtractResourcesAsync(default!, default);
    }

    [Fact]
    public async Task AwayFromTheAsteroid_OrAtAWaypointThatIsntOne_ItRefuses()
    {
        Ship(CommandShip(H51));
        (await Handler().ExecuteAsync(new ExtractResourcesCommand("SHIP-1", XB5C), CancellationToken.None)).Accepted.Should().BeFalse();

        _waypoints.FindAsync(C38, Arg.Any<CancellationToken>()).Returns(Waypoints.Single(waypoint => waypoint.Symbol == C38));
        Ship(CommandShip(C38));
        (await Handler().ExecuteAsync(new ExtractResourcesCommand("SHIP-1", C38), CancellationToken.None)).Accepted.Should().BeFalse();

        await _port.DidNotReceiveWithAnyArgs().ExtractResourcesAsync(default!, default);
    }

    private void Ship(ShipModel ship) => _ships.FindAsync(ship.Symbol, Arg.Any<CancellationToken>()).Returns(ship);

    private ExtractResourcesHandler Handler()
        => new(
            _port,
            _ships,
            _waypoints,
            _tradeContexts,
            _orbit,
            _bus,
            _metrics,
            _rates,
            _log.For<ExtractResourcesHandler>());
}
