using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>B62: a flight the fuel aboard can't pay for isn't asked of the API.</summary>
public sealed class NavigationFuelGuardTests
{
    private const string Ship = "SPECTER-6";
    private const string System = "X1-FJ91";
    private const string GasGiant = "X1-FJ91-C45";
    private const string Station = "X1-FJ91-C46";
    private const string Planet = "X1-FJ91-G59";
    private const string Moon = "X1-FJ91-C47";

    private static readonly DateTimeOffset ArrivesAt = new(2026, 10, 04, 20, 13, 18, TimeSpan.Zero);

    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();

    private ShipModel _ship = new(Ship, System, GasGiant, "IN_ORBIT", "CRUISE", 37, 80);

    public NavigationFuelGuardTests()
    {
        // X1-FJ91 as the bot caches it: the gas giant C45 and its station C46 at one spot, G59 93 away; and a moon 30 away.
        WaypointCacheModel[] cached =
        [
            new(GasGiant, System, "GAS_GIANT", 63, 143, HasMarket: false, HasShipyard: false, DateTimeOffset.UtcNow),
            new(Station, System, "ORBITAL_STATION", 63, 143, HasMarket: true, HasShipyard: false, DateTimeOffset.UtcNow),
            new(Planet, System, "PLANET", 33, 55, HasMarket: true, HasShipyard: false, DateTimeOffset.UtcNow),
            new(Moon, System, "MOON", 81, 167, HasMarket: false, HasShipyard: false, DateTimeOffset.UtcNow),
        ];
        _waypoints.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => cached.FirstOrDefault(waypoint => waypoint.Symbol == call.Arg<string>()));
        _waypoints.GetBySystemAsync(System, Arg.Any<CancellationToken>()).Returns(cached);
        _ships.FindAsync(Ship, Arg.Any<CancellationToken>()).Returns(_ => _ship);
        _port.PatchShipNavAsync(Ship, "DRIFT", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _ship = _ship with { FlightMode = "DRIFT" };
            return new NavModel("IN_ORBIT", System, GasGiant, "DRIFT", null, null);
        });
    }

    [Fact]
    public async Task AFlightBeyondTheFuelAboard_IsNotAskedOfTheApi()
    {
        // B62, seen on the cluster on 2026-10-04 at 19:29:59Z: SPECTER-6, at C45 with 37 of its 80 fuel in CRUISE, was sent
        // to G59, 93 away. The API refused the flight with a 400, and only then did the fallback drift. The fuel aboard
        // already said it couldn't fly: the flight goes to the fallback without asking the API.
        _port.NavigateShipAsync(Ship, Planet, Arg.Any<CancellationToken>()).Returns(_ => _ship.FlightMode == "DRIFT"
            ? new NavigateActionResult(new NavModel("IN_TRANSIT", System, Planet, "DRIFT", Planet, ArrivesAt), new FuelModel(36, 80))
            : throw new InvalidOperationException("Navigate request failed. Ship SPECTER-6 requires 93 more fuel for navigation."));

        await NavigateAsync(Planet);

        await _port.Received(1).NavigateShipAsync(Ship, Planet, Arg.Any<CancellationToken>());
        await _port.Received(1).PatchShipNavAsync(Ship, "DRIFT", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFlightTheFuelAboardPaysFor_IsAskedOfTheApi_InItsMode()
    {
        // The station at the gas giant: no distance at all, so no fuel to speak of.
        _port.NavigateShipAsync(Ship, Station, Arg.Any<CancellationToken>())
            .Returns(new NavigateActionResult(new NavModel("IN_TRANSIT", System, Station, "CRUISE", Station, ArrivesAt), new FuelModel(37, 80)));

        await NavigateAsync(Station);

        await _port.Received(1).NavigateShipAsync(Ship, Station, Arg.Any<CancellationToken>());
        await _port.DidNotReceive().PatchShipNavAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABurnTheFuelAboardDoesntPayFor_FliesInCruise_BeforeAnythingDrifts()
    {
        // D84: a leg planned in BURN that the fuel aboard no longer pays for flies in CRUISE where that is paid for, rather
        // than in DRIFT, ten times slower. The moon is 30 from C45: 60 in BURN, 30 in CRUISE, and 37 are aboard.
        _ship = _ship with { FlightMode = "BURN" };
        _port.PatchShipNavAsync(Ship, "CRUISE", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _ship = _ship with { FlightMode = "CRUISE" };
            return new NavModel("IN_ORBIT", System, GasGiant, "CRUISE", null, null);
        });
        _port.NavigateShipAsync(Ship, Moon, Arg.Any<CancellationToken>())
            .Returns(new NavigateActionResult(new NavModel("IN_TRANSIT", System, Moon, "CRUISE", Moon, ArrivesAt), new FuelModel(7, 80)));

        await NavigateAsync(Moon);

        await _port.Received(1).PatchShipNavAsync(Ship, "CRUISE", Arg.Any<CancellationToken>());
        await _port.DidNotReceive().PatchShipNavAsync(Ship, "DRIFT", Arg.Any<CancellationToken>());
        await _port.Received(1).NavigateShipAsync(Ship, Moon, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("CRUISE", 92.6, 93)]
    [InlineData("CRUISE", 0.3, 1)]
    [InlineData("STEALTH", 42.5, 43)]
    [InlineData("BURN", 42.5, 86)]
    [InlineData("DRIFT", 92.6, 1)]
    [InlineData("CRUISE", 0, 0)]
    [InlineData("DRIFT", 0, 0)]
    public void FlightFuel_IsWhatTheApiCharges(string flightMode, double distance, int fuel)
        => FlightFuel.Needed(flightMode, distance).Should().Be(fuel);

    private Task NavigateAsync(string destination)
        => new NavigateSubCommand(
                _port,
                _ships,
                _waypoints,
                Substitute.For<IShipEventScheduler>(),
                Substitute.For<IDashboardNotifier>(),
                Substitute.For<IMessageBus>(),
                NullLogger<NavigateSubCommand>.Instance)
            .ExecuteAsync(Ship, destination, Guid.NewGuid(), CancellationToken.None);
}
