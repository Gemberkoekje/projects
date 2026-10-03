using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>D42: cargo nothing will sell or use goes overboard, counted and journaled.</summary>
public sealed class CargoJettisonTests
{
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly JettisonRetries _retries = new();
    private readonly LogRecorder _log = new();

    [Fact]
    public async Task ItJettisonsTheGood_StoresTheHoldLeft_CountsIt_AndJournalsIt()
    {
        var left = new CargoModel(10, 40, [new CargoItemModel("EQUIPMENT", 10)]);
        _port.JettisonCargoAsync("SHIP-1", "COPPER_ORE", 7, Arg.Any<CancellationToken>()).Returns(new JettisonActionResult(left));

        var result = await Jettison().JettisonAsync(Ship("DOCKED"), new CargoItemModel("COPPER_ORE", 7), HeldCargo.NoBuyer, CancellationToken.None);

        result.Should().Be(left);
        await _ships.Received(1).UpdateCargoAsync("SHIP-1", left, Arg.Any<CancellationToken>());
        _metrics.Received(1).Jettisoned("SHIP-1", "COPPER_ORE", 7);
        var line = _log.Journal.Should().ContainSingle().Subject;
        line.EventKind.Should().Be("CargoJettisoned");
        line.Properties["TradeSymbol"].Should().Be("COPPER_ORE");
        line.Properties["Units"].Should().Be(7);
        line.Properties["WaypointSymbol"].Should().Be(A1);
        line.Properties["Reason"].Should().Be(HeldCargo.NoBuyer);
    }

    [Fact]
    public async Task AShipInFlight_KeepsItsCargo()
    {
        var result = await Jettison().JettisonAsync(Ship("IN_TRANSIT"), new CargoItemModel("COPPER_ORE", 7), HeldCargo.NoBuyer, CancellationToken.None);

        result.Should().BeNull();
        await _port.DidNotReceiveWithAnyArgs().JettisonCargoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task AJettisonTheApiRefuses_IsAWarning_TheCargoStaysAboard_AndItIsntTriedAgainForAWhile()
    {
        _port.JettisonCargoAsync("SHIP-1", "COPPER_ORE", 7, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("400"));

        var result = await Jettison().JettisonAsync(Ship("IN_ORBIT"), new CargoItemModel("COPPER_ORE", 7), HeldCargo.NoBuyer, CancellationToken.None);
        await Jettison().JettisonAsync(Ship("IN_ORBIT"), new CargoItemModel("COPPER_ORE", 7), HeldCargo.NoBuyer, CancellationToken.None);

        result.Should().BeNull();
        await _port.Received(1).JettisonCargoAsync("SHIP-1", "COPPER_ORE", 7, Arg.Any<CancellationToken>());
        await _ships.DidNotReceiveWithAnyArgs().UpdateCargoAsync(default!, default!, default);
        _metrics.DidNotReceiveWithAnyArgs().Jettisoned(default!, default!, default);
        _log.Journal.Should().BeEmpty();
        _retries.MayTry("SHIP-1", "COPPER_ORE", DateTimeOffset.UtcNow.Add(JettisonRetries.Wait)).Should().BeTrue();
    }

    private static ShipModel Ship(string status)
        => CommandShip(A1, status, cargo: [new CargoItemModel("COPPER_ORE", 7), new CargoItemModel("EQUIPMENT", 10)]);

    private CargoJettison Jettison() => new(_port, _ships, _metrics, _retries, _log.For<CargoJettison>());
}
