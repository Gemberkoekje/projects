using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

public sealed class MineAndSellGoalExecutorTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISurveyRepository _surveys = Substitute.For<ISurveyRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private MineAndSellGoalExecutor CreateExecutor()
    {
        // Default survey repository to return null (no survey available)
        _surveys.GetBestActiveSurveyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SurveyModel)null);

        _goals.GetActiveSurveyTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string TargetWaypointSymbol, string TargetDepositSymbol)>());

        return new(
            _ships,
            _goals,
            _agents,
            _surveys,
            _port,
            _bus,
            NullLogger<MineAndSellGoalExecutor>.Instance);
    }

    private static MineAndSellGoal Goal() =>
        new()
        {
            TradeSymbol = "IRON_ORE",
            SourceWaypointSymbol = "X1-AB-AST",
            SellWaypointSymbol = "X1-AB-MKT",
        };

    [Fact]
    public async Task ExecuteStepAsync_Mines_WhenNoTargetCargoPresent()
    {
        var ship = new ShipModel(
            "MINER-1",
            "X1-AB",
            "X1-AB-AST",
            "IN_ORBIT",
            "CRUISE",
            10,
            40,
            CargoCurrent: 0,
            CargoCapacity: 40,
            CargoInventory: []);

        _bus.InvokeAsync<ShipCommandResult>(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ShipCommandResult("MINER-1", Domain.Enums.ShipLocalStatus.InOrbit, "X1-AB", "X1-AB-AST"));

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _bus.Received(1).InvokeAsync<ShipCommandResult>(
            Arg.Is<MineResourceVolumeCommand>(c =>
                c.ShipSymbol == "MINER-1"
                && c.TradeSymbol == "IRON_ORE"
                && c.SourceWaypoint == "X1-AB-AST"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_NavigatesToSellWaypoint_WhenCargoPresentAndNotDockedAtSellMarket()
    {
        var ship = new ShipModel(
            "MINER-1",
            "X1-AB",
            "X1-AB-AST",
            "IN_ORBIT",
            "CRUISE",
            10,
            40,
            CargoCurrent: 10,
            CargoCapacity: 40,
            CargoInventory: [new CargoItemModel("IRON_ORE", 10)]);

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.ShipSymbol == "MINER-1" && c.DestinationWaypoint == "X1-AB-MKT"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_AssignsSurveyGoal_WhenNoUsableSurveyAndSurveyShipIsAvailable()
    {
        var ship = new ShipModel(
            "MINER-1",
            "X1-AB",
            "X1-AB-AST",
            "IN_ORBIT",
            "CRUISE",
            10,
            40,
            CargoCurrent: 0,
            CargoCapacity: 40,
            CargoInventory: [],
            MountSymbols: ["MOUNT_SURVEYOR_I"]);

        _bus.InvokeAsync<ShipCommandResult>(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ShipCommandResult("MINER-1", Domain.Enums.ShipLocalStatus.InOrbit, "X1-AB", "X1-AB-AST"));

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _goals.Received(1).SetActiveGoalAsync(
            "MINER-1",
            Arg.Is<SurveyWaypointGoal>(goal =>
                goal.TargetWaypointSymbol == "X1-AB-AST"
                && goal.TargetDepositSymbol == "IRON_ORE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_DoesNotAssignSurveyGoal_WhenTargetAlreadyActive()
    {
        var ship = new ShipModel(
            "MINER-1",
            "X1-AB",
            "X1-AB-AST",
            "IN_ORBIT",
            "CRUISE",
            10,
            40,
            CargoCurrent: 0,
            CargoCapacity: 40,
            CargoInventory: [],
            MountSymbols: ["MOUNT_SURVEYOR_I"]);

        var executor = CreateExecutor();

        _goals.GetActiveSurveyTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string TargetWaypointSymbol, string TargetDepositSymbol)>
            {
                ("X1-AB-AST", "IRON_ORE")
            });

        _bus.InvokeAsync<ShipCommandResult>(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ShipCommandResult("MINER-1", Domain.Enums.ShipLocalStatus.InOrbit, "X1-AB", "X1-AB-AST"));

        var result = await executor.ExecuteStepAsync(ship, Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _goals.DidNotReceive().SetActiveGoalAsync(
            Arg.Any<string>(),
            Arg.Is<SurveyWaypointGoal>(_ => true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_SellsAtMarket_WhenCargoPresentAndDocked()
    {
        var ship = new ShipModel(
            "MINER-1",
            "X1-AB",
            "X1-AB-MKT",
            "DOCKED",
            "CRUISE",
            10,
            40,
            CargoCurrent: 10,
            CargoCapacity: 40,
            CargoInventory: [new CargoItemModel("IRON_ORE", 10)]);

        _port.SellCargoAsync("MINER-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult(
                AgentSymbol: "AGENT",
                AgentCredits: 220_000,
                Cargo: new CargoModel(0, 40, []),
                Revenue: 15_000));

        _agents.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT", null, "X1-AB-HQ", 205_000, "FACTION", 1));

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).SellCargoAsync("MINER-1", "IRON_ORE", 10, Arg.Any<CancellationToken>());
        await _ships.Received(1).UpdateCargoAsync("MINER-1", Arg.Any<CargoModel>(), Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync("MINER-1", Arg.Any<MineAndSellGoal>(), Arg.Any<CancellationToken>());
    }
}
