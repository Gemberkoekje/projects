using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Commands.FuelStopFixture;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class MineResourceVolumeHandlerTests
{
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly ISurveyRepository _surveys = Substitute.For<ISurveyRepository>();
    private readonly ISurveyKeeper _surveyKeeper = Substitute.For<ISurveyKeeper>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly INavigateSubCommand _navigate = Substitute.For<INavigateSubCommand>();
    private readonly Wolverine.IMessageBus _bus = Substitute.For<Wolverine.IMessageBus>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly GatheringRates _rates = new();
    private readonly LogRecorder _log = new();

    public MineResourceVolumeHandlerTests()
    {
        _surveys.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<StoredSurvey>());
        _tradeContexts.ReadAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns(new TradeContext(new TradeMarketMap([], [], new Dictionary<string, IReadOnlyList<string>>()), 0, 0));
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _waypoints.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Waypoints.FirstOrDefault(waypoint => waypoint.Symbol == call.Arg<string>()));
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsAndJettisonsNonTargetCargo_WhenAtSourceInOrbit()
    {
        var ship = AtAsteroid("SHIP-1") with
        {
            CargoCurrent = 5,
            CargoInventory = [new CargoItemModel("ICE_WATER", 5)],
        };

        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship, ship with
        {
            CargoInventory =
            [
                new CargoItemModel("IRON_ORE", 8),
                new CargoItemModel("ICE_WATER", 2),
            ],
            CargoCurrent = 10,
            CooldownExpiresAt = DateTimeOffset.UtcNow.AddSeconds(10),
        });

        _port.ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>())
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

        _port.JettisonCargoAsync("SHIP-1", "ICE_WATER", 2, Arg.Any<CancellationToken>())
            .Returns(new JettisonActionResult(new CargoModel(8, 40, [new CargoItemModel("IRON_ORE", 8)])));

        var result = await Handler().ExecuteAsync(new MineResourceVolumeCommand("SHIP-1", "IRON_ORE", "X1-AB-AST", 20), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await _port.Received(1).ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _port.Received(1).JettisonCargoAsync("SHIP-1", "ICE_WATER", 2, Arg.Any<CancellationToken>());
        await _navigate.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, Guid.Empty, default);

        // What the dashboard shows as mined, and what went overboard.
        _metrics.Received(1).Extracted("SHIP-1", "IRON_ORE", 8);
        _metrics.Received(1).Jettisoned("SHIP-1", "ICE_WATER", 2);
        _metrics.Received(1).Extraction("SHIP-1", false);
    }

    [Fact]
    public async Task ForAMiningTrip_TheOresAMarketBuysWithinOneTank_StayAboard_AndTheRestGoOverboard()
    {
        // D71, asked on 2026-10-04: "only throw out minerals that they cannot sell within a single tank of fuel, instead of
        // everything they're not specifically mining for". NEAR buys quartz 60 from the asteroid, within the drone's
        // 80-unit tank. FAR buys aluminum 140 away: a refuelling stop at FUEL gets it there, but not one tank. Nobody buys
        // ice water.
        _tradeContexts.ReadAsync(OneTank.SystemSymbol, Arg.Any<CancellationToken>()).Returns(OneTank.Context());
        var drone = OneTank.Drone();
        var mined = new CargoModel(
            14,
            15,
            [
                new CargoItemModel("IRON_ORE", 3),
                new CargoItemModel("QUARTZ_SAND", 4),
                new CargoItemModel("ALUMINUM_ORE", 5),
                new CargoItemModel("ICE_WATER", 2),
            ]);
        _ships.FindAsync(drone.Symbol, Arg.Any<CancellationToken>()).Returns(drone, drone with { CargoCurrent = 14, CargoInventory = mined.Inventory });
        _port.ExtractResourcesAsync(drone.Symbol, Arg.Any<CancellationToken>())
            .Returns(new ExtractionActionResult("QUARTZ_SAND", 4, mined, CooldownSeconds: 70));
        _port.JettisonCargoAsync(drone.Symbol, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new JettisonActionResult(new CargoModel(7, 15, [new CargoItemModel("IRON_ORE", 3), new CargoItemModel("QUARTZ_SAND", 4)])));

        var result = await Handler().ExecuteAsync(
            new MineResourceVolumeCommand(drone.Symbol, "IRON_ORE", OneTank.Asteroid, 15) { KeepOtherOres = true },
            CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await _port.Received(1).JettisonCargoAsync(drone.Symbol, "ALUMINUM_ORE", 5, Arg.Any<CancellationToken>());
        await _port.Received(1).JettisonCargoAsync(drone.Symbol, "ICE_WATER", 2, Arg.Any<CancellationToken>());
        await _port.DidNotReceive().JettisonCargoAsync(drone.Symbol, "QUARTZ_SAND", Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _port.DidNotReceive().JettisonCargoAsync(drone.Symbol, "IRON_ORE", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForTheContract_EveryOtherGoodGoesOverboard_ThoughAMarketWithinOneTankBuysIt()
    {
        // D71 is the mining plan's: a contract round trip keeps its hold for the contract's ore.
        _tradeContexts.ReadAsync(OneTank.SystemSymbol, Arg.Any<CancellationToken>()).Returns(OneTank.Context());
        var drone = OneTank.Drone();
        var mined = new CargoModel(7, 15, [new CargoItemModel("IRON_ORE", 3), new CargoItemModel("QUARTZ_SAND", 4)]);
        _ships.FindAsync(drone.Symbol, Arg.Any<CancellationToken>()).Returns(drone, drone with { CargoCurrent = 7, CargoInventory = mined.Inventory });
        _port.ExtractResourcesAsync(drone.Symbol, Arg.Any<CancellationToken>())
            .Returns(new ExtractionActionResult("QUARTZ_SAND", 4, mined, CooldownSeconds: 70));
        _port.JettisonCargoAsync(drone.Symbol, "QUARTZ_SAND", 4, Arg.Any<CancellationToken>())
            .Returns(new JettisonActionResult(new CargoModel(3, 15, [new CargoItemModel("IRON_ORE", 3)])));

        await Handler().ExecuteAsync(new MineResourceVolumeCommand(drone.Symbol, "IRON_ORE", OneTank.Asteroid, 40), CancellationToken.None);

        await _port.Received(1).JettisonCargoAsync(drone.Symbol, "QUARTZ_SAND", 4, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForAMiningTrip_AFullHold_KeepsTheOresAMarketBuysWithinOneTank()
    {
        // A full hold mines no more; what the trip keeps stays aboard for the mining plan to sell (D71).
        _tradeContexts.ReadAsync(OneTank.SystemSymbol, Arg.Any<CancellationToken>()).Returns(OneTank.Context());
        var full = OneTank.Drone() with
        {
            CargoCurrent = 15,
            CargoInventory = [new CargoItemModel("IRON_ORE", 5), new CargoItemModel("QUARTZ_SAND", 10)],
        };
        _ships.FindAsync(full.Symbol, Arg.Any<CancellationToken>()).Returns(full);

        var result = await Handler().ExecuteAsync(
            new MineResourceVolumeCommand(full.Symbol, "IRON_ORE", OneTank.Asteroid, 15) { KeepOtherOres = true },
            CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await _port.DidNotReceiveWithAnyArgs().ExtractResourcesAsync(default!, default);
        await _port.DidNotReceiveWithAnyArgs().JettisonCargoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_NavigatesToSource_WhenNotAtSource()
    {
        var ship = AtAsteroid("SHIP-2") with { WaypointSymbol = "X1-AB-MKT" };
        _ships.FindAsync("SHIP-2", Arg.Any<CancellationToken>()).Returns(ship);

        var result = await Handler().ExecuteAsync(new MineResourceVolumeCommand("SHIP-2", "IRON_ORE", "X1-AB-AST", 15), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await _navigate.Received(1).ExecuteAsync("SHIP-2", "X1-AB-AST", Guid.Empty, Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().ExtractResourcesAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_MinesAnAsteroid()
    {
        // 56 of X1-DC53's 57 asteroids are of type ASTEROID; only ASTEROID_FIELD and ENGINEERED_ASTEROID
        // were accepted, so a drone sent to any of them got a state mismatch instead of ore.
        _ships.FindAsync("SHIP-3", Arg.Any<CancellationToken>()).Returns(AtAsteroid("SHIP-3"));
        _waypoints.FindAsync("X1-AB-AST", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-AST", "X1-AB", "ASTEROID", 0, 0, false, false, DateTimeOffset.UnixEpoch));
        _port.ExtractResourcesAsync("SHIP-3", Arg.Any<CancellationToken>())
            .Returns(Extraction("IRON_ORE", 3));

        var result = await Handler().ExecuteAsync(new MineResourceVolumeCommand("SHIP-3", "IRON_ORE", "X1-AB-AST", 15), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await _port.Received(1).ExtractResourcesAsync("SHIP-3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsWithTheBestSurveyForTheGood_AndCountsItsUse()
    {
        _ships.FindAsync("SHIP-3", Arg.Any<CancellationToken>()).Returns(AtAsteroid("SHIP-3"));
        var mostlyIron = Survey("SIG-IRON", "IRON_ORE", "IRON_ORE", "ICE_WATER");
        var someIron = Survey("SIG-SOME", "IRON_ORE", "ICE_WATER", "ICE_WATER");
        _surveys.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new StoredSurvey(someIron, "SHIP-1", DateTimeOffset.UtcNow, 0), new StoredSurvey(mostlyIron, "SHIP-1", DateTimeOffset.UtcNow, 0)]);
        _port.ExtractWithSurveyAsync("SHIP-3", Arg.Any<SurveyModel>(), Arg.Any<CancellationToken>())
            .Returns(Extraction("IRON_ORE", 3));

        await Handler().ExecuteAsync(new MineResourceVolumeCommand("SHIP-3", "IRON_ORE", "X1-AB-AST", 15), CancellationToken.None);

        await _port.Received(1).ExtractWithSurveyAsync("SHIP-3", Arg.Is<SurveyModel>(s => s.Signature == "SIG-IRON"), Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().ExtractResourcesAsync(default!, default);
        await _surveyKeeper.Received(1).UsedAsync("SIG-IRON", Arg.Any<CancellationToken>());
        _metrics.Received(1).Extraction("SHIP-3", true);
    }

    [Fact]
    public async Task ExecuteAsync_DropsASurveyTheApiRefuses_AndExtractsNothingThisStep()
    {
        _ships.FindAsync("SHIP-3", Arg.Any<CancellationToken>()).Returns(AtAsteroid("SHIP-3"));
        _surveys.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new StoredSurvey(Survey("SIG-1", "IRON_ORE"), "SHIP-1", DateTimeOffset.UtcNow, 4)]);
        var refused = new SurveyRefusedException("SIG-1", SurveyRefusedException.ExhaustedErrorCode, new InvalidOperationException("exhausted"));
        _port.ExtractWithSurveyAsync("SHIP-3", Arg.Any<SurveyModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(refused);

        var result = await Handler().ExecuteAsync(new MineResourceVolumeCommand("SHIP-3", "IRON_ORE", "X1-AB-AST", 15), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await _surveyKeeper.Received(1).RefusedAsync(refused, Arg.Any<CancellationToken>());
        await _ships.DidNotReceiveWithAnyArgs().UpdateCargoAsync(default!, default!, default);
        _metrics.DidNotReceiveWithAnyArgs().Extraction(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ASurveyTheApiRejects_IsDropped_AndLoggedWithWhatTheApiSaid()
    {
        // B51: the API's 422 named no game error; its "data" is the only clue to what it couldn't read.
        _ships.FindAsync("SHIP-3", Arg.Any<CancellationToken>()).Returns(AtAsteroid("SHIP-3"));
        _surveys.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new StoredSurvey(Survey("SIG-1", "IRON_ORE"), "SHIP-1", DateTimeOffset.UtcNow, 0)]);
        var rejected = new SurveyRefusedException(
            "SIG-1",
            SurveyRefusedException.RejectedErrorCode,
            new InvalidOperationException("invalid payload"),
            """{"error":{"code":422,"data":{"expiration":["Invalid datetime"]}}}""");
        _port.ExtractWithSurveyAsync("SHIP-3", Arg.Any<SurveyModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(rejected);

        var result = await Handler().ExecuteAsync(new MineResourceVolumeCommand("SHIP-3", "IRON_ORE", "X1-AB-AST", 15), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await _surveyKeeper.Received(1).RefusedAsync(rejected, Arg.Any<CancellationToken>());
        _log.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("SIG-1").And.Contain("Invalid datetime");
    }

    [Fact]
    public async Task ExecuteAsync_TheFlightToAnAsteroidBeyondOneTank_RefuelsOnTheWay_BurningWhereTheFuelAllows()
    {
        // B47, on the cluster on 2026-10-04: the scout plan left SPECTER-1 at J67, 747 from the contract's asteroid. A
        // 400-unit tank doesn't do that in CRUISE, so the navigation's fallback drifted it there: 87 minutes. The fuel
        // stations J66 and I65 are on the way. Each tick flies a leg, and the next one dead-reckons its arrival. A full
        // tank pays for the 119 to J66 twice over, so that leg burns (D84); the 372 and the 256 after it cruise.
        var ship = new FlyingShip(CommandShip());
        _port.ExtractResourcesAsync("SPECTER-1", Arg.Any<CancellationToken>()).Returns(Extraction("COPPER_ORE", 9));

        for (var tick = 0; tick < 4; tick++)
        {
            await Handler(ship).ExecuteAsync(new MineResourceVolumeCommand("SPECTER-1", "COPPER_ORE", EF5D, 120), CancellationToken.None);
        }

        ship.Flights.Should().Equal(
            new Flight(J66, "BURN", 400, 238),
            new Flight(I65, "CRUISE", 400, 372),
            new Flight(EF5D, "CRUISE", 400, 256));
        await _port.Received(1).ExtractResourcesAsync("SPECTER-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AShipLeftInDrift_NoLongerDrifts()
    {
        // B47: the fallback leaves a ship in DRIFT, ten times slower, and nothing switched SPECTER-1 back for the contract:
        // it delivered to H60 in DRIFT, and stayed in DRIFT until a survey trip asked for CRUISE. Docked at H60 after a
        // delivery, its next trip to EF5D goes in the mode its flight asks for: BURN, since EF5D sells fuel and a full tank
        // pays for the 19 twice over (D84).
        var ship = new FlyingShip(CommandShip(H60, flightMode: "DRIFT", fuel: 399));

        await Handler(ship).ExecuteAsync(new MineResourceVolumeCommand("SPECTER-1", "COPPER_ORE", EF5D, 80), CancellationToken.None);

        ship.Flights.Should().Equal(new Flight(EF5D, "BURN", 400, 38));
    }

    [Fact]
    public async Task ExecuteAsync_InOrbitAtAFuelMarket_ItFillsTheTankFirst_WhenAFullTankWouldBurn()
    {
        // D84: with 30 aboard at H60, the 19 to EF5D cruise; a full tank burns them. The ship docks and refuels first, as a
        // goal's flight does.
        var ship = new FlyingShip(CommandShip(H60, "IN_ORBIT", fuel: 30));

        await Handler(ship).ExecuteAsync(new MineResourceVolumeCommand("SPECTER-1", "COPPER_ORE", EF5D, 80), CancellationToken.None);

        ship.Flights.Should().Equal(new Flight(EF5D, "BURN", 400, 38));
    }

    private MineResourceVolumeHandler Handler()
        => new(
            _port,
            _ships,
            _waypoints,
            _surveys,
            _surveyKeeper,
            _tradeContexts,
            Substitute.For<IRefuelSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            Substitute.For<IDockSubCommand>(),
            Substitute.For<IFlightModeSubCommand>(),
            _navigate,
            _bus,
            _metrics,
            _rates,
            _log.For<MineResourceVolumeHandler>());

    private MineResourceVolumeHandler Handler(FlyingShip ship)
        => new(
            _port,
            ship.Ships,
            _waypoints,
            _surveys,
            _surveyKeeper,
            _tradeContexts,
            ship.Refuel,
            ship.Orbit,
            ship.Dock,
            ship.FlightMode,
            ship.Navigate,
            _bus,
            _metrics,
            _rates,
            _log.For<MineResourceVolumeHandler>());

    private static ShipModel AtAsteroid(string symbol)
        => new(
            Symbol: symbol,
            SystemSymbol: "X1-AB",
            WaypointSymbol: "X1-AB-AST",
            Status: "IN_ORBIT",
            FlightMode: "CRUISE",
            FuelCurrent: 100,
            FuelCapacity: 100,
            CargoCurrent: 0,
            CargoCapacity: 40,
            CargoInventory: []);

    private static ExtractionActionResult Extraction(string good, int units)
        => new(good, units, new CargoModel(units, 40, [new CargoItemModel(good, units)]), CooldownSeconds: 70);

    private static SurveyModel Survey(string signature, params string[] deposits)
        => new(signature, "X1-AB-AST", [.. deposits.Select(deposit => new SurveyDepositModel(deposit))], DateTimeOffset.UtcNow.AddMinutes(20), "MODERATE");

    /// <summary>
    /// A drone's one tank (D71): the asteroid AST, NEAR 60 from it, which buys quartz, and FAR 140 from it, which buys
    /// aluminum. FUEL, 70 from both, sells fuel: a drone's 80-unit tank gets to FAR by way of it, not in one go.
    /// </summary>
    private static class OneTank
    {
        public const string SystemSymbol = "X1-OT";
        public const string Asteroid = "X1-OT-AST";
        private const string Near = "X1-OT-NEAR";
        private const string Fuel = "X1-OT-FUEL";
        private const string Far = "X1-OT-FAR";

        public static TradeContext Context()
            => new(
                new TradeMarketMap(
                    [
                        Waypoint(Asteroid, "ASTEROID", 0, 0, hasMarket: false),
                        Waypoint(Near, "PLANET", 0, 60),
                        Waypoint(Fuel, "FUEL_STATION", 70, 0),
                        Waypoint(Far, "PLANET", 140, 0),
                    ],
                    [
                        Market(Near, new TradeGoodSnapshot("QUARTZ_SAND", "IMPORT", 52, 26, 60, "SCARCE")),
                        Market(Fuel, new TradeGoodSnapshot("FUEL", "EXCHANGE", 72, 68, 180, "MODERATE")),
                        Market(Far, new TradeGoodSnapshot("ALUMINUM_ORE", "IMPORT", 130, 63, 60, "LIMITED")),
                    ],
                    new Dictionary<string, IReadOnlyList<string>>()),
                150_000,
                200);

        /// <summary>A mining drone in orbit at the asteroid: a 15-unit hold and an 80-unit tank.</summary>
        public static ShipModel Drone()
            => new(
                "SHIP-4",
                SystemSymbol,
                Asteroid,
                "IN_ORBIT",
                "CRUISE",
                80,
                80,
                CargoCapacity: 15,
                ShipType: "EXCAVATOR",
                MountSymbols: ["MOUNT_MINING_LASER_I"],
                CargoInventory: []);

        private static MarketSnapshot Market(string waypoint, TradeGoodSnapshot good)
            => new(waypoint, SystemSymbol, [good], good.Type == "IMPORT" ? [good.Symbol] : [], [], good.Type == "EXCHANGE" ? [good.Symbol] : []);

        private static WaypointCacheModel Waypoint(string symbol, string type, int x, int y, bool hasMarket = true)
            => new(symbol, SystemSymbol, type, x, y, hasMarket, false, DateTimeOffset.UnixEpoch);
    }
}
