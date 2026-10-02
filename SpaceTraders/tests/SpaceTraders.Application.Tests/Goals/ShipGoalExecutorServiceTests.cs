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
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly ShipGoalStepGuard _stepGuard = new();
    private readonly LogRecorder _log = new();

    private static readonly ShipModel FullFuelShip = new("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100);
    private static readonly ShipModel NotFullFuelShip = new("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100);

    public ShipGoalExecutorServiceTests()
    {
        // Automation and every plan switched on, unless a test says otherwise.
        _settings.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private ShipGoalExecutorService CreateService() =>
        new(
            [_executor],
            _goals,
            _ships,
            _scoutPlanService,
            _settings,
            _circuitBreaker,
            _stepGuard,
            _metrics,
            _log.For<ShipGoalExecutorService>());

    [Fact]
    public async Task ExecuteAsync_WhenTheBreakerTrips_JournalsTheShipAsBlocked()
    {
        var scoutGoal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-009" };
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(scoutGoal);
        _executor.CanExecute(scoutGoal).Returns(true);
        _circuitBreaker.RecordStep("SHIP-1", Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(true);

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        var blocked = _log.Journal.Should().ContainSingle().Subject;
        blocked.EventKind.Should().Be("ShipBlocked");
        blocked.Properties["ShipSymbol"].Should().Be("SHIP-1");
        blocked.Properties["Reason"].Should().Be("runaway");
    }

    [Fact]
    public async Task ExecuteAsync_AStepForAShipWhoseStepIsStillRunning_IsSkipped()
    {
        // B46: the tick steps every ship, and an arrival steps the ship it docks. Both could run one
        // ship's step at once (B45 was the scout plan's case), and a trade step would buy twice.
        var tradeGoal = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = "X1-AB-001", SellWaypointSymbol = "X1-AB-002" };
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(tradeGoal);
        _executor.CanExecute(tradeGoal).Returns(true);
        var buying = new TaskCompletionSource<GoalExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _executor.ExecuteStepAsync(FullFuelShip, tradeGoal, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => buying.Task);

        var tickStep = CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);
        var arrivalStep = CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);
        buying.SetResult(GoalExecutionResult.Progressing("bought"));
        var results = await Task.WhenAll(tickStep, arrivalStep);

        await _executor.Received(1).ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>());
        results.Should().ContainSingle(result => result == null, "the second step finds the ship busy and is skipped");

        // Once the first step is done, the ship takes steps again.
        _executor.ExecuteStepAsync(FullFuelShip, tradeGoal, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.Progressing("selling"));
        (await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_CountsTheStep_ByGoalKind()
    {
        // B11: goal steps by kind are one of the metrics phase 2 needs.
        var tradeGoal = new TradeBetweenMarketsGoal
        {
            BuyWaypointSymbol = "X1-AB-001",
            SellWaypointSymbol = "X1-AB-002",
            TradeSymbol = "FOOD",
        };
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(tradeGoal);
        _executor.CanExecute(tradeGoal).Returns(true);
        _executor.ExecuteStepAsync(FullFuelShip, tradeGoal, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.Progressing("buying"));

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        _metrics.Received(1).GoalStep("TradeBetweenMarkets");
    }

    [Fact]
    public async Task ExecuteAsync_ForABlockedGoal_CountsNoStep()
    {
        var blocked = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-009", Status = Domain.Enums.GoalStatus.Blocked };
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(blocked);
        _executor.CanExecute(blocked).Returns(true);

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        _metrics.DidNotReceive().GoalStep(Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ForTheCommandShipAfterScouting_KeepsNoLogLines()
    {
        // B12: once the scout plan has completed, the command ship keeps its last scout goal (B10),
        // so every tick completed that goal again and wrote two lines at Information.
        var log = new LogRecorder();
        var scoutGoal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-001" };
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(scoutGoal);
        _executor.CanExecute(scoutGoal).Returns(true);
        _executor.ExecuteStepAsync(FullFuelShip, scoutGoal, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.Completed("Scout waypoint visited."));
        var scoutPlans = Substitute.For<IScoutPlanRepository>();
        scoutPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ScoutAllMarketplacesPlanState
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = "SHIP-1",
            StartWaypointSymbol = "X1-AB-001",
            RouteWaypointSymbols = ["X1-AB-001"],
            CurrentRouteIndex = 0,
            Status = ScoutPlanStatus.Completed,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        });
        var scoutPlanService = new ScoutAllMarketplacesPlanService(
            scoutPlans,
            Substitute.For<IScoutShipSelectionService>(),
            Substitute.For<IScoutMarketplaceDiscoveryService>(),
            Substitute.For<IMarketplaceRoutePlanner>(),
            Substitute.For<IShipAssignmentRepository>(),
            _goals,
            log.For<ScoutAllMarketplacesPlanService>());
        var service = new ShipGoalExecutorService(
            [_executor],
            _goals,
            _ships,
            scoutPlanService,
            _settings,
            new GoalStepCircuitBreaker(),
            _stepGuard,
            Substitute.For<IAutomationMetrics>(),
            log.For<ShipGoalExecutorService>());

        for (var tick = 0; tick < 12; tick++)
        {
            await service.ExecuteAsync("SHIP-1", CancellationToken.None);
        }

        log.Kept.Should().BeEmpty("a minute of ticks with nothing new to say");
    }

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
    public async Task ExecuteAsync_WhenAutomationIsSwitchedOff_DoesNotStep()
    {
        var scoutGoal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-009" };
        _settings.GetAsync<bool>("Automation.Enabled", Arg.Any<CancellationToken>()).Returns(false);
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(scoutGoal);
        _executor.CanExecute(scoutGoal).Returns(true);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().BeNull();
        await _executor.DidNotReceive().ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Scout")]
    [InlineData("ProbeDeployment")]
    [InlineData("Mining")]
    [InlineData("Trading")]
    public async Task ExecuteAsync_WhenTheGoalsPlanIsSwitchedOff_DoesNotStep(string plan)
    {
        ShipGoal goal = plan switch
        {
            "Scout" => new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-009" },
            "ProbeDeployment" => new DeployProbeGoal { TargetWaypointSymbol = "X1-AB-009" },
            "Mining" => new MineAndSellGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = "X1-AB-AST", SellWaypointSymbol = "X1-AB-009" },
            _ => new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = "X1-AB-001", SellWaypointSymbol = "X1-AB-009" },
        };
        _settings.GetAsync<bool>($"Automation.Plan.{plan}.Enabled", Arg.Any<CancellationToken>()).Returns(false);
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(goal);
        _executor.CanExecute(goal).Returns(true);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().BeNull();
        await _executor.DidNotReceive().ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheGoalIsBlocked_DoesNotStep()
    {
        var scoutGoal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-009", Status = Domain.Enums.GoalStatus.Blocked, StatusReason = "runaway" };
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(scoutGoal);
        _executor.CanExecute(scoutGoal).Returns(true);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().BeNull();
        await _executor.DidNotReceive().ExecuteStepAsync(Arg.Any<ShipModel>(), Arg.Any<ShipGoal>(), Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>());
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
        await _scoutPlanService.DidNotReceive().AdvanceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
