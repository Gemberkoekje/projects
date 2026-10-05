using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using Wolverine;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Slice 6.18, D83: "When the light shuttle is full, it sells the ore at the market, then comes back." The shuttle flies to
/// B13, waits in orbit while the parked drones hand it their ore, and with its hold full sells everything at B7; the round is
/// booked as a trip and the mining plan gives the next.
/// </summary>
public sealed class CollectOreGoalExecutorTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IMarketRefresher _refresher = Substitute.For<IMarketRefresher>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly ICargoJettison _jettison = Substitute.For<ICargoJettison>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly ITripBook _trips = Substitute.For<ITripBook>();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);

    private static readonly CollectOreGoal Round = new() { AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7 };

    public CollectOreGoalExecutorTests()
    {
        // B7 buys iron, 20 at a time, and no ice.
        var map = Map(
        [
            .. Markets().Where(market => market.WaypointSymbol != B7),
            Market(B7, Good("IRON_ORE", "IMPORT", 118, 61, 20, "LIMITED"), Good("FUEL", "EXCHANGE", 79, 71, 180, "MODERATE")),
        ]);
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new TradeContext(map, 129_357, 200));
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
    }

    [Fact]
    public async Task AwayFromTheAsteroid_ItFliesThere()
    {
        var result = await StepAsync(Shuttle(waypoint: B7, status: "DOCKED"), Round);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == B13 && command.FlightMode == "CRUISE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DockedAtTheAsteroid_ItOrbits_WhereTheDronesHandOver()
    {
        var result = await StepAsync(Shuttle(status: "DOCKED"), Round);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _orbit.Received(1).ExecuteAsync("SHIP-7", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheAsteroid_ItWaits_WhileADroneMinesThere_WithoutAnApiCall()
    {
        var shuttle = Shuttle(cargo: [new CargoItemModel("IRON_ORE", 10)]);
        var drone = Drone(waypoint: B13, status: "IN_ORBIT");
        Fleet(shuttle, drone);
        _activeGoals["SHIP-3"] = new MineForShuttleGoal { TradeSymbol = "IRON_ORE", AsteroidWaypointSymbol = B13, SellWaypointSymbol = B7 };

        var result = await StepAsync(shuttle, Round);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
        result.Reason.Should().Contain("10 of 40");
        _port.ReceivedCalls().Should().BeEmpty();
        await _goals.DidNotReceiveWithAnyArgs().SetActiveGoalAsync(default!, default!, default);
    }

    [Fact]
    public async Task WithAFullHold_ItSetsOffToSell()
    {
        await StepAsync(Shuttle(cargo: [new CargoItemModel("IRON_ORE", 40)]), Round);

        await _goals.Received(1).SetActiveGoalAsync("SHIP-7", Arg.Is<CollectOreGoal>(goal => goal.Selling && goal.GoalId == Round.GoalId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithAPartHold_AndNoDroneLeftThere_ItSetsOffToSell()
    {
        // Nothing more would come: the drones' place at the collection point was given up.
        var shuttle = Shuttle(cargo: [new CargoItemModel("IRON_ORE", 12)]);
        Fleet(shuttle, Drone());

        await StepAsync(shuttle, Round);

        await _goals.Received(1).SetActiveGoalAsync("SHIP-7", Arg.Is<CollectOreGoal>(goal => goal.Selling), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Selling_DockedAtTheMarket_ItSellsWhatItHolds_InBatches_JettisonsWhatNobodyBuys_AndTheRoundEnds()
    {
        // Its hold as the API has it: the drones' transfers wrote the cache from what they handed over.
        _port.GetShipCargoAsync("SHIP-7", Arg.Any<CancellationToken>())
            .Returns(new CargoModel(40, 40, [new CargoItemModel("IRON_ORE", 30), new CargoItemModel("ICE_WATER", 10)]));
        _port.SellCargoAsync("SHIP-7", "IRON_ORE", 20, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT", 130_577, new CargoModel(20, 40, [new CargoItemModel("IRON_ORE", 10), new CargoItemModel("ICE_WATER", 10)]), 1_220));
        _port.SellCargoAsync("SHIP-7", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT", 131_167, new CargoModel(10, 40, [new CargoItemModel("ICE_WATER", 10)]), 590));

        var result = await StepAsync(Shuttle(waypoint: B7, status: "DOCKED", cargo: [new CargoItemModel("IRON_ORE", 40)]), Round with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _bus.Received(2).PublishAsync(Arg.Is<ShipCargoSoldEvent>(sold => sold.ShipSymbol == "SHIP-7" && sold.WaypointSymbol == B7), Arg.Any<DeliveryOptions>());
        await _jettison.Received(1).JettisonAsync(Arg.Any<ShipModel>(), Arg.Is<CargoItemModel>(item => item.Symbol == "ICE_WATER" && item.Units == 10), "no_buyer", Arg.Any<CancellationToken>());
        await _refresher.Received(1).RefreshAfterTradeAsync(SystemSymbol, B7, "SHIP-7", Arg.Any<CancellationToken>());
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-7", Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-7", Arg.Is<TripGoal>(trip => trip.Earned == 1_810), TripBook.Sold, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Selling_InOrbitAtTheMarket_ItDocksFirst()
    {
        var result = await StepAsync(Shuttle(waypoint: B7, status: "IN_ORBIT", cargo: [new CargoItemModel("IRON_ORE", 40)]), Round with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _dock.Received(1).ExecuteAsync("SHIP-7", Arg.Any<CancellationToken>());
    }

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    /// <summary>A light shuttle: a 40-unit hold and a 300-unit tank, by default in orbit at B13.</summary>
    private static ShipModel Shuttle(string waypoint = B13, string status = "IN_ORBIT", IReadOnlyList<CargoItemModel>? cargo = null)
        => new("SHIP-7", SystemSymbol, waypoint, status, "CRUISE", 300, 300, CargoCurrent: (cargo ?? []).Sum(item => item.Units), CargoCapacity: 40, ShipType: "SHIP_LIGHT_SHUTTLE", MountSymbols: [], CargoInventory: cargo ?? []);

    private Task<GoalExecutionResult> StepAsync(ShipModel ship, CollectOreGoal round)
        => new CollectOreGoalExecutor(
                _ships,
                _goals,
                _agents,
                _port,
                _tradeContexts,
                _refresher,
                _dock,
                _orbit,
                _jettison,
                _bus,
                _trips,
                _log.For<CollectOreGoalExecutor>())
            .ExecuteStepAsync(ship, round, new ShipGoalContext(), CancellationToken.None);
}
