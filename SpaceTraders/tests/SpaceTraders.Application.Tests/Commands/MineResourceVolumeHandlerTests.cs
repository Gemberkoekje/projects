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

namespace SpaceTraders.Application.Tests.Commands;

public sealed class MineResourceVolumeHandlerTests
{
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly ISurveyRepository _surveys = Substitute.For<ISurveyRepository>();
    private readonly ISurveyKeeper _surveyKeeper = Substitute.For<ISurveyKeeper>();
    private readonly INavigateSubCommand _navigate = Substitute.For<INavigateSubCommand>();
    private readonly Wolverine.IMessageBus _bus = Substitute.For<Wolverine.IMessageBus>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly LogRecorder _log = new();

    public MineResourceVolumeHandlerTests()
    {
        _surveys.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<StoredSurvey>());
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

    private MineResourceVolumeHandler Handler()
        => new(
            _port,
            _ships,
            _waypoints,
            _surveys,
            _surveyKeeper,
            Substitute.For<IRefuelSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            _navigate,
            _bus,
            _metrics,
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
}
