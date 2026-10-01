using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Goals;

public sealed class ShipGoalExecutorServiceTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipGoalExecutor _executor = Substitute.For<IShipGoalExecutor>();
    private readonly IScoutAllMarketplacesPlanService _scoutPlanService = Substitute.For<IScoutAllMarketplacesPlanService>();
    private readonly IGoalStepCircuitBreaker _circuitBreaker = Substitute.For<IGoalStepCircuitBreaker>();

    private static readonly ShipModel FullFuelShip = new("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100);
    private static readonly ShipModel NotFullFuelShip = new("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100);

    private ShipGoalExecutorService CreateService() =>
        new(
            [_executor],
            _goals,
            _ships,
            _scoutPlanService,
            Substitute.For<ISettingsRepository>(),
            _circuitBreaker,
            Substitute.For<IAutomationMetrics>(),
            NullLogger<ShipGoalExecutorService>.Instance);

    [Fact]
    public async Task ExecuteAsync_WhenShipNotFound_ReturnsNull()
    {
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipModel)null!);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenFuelIsNotFull_ReturnsNull()
    {
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(NotFullFuelShip);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().BeNull();
        await _goals.Received(1).GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenActiveGoalIsNotScout_ReturnsNull()
    {
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(new IdleGoal());

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoExecutorCanHandleScoutGoal_ReturnsNull()
    {
        var scoutGoal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-009" };

        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(scoutGoal);
        _executor.CanExecute(scoutGoal).Returns(false);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().BeNull();
        await _executor.DidNotReceive().ExecuteStepAsync(
            Arg.Any<ShipModel>(),
            Arg.Any<ShipGoal>(),
            Arg.Any<ShipGoalContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenScoutGoalAndExecutorExists_ReturnsExecutorResult()
    {
        var scoutGoal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-009" };
        var expected = GoalExecutionResult.Completed("done");

        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(scoutGoal);
        _executor.CanExecute(scoutGoal).Returns(true);
        _executor.ExecuteStepAsync(
                FullFuelShip,
                scoutGoal,
                Arg.Any<ShipGoalContext>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().Be(expected);
        await _executor.Received(1).ExecuteStepAsync(
            FullFuelShip,
            scoutGoal,
            Arg.Is<ShipGoalContext>(c =>
                c.ActiveSurveyCount == 0 &&
                c.FuelMarketWaypoint == string.Empty &&
                !c.CurrentWaypointSellsFuel &&
                c.RecommendedFlightMode == string.Empty),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenGoalProgresses_DoesNotClearGoalOrAdvancePlan()
    {
        var scoutGoal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-009" };

        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(scoutGoal);
        _executor.CanExecute(scoutGoal).Returns(true);
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.Progressing("progress"));

        _ = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        await _goals.DidNotReceive().ClearActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _scoutPlanService.DidNotReceive().AdvanceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenActiveGoalIsTradeBetweenMarkets_DispatchesToExecutor()
    {
        var tradeGoal = new TradeBetweenMarketsGoal
        {
            TradeSymbol = "FOOD",
            BuyWaypointSymbol = "X1-AB-BUY",
            SellWaypointSymbol = "X1-AB-SELL",
        };
        var expected = GoalExecutionResult.Progressing("trade");

        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip with { CargoCapacity = 40, ShipType = "SHIP_LIGHT_HAULER" });
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(tradeGoal);
        _executor.CanExecute(tradeGoal).Returns(true);
        _executor.ExecuteStepAsync(
                Arg.Any<ShipModel>(),
                tradeGoal,
                Arg.Any<ShipGoalContext>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().Be(expected);
        await _executor.Received(1).ExecuteStepAsync(
            Arg.Any<ShipModel>(),
            tradeGoal,
            Arg.Any<ShipGoalContext>(),
            Arg.Any<CancellationToken>());
    }
}
