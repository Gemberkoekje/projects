using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Slice 6.18, D83, asked on 2026-10-05: "We park a light shuttle ... at the asteroid, and have the drones drop their ore into
/// the light shuttle." B13 is 48 from B7: a drone gets there from B7 on a full tank, but not back. It drifts to B7 first
/// (D45), flies on, and at B13 mines and hands its ore to the shuttle collecting there; with its hold full and no shuttle
/// there, it waits without an API call.
/// </summary>
public sealed class MineForShuttleGoalExecutorTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);

    private static readonly MineForShuttleGoal Job = new() { TradeSymbol = "IRON_ORE", AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7 };

    public MineForShuttleGoalExecutorTests()
    {
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new TradeContext(Map(), 129_357, 200));
        _bus.InvokeAsync<ShipCommandResult>(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShipCommandResult("SHIP-3", ShipLocalStatus.InOrbit, SystemSymbol, B13, Accepted: true));
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
    }

    [Fact]
    public async Task FromAfar_ItGoesTheFastestWay_CruisingAsFarAsItCan()
    {
        // D84: the drone cruises the 52 to F49, drifts the 274 to B7 and cruises the 48 on to B13: faster than drifting the
        // 343 straight there, or the 311 from F49.
        var result = await StepAsync(Drone(), Job with { Drifting = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == F49 && command.FlightMode == "CRUISE"),
            Arg.Any<CancellationToken>());
        _log.Journal.Should().BeEmpty();

        // Its arrival docked it at F49.
        await StepAsync(Drone(waypoint: F49) with { FuelCurrent = 28 }, Job with { Drifting = true });

        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == B7 && command.FlightMode == "DRIFT"),
            Arg.Any<CancellationToken>());
        var drift = _log.Journal.Should().ContainSingle().Subject;
        drift.EventKind.Should().Be("DriftStarted");
        drift.Message.Should().Contain(F49).And.Contain(B7).And.Contain(B13);
    }

    [Fact]
    public async Task AtTheMarket_ItFliesOnToTheAsteroid_InCruise()
    {
        await StepAsync(Drone(waypoint: B7), Job);

        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == B13 && command.FlightMode == "CRUISE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheAsteroid_WithRoom_ItExtracts_KeepingTheOresAMarketBuys()
    {
        Fleet(Drone(waypoint: B13, status: "IN_ORBIT"));

        var result = await StepAsync(Drone(waypoint: B13, status: "IN_ORBIT"), Job);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _bus.Received(1).InvokeAsync<ShipCommandResult>(
            Arg.Is<MineResourceVolumeCommand>(command => command.TradeSymbol == "IRON_ORE" && command.SourceWaypoint == B13 && command.KeepOtherOres),
            Arg.Any<CancellationToken>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task DockedAtTheAsteroid_ItOrbitsFirst()
    {
        var result = await StepAsync(Drone(waypoint: B13, status: "DOCKED"), Job);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _orbit.Received(1).ExecuteAsync("SHIP-3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithOreAboard_AndTheShuttleCollectingThere_ItHandsItOver_OneGoodATransfer()
    {
        var drone = Drone(waypoint: B13, status: "IN_ORBIT", cargo: [new CargoItemModel("IRON_ORE", 5), new CargoItemModel("ICE_WATER", 2)]);
        var shuttle = Shuttle(cargo: [new CargoItemModel("IRON_ORE", 30)]);
        Fleet(drone, shuttle);
        _activeGoals["SHIP-7"] = new CollectOreGoal { AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7 };
        _port.TransferCargoAsync("SHIP-3", "SHIP-7", "IRON_ORE", 5, Arg.Any<CancellationToken>()).Returns(new CargoModel(2, 15, [new CargoItemModel("ICE_WATER", 2)]));
        _port.TransferCargoAsync("SHIP-3", "SHIP-7", "ICE_WATER", 2, Arg.Any<CancellationToken>()).Returns(new CargoModel(0, 15, []));

        var result = await StepAsync(drone, Job);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _ships.Received(1).UpdateCargoAsync("SHIP-3", Arg.Is<CargoModel>(cargo => cargo.Units == 0), Arg.Any<CancellationToken>());
        await _ships.Received(1).UpdateCargoAsync(
            "SHIP-7",
            Arg.Is<CargoModel>(cargo => cargo.Units == 37 && cargo.Inventory!.Single(item => item.Symbol == "IRON_ORE").Units == 35 && cargo.Inventory!.Single(item => item.Symbol == "ICE_WATER").Units == 2),
            Arg.Any<CancellationToken>());
        _log.Journal.Where(entry => entry.EventKind == "CargoTransferred").Should().HaveCount(2);
        await _bus.DidNotReceive().InvokeAsync<ShipCommandResult>(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task ItHandsOver_NoMoreThanTheShuttleHasRoomFor()
    {
        var drone = Drone(waypoint: B13, status: "IN_ORBIT", cargo: [new CargoItemModel("IRON_ORE", 15)]);
        Fleet(drone, Shuttle(cargo: [new CargoItemModel("IRON_ORE", 36)]));
        _activeGoals["SHIP-7"] = new CollectOreGoal { AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7 };
        _port.TransferCargoAsync("SHIP-3", "SHIP-7", "IRON_ORE", 4, Arg.Any<CancellationToken>()).Returns(new CargoModel(11, 15, [new CargoItemModel("IRON_ORE", 11)]));

        await StepAsync(drone, Job);

        await _port.Received(1).TransferCargoAsync("SHIP-3", "SHIP-7", "IRON_ORE", 4, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithAFullHold_AndNoShuttleThere_ItWaits_WithoutAnApiCall()
    {
        var drone = Drone(waypoint: B13, status: "IN_ORBIT", cargo: [new CargoItemModel("IRON_ORE", 15)]);
        Fleet(drone);

        var result = await StepAsync(drone, Job);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
        result.Reason.Should().Contain("waiting for the shuttle");
        await _port.DidNotReceiveWithAnyArgs().TransferCargoAsync(default!, default!, default!, default, default);
        await _bus.DidNotReceive().InvokeAsync<ShipCommandResult>(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task AShuttleOnItsWayToSell_TakesNothing()
    {
        var drone = Drone(waypoint: B13, status: "IN_ORBIT", cargo: [new CargoItemModel("IRON_ORE", 15)]);
        Fleet(drone, Shuttle());
        _activeGoals["SHIP-7"] = new CollectOreGoal { AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7, Selling = true };

        var result = await StepAsync(drone, Job);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
        await _port.DidNotReceiveWithAnyArgs().TransferCargoAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task ARefusedTransfer_FetchesTheShuttlesHoldAgain()
    {
        // Another drone's transfer may have filled the shuttle since the cache was written.
        var drone = Drone(waypoint: B13, status: "IN_ORBIT", cargo: [new CargoItemModel("IRON_ORE", 5)]);
        Fleet(drone, Shuttle(cargo: [new CargoItemModel("IRON_ORE", 30)]));
        _activeGoals["SHIP-7"] = new CollectOreGoal { AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7 };
        _port.TransferCargoAsync("SHIP-3", "SHIP-7", "IRON_ORE", 5, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Ship cargo does not have room."));
        var fetched = new CargoModel(40, 40, [new CargoItemModel("IRON_ORE", 40)]);
        _port.GetShipCargoAsync("SHIP-7", Arg.Any<CancellationToken>()).Returns(fetched);

        var result = await StepAsync(drone, Job);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _ships.Received(1).UpdateCargoAsync("SHIP-7", fetched, Arg.Any<CancellationToken>());
        _log.Entries.Should().Contain(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    /// <summary>A light shuttle collecting at B13: a 40-unit hold, in orbit there.</summary>
    private static ShipModel Shuttle(IReadOnlyList<CargoItemModel>? cargo = null)
        => new("SHIP-7", SystemSymbol, B13, "IN_ORBIT", "CRUISE", 300, 300, CargoCurrent: (cargo ?? []).Sum(item => item.Units), CargoCapacity: 40, ShipType: "SHIP_LIGHT_SHUTTLE", MountSymbols: [], CargoInventory: cargo ?? []);

    private Task<GoalExecutionResult> StepAsync(ShipModel ship, MineForShuttleGoal job)
        => new MineForShuttleGoalExecutor(
                _ships,
                _goals,
                _port,
                _tradeContexts,
                _dock,
                _orbit,
                _bus,
                _log.For<MineForShuttleGoalExecutor>())
            .ExecuteStepAsync(ship, job, new ShipGoalContext(), CancellationToken.None);
}
