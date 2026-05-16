using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

public sealed class TradeBetweenMarketsGoalExecutorTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private TradeBetweenMarketsGoalExecutor CreateExecutor() =>
        new(
            _ships,
            _goals,
            _agents,
            _port,
            _dock,
            _bus,
            NullLogger<TradeBetweenMarketsGoalExecutor>.Instance);

    private static TradeBetweenMarketsGoal Goal() =>
        new()
        {
            TradeSymbol = "FOOD",
            BuyWaypointSymbol = "X1-AB-BUY",
            SellWaypointSymbol = "X1-AB-SELL",
        };

    [Fact]
    public async Task ExecuteStepAsync_NavigatesToBuyWaypoint_WhenNoCargoAndNotAtSource()
    {
        var ship = new ShipModel(
            "TRADER-1",
            "X1-AB",
            "X1-AB-OTHER",
            "DOCKED",
            "CRUISE",
            20,
            40,
            CargoCurrent: 0,
            CargoCapacity: 40,
            CargoInventory: []);

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.ShipSymbol == "TRADER-1" && c.DestinationWaypoint == "X1-AB-BUY"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_BuysCargo_WhenDockedAtSourceWithCapacity()
    {
        var ship = new ShipModel(
            "TRADER-1",
            "X1-AB",
            "X1-AB-BUY",
            "DOCKED",
            "CRUISE",
            20,
            40,
            CargoCurrent: 0,
            CargoCapacity: 40,
            CargoInventory: []);

        _port.GetMarketAsync("X1-AB", "X1-AB-BUY", Arg.Any<CancellationToken>())
            .Returns(new MarketDataModel("X1-AB-BUY", "X1-AB", null, null, null, null));

        _port.BuyCargoAsync("TRADER-1", "FOOD", 40, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult(
                AgentSymbol: "AGENT",
                AgentCredits: 180_000,
                Cargo: new CargoModel(40, 40, [new CargoItemModel("FOOD", 40)]),
                Revenue: 8_000));

        _agents.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT", null, "X1-AB-HQ", 188_000, "FACTION", 1));

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).BuyCargoAsync("TRADER-1", "FOOD", 40, Arg.Any<CancellationToken>());
        await _ships.Received(1).UpdateCargoAsync("TRADER-1", Arg.Any<CargoModel>(), Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync("TRADER-1", Arg.Any<TradeBetweenMarketsGoal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_ClampsBuyUnitsToMarketTradeVolume_WhenVolumeIsLowerThanCapacity()
    {
        var ship = new ShipModel(
            "TRADER-1",
            "X1-AB",
            "X1-AB-BUY",
            "DOCKED",
            "CRUISE",
            20,
            40,
            CargoCurrent: 0,
            CargoCapacity: 40,
            CargoInventory: []);

        _port.GetMarketAsync("X1-AB", "X1-AB-BUY", Arg.Any<CancellationToken>())
            .Returns(new MarketDataModel(
                "X1-AB-BUY",
                "X1-AB",
                "[{\"symbol\":\"FOOD\",\"tradeVolume\":6}]",
                null,
                null,
                null));

        _port.BuyCargoAsync("TRADER-1", "FOOD", 6, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult(
                AgentSymbol: "AGENT",
                AgentCredits: 180_000,
                Cargo: new CargoModel(6, 40, [new CargoItemModel("FOOD", 6)]),
                Revenue: 1_200));

        _agents.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT", null, "X1-AB-HQ", 188_000, "FACTION", 1));

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).BuyCargoAsync("TRADER-1", "FOOD", 6, Arg.Any<CancellationToken>());
        await _port.DidNotReceive().BuyCargoAsync("TRADER-1", "FOOD", 40, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_CompletesAndClearsGoal_WhenDockedAtDestination()
    {
        var ship = new ShipModel(
            "TRADER-1",
            "X1-AB",
            "X1-AB-SELL",
            "DOCKED",
            "CRUISE",
            20,
            40,
            CargoCurrent: 15,
            CargoCapacity: 40,
            CargoInventory: [new CargoItemModel("FOOD", 15)]);

        _port.SellCargoAsync("TRADER-1", "FOOD", 15, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult(
                AgentSymbol: "AGENT",
                AgentCredits: 205_000,
                Cargo: new CargoModel(0, 40, []),
                Revenue: 12_000));

        _agents.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT", null, "X1-AB-HQ", 193_000, "FACTION", 1));

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.Received(1).SellCargoAsync("TRADER-1", "FOOD", 15, Arg.Any<CancellationToken>());
        await _ships.Received(1).UpdateCargoAsync("TRADER-1", Arg.Any<CargoModel>(), Arg.Any<CancellationToken>());
        await _goals.Received(1).ClearActiveGoalAsync("TRADER-1", Arg.Any<CancellationToken>());
    }
}
