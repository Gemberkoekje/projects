using NSubstitute;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>
/// The far end of X1-FJ91 and the contract's asteroid, where the contract's flights drifted on 2026-10-04 (B47), with the
/// positions and the fuel price the bot had cached that day:
/// <list type="bullet">
///   <item>J67, the asteroid base at the far end, where the scout plan ended;</item>
///   <item>the fuel stations J66, 119 from J67, and I65, 372 from J66 and 491 from J67;</item>
///   <item>EF5D, the engineered asteroid the contract mined copper at: 747 from J67, 628 from J66 and 256 from I65;</item>
///   <item>H60, where the copper was delivered: 19 from EF5D, 274 from I65 and 765 from J67.</item>
/// </list>
/// Each sells fuel at 72. The command ship's 400-unit tank doesn't take it from J67 to EF5D in CRUISE, and no market is
/// within one tank of both: the flight refuels at J66 and I65. The system's other markets are left out, so that is the only
/// way with the fewest stops.
/// </summary>
internal static class FuelStopFixture
{
    public const string SystemSymbol = "X1-FJ91";
    public const string J67 = "X1-FJ91-J67";
    public const string J66 = "X1-FJ91-J66";
    public const string I65 = "X1-FJ91-I65";
    public const string EF5D = "X1-FJ91-EF5D";
    public const string H60 = "X1-FJ91-H60";

    public static IReadOnlyList<WaypointCacheModel> Waypoints =>
    [
        Waypoint(J67, "ASTEROID_BASE", -688, -210),
        Waypoint(J66, "FUEL_STATION", -574, -175),
        Waypoint(I65, "FUEL_STATION", -218, -67),
        Waypoint(EF5D, "ENGINEERED_ASTEROID", 27, 7),
        Waypoint(H60, "PLANET", 46, 7),
    ];

    public static TradeMarketMap Map()
        => new(Waypoints, [.. Waypoints.Select(waypoint => FuelMarket(waypoint.Symbol))], new Dictionary<string, IReadOnlyList<string>>());

    public static TradeContext Context() => new(Map(), 150_000, 200);

    /// <summary>SPECTER-1, the command ship: a mining laser, a 40-unit hold and a 400-unit tank.</summary>
    public static ShipModel CommandShip(
        string waypoint = J67,
        string status = "DOCKED",
        string flightMode = "CRUISE",
        int fuel = 400,
        IReadOnlyList<CargoItemModel>? cargo = null)
        => new(
            "SPECTER-1",
            SystemSymbol,
            waypoint,
            status,
            flightMode,
            fuel,
            400,
            CargoCurrent: (cargo ?? []).Sum(item => item.Units),
            CargoCapacity: 40,
            ShipType: "COMMAND",
            MountSymbols: ["MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II"],
            CargoInventory: cargo ?? []);

    private static MarketSnapshot FuelMarket(string waypoint)
        => new(waypoint, SystemSymbol, [new TradeGoodSnapshot("FUEL", "EXCHANGE", 72, 68, 180, "MODERATE")], [], [], ["FUEL"]);

    private static WaypointCacheModel Waypoint(string symbol, string type, int x, int y)
        => new(symbol, SystemSymbol, type, x, y, true, false, DateTimeOffset.UnixEpoch, TraitsJson: """[{"symbol":"MARKETPLACE"}]""");
}

/// <summary>One flight: where it went, in which mode, the fuel aboard when it left and the fuel it burnt.</summary>
internal sealed record Flight(string Destination, string FlightMode, int FuelAboard, int FuelBurnt);

/// <summary>
/// A ship in <see cref="FuelStopFixture"/> as the cache holds it, for a contract command run tick after tick (B47). The
/// sub-commands change it as the game would: only a docked ship refuels, only a ship in orbit flies, and a flight burns its
/// CRUISE fuel, or 1 in DRIFT. Navigating flies as the navigation does: in the ship's mode, and in DRIFT when CRUISE needs
/// more fuel than is aboard (the fallback). Each flight lands before the next tick, which dead-reckons its arrival (B17).
/// </summary>
internal sealed class FlyingShip
{
    private const string Cruise = "CRUISE";
    private const string Drift = "DRIFT";

    private readonly TradeMarketMap _map = FuelStopFixture.Map();

    public FlyingShip(ShipModel ship)
    {
        Ship = ship;
        Ships.FindAsync(ship.Symbol, Arg.Any<CancellationToken>()).Returns(_ => Ship);
        Ships.When(ships => ships.UpdateNavAsync(ship.Symbol, Arg.Any<NavModel>(), Arg.Any<FuelModel?>(), Arg.Any<CancellationToken>()))
            .Do(call => Land(call.Arg<NavModel>()));
        Dock.When(dock => dock.ExecuteAsync(ship.Symbol, Arg.Any<CancellationToken>()))
            .Do(_ => Ship = Ship with { Status = "DOCKED" });
        Orbit.When(orbit => orbit.ExecuteAsync(ship.Symbol, Arg.Any<CancellationToken>()))
            .Do(_ => Ship = Ship with { Status = "IN_ORBIT" });
        Refuel.When(refuel => refuel.ExecuteAsync(ship.Symbol, false, Arg.Any<CancellationToken>()))
            .Do(_ => FillTank());
        FlightMode.When(mode => mode.EnsureAsync(Arg.Any<ShipModel>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(call => Ship = Ship with { FlightMode = call.ArgAt<string>(1) });
        Navigate.When(navigate => navigate.ExecuteAsync(ship.Symbol, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()))
            .Do(call => Fly(call.ArgAt<string>(1)));
    }

    /// <summary>The ship as the cache holds it now.</summary>
    public ShipModel Ship { get; private set; }

    public IShipRepository Ships { get; } = Substitute.For<IShipRepository>();

    public IDockSubCommand Dock { get; } = Substitute.For<IDockSubCommand>();

    public IOrbitSubCommand Orbit { get; } = Substitute.For<IOrbitSubCommand>();

    public IRefuelSubCommand Refuel { get; } = Substitute.For<IRefuelSubCommand>();

    public IFlightModeSubCommand FlightMode { get; } = Substitute.For<IFlightModeSubCommand>();

    public INavigateSubCommand Navigate { get; } = Substitute.For<INavigateSubCommand>();

    /// <summary>Its flights, in order.</summary>
    public List<Flight> Flights { get; } = [];

    private void FillTank()
    {
        if (Ship.LocalStatus != ShipLocalStatus.Docked)
        {
            throw new InvalidOperationException($"{Ship.Symbol} refuels in orbit; the API refuels only a docked ship.");
        }

        Ship = Ship with { FuelCurrent = Ship.FuelCapacity };
    }

    private void Fly(string destination)
    {
        if (Ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            throw new InvalidOperationException($"{Ship.Symbol} navigates while {Ship.Status}; the API navigates only a ship in orbit.");
        }

        _map.TryGetDistance(Ship.WaypointSymbol ?? string.Empty, destination, out var distance);
        var cruiseFuel = Math.Max(1, (int)Math.Round(distance, MidpointRounding.AwayFromZero));
        var mode = Ship.FlightMode == Cruise && cruiseFuel > Ship.FuelCurrent ? Drift : Ship.FlightMode ?? Cruise;
        var burnt = mode == Drift ? 1 : cruiseFuel;
        Flights.Add(new Flight(destination, mode, Ship.FuelCurrent, burnt));
        Ship = Ship with
        {
            Status = "IN_TRANSIT",
            FlightMode = mode,
            DestWaypointSymbol = destination,
            ArrivesAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            FuelCurrent = Ship.FuelCurrent - burnt,
        };
    }

    private void Land(NavModel nav)
        => Ship = Ship with
        {
            Status = nav.Status,
            WaypointSymbol = nav.WaypointSymbol,
            FlightMode = nav.FlightMode,
            DestWaypointSymbol = nav.DestWaypointSymbol,
            ArrivesAt = nav.ArrivesAt,
        };
}
