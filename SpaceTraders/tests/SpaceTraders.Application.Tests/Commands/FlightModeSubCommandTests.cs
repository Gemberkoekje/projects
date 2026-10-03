using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>
/// Slice 6.10c: a navigation that asks for a flight mode sets it first, DRIFT for a drone's drift to a market out of its
/// CRUISE reach (D45), CRUISE for every flight a goal plans; the API is called only when the ship's mode differs.
/// </summary>
public sealed class FlightModeSubCommandTests
{
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly LogRecorder _log = new();

    [Fact]
    public async Task AShipInAnotherMode_IsSwitched_AndItsNavIsStored()
    {
        var nav = new NavModel("IN_ORBIT", "X1-AB", "X1-AB-B7", "CRUISE", null, null);
        _port.PatchShipNavAsync("SHIP-3", "CRUISE", Arg.Any<CancellationToken>()).Returns(nav);

        await EnsureAsync(Ship("DRIFT"), "CRUISE");

        await _port.Received(1).PatchShipNavAsync("SHIP-3", "CRUISE", Arg.Any<CancellationToken>());
        await _ships.Received(1).UpdateNavAsync("SHIP-3", nav, null, Arg.Any<CancellationToken>());
        _log.Kept.Should().ContainSingle().Which.Should().Contain("DRIFT").And.Contain("CRUISE");
    }

    [Theory]
    [InlineData("CRUISE")]
    [InlineData("cruise")]
    public async Task AShipAlreadyInTheMode_MakesNoCall(string mode)
    {
        await EnsureAsync(Ship(mode), "CRUISE");

        await _port.DidNotReceiveWithAnyArgs().PatchShipNavAsync(default!, default!, default);
        await _ships.DidNotReceiveWithAnyArgs().UpdateNavAsync(default!, default!, default, default);
    }

    private Task EnsureAsync(ShipModel ship, string flightMode)
        => new FlightModeSubCommand(_port, _ships, _log.For<FlightModeSubCommand>()).EnsureAsync(ship, flightMode, CancellationToken.None);

    private static ShipModel Ship(string flightMode)
        => new("SHIP-3", "X1-AB", "X1-AB-B7", "IN_ORBIT", flightMode, 79, 80);
}
