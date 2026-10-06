using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
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
    private readonly ITripBook _trips = Substitute.For<ITripBook>();
    private readonly ShipGoalStepGuard _stepGuard = new();
    private readonly IShipEventScheduler _scheduler = Substitute.For<IShipEventScheduler>();
    private readonly LostArrivals _lostArrivals;
    private readonly LogRecorder _log = new();

    private static readonly ShipModel FullFuelShip = new("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100);
    private static readonly ShipModel NotFullFuelShip = new("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100);

    public ShipGoalExecutorServiceTests()
    {
        // Automation and every plan switched on, unless a test says otherwise.
        _settings.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _lostArrivals = new LostArrivals(_scheduler);
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
            _trips,
            _lostArrivals,
            _log.For<ShipGoalExecutorService>());

    [Fact]
    public async Task ExecuteAsync_AShipStoredInTransitLongPastItsArrival_HasItsArrivalScheduledAgain_OnceAMargin()
    {
        // B70, seen on 2026-10-06: SPECTER-5's arrival at gas giant C45, due at 00:42:53Z, fell in a 35-second outage of the
        // game's API. The dock failed four times and the arrival was dropped; the cache kept the ship in transit, nothing
        // scheduled its arrival again, and every step of its SiphonAndSell goal waited for it, for hours, until a restart.
        var siphon = new SiphonAndSellGoal { TradeSymbol = "LIQUID_NITROGEN", SourceWaypointSymbol = "X1-AB-045", SellWaypointSymbol = "X1-AB-051" };
        var lost = new ShipModel("SHIP-1", "X1-AB", "X1-AB-045", "IN_TRANSIT", "CRUISE", 37, 80, ArrivesAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(lost);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(siphon);
        _executor.CanExecute(siphon).Returns(true);
        _executor.ExecuteStepAsync(lost, siphon, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.WaitingForArrival("In transit to X1-AB-045."));

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);
        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        await _scheduler.Received(1).ScheduleArrivalAsync("SHIP-1", siphon.GoalId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        _log.Entries.Should().ContainSingle(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning && entry.Message.Contains("scheduled again"));
    }

    [Fact]
    public async Task ExecuteAsync_AShipWhoseArrivalIsJustDue_IsLeftToItsArrival()
    {
        // An arrival is handled within seconds of its time: the scheduler fires it, and the dock takes the ship out of transit.
        var siphon = new SiphonAndSellGoal { TradeSymbol = "LIQUID_NITROGEN", SourceWaypointSymbol = "X1-AB-045", SellWaypointSymbol = "X1-AB-051" };
        var landing = new ShipModel("SHIP-1", "X1-AB", "X1-AB-045", "IN_TRANSIT", "CRUISE", 37, 80, ArrivesAt: DateTimeOffset.UtcNow.AddSeconds(-20));
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(landing);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(siphon);
        _executor.CanExecute(siphon).Returns(true);
        _executor.ExecuteStepAsync(landing, siphon, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>())
            .Returns(GoalExecutionResult.WaitingForArrival("In transit to X1-AB-045."));

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        await _scheduler.DidNotReceiveWithAnyArgs().ScheduleArrivalAsync(default!, default, default, default);
    }

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
        await _trips.DidNotReceiveWithAnyArgs().BookAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ATripTheBreakerBlocks_IsBooked()
    {
        // D46: a blocked goal stays blocked until its plan replaces it, so the trip ends here. Unbooked, its purchase
        // would be missing, and the trip that sells its cargo after would look like profit.
        var trade = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = "X1-AB-001", SellWaypointSymbol = "X1-AB-002", CargoBought = true, Spent = 8_000 };
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(trade);
        _executor.CanExecute(trade).Returns(true);
        _circuitBreaker.RecordStep("SHIP-1", Arg.Any<int>(), Arg.Any<DateTimeOffset>()).Returns(true);

        await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        await _goals.Received(1).BlockGoalAsync("SHIP-1", trade.GoalId, "runaway", Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-1", trade, "runaway", Arg.Any<CancellationToken>());
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
            _trips,
            _lostArrivals,
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
    [InlineData("Survey")]
    [InlineData("Explore")]
    [InlineData("Construction")]
    public async Task ExecuteAsync_WhenTheGoalsPlanIsSwitchedOff_DoesNotStep(string plan)
    {
        ShipGoal goal = plan switch
        {
            "Scout" => new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-009" },
            "Explore" => new JumpGoal { GateWaypointSymbol = "X1-AB-I55", DestinationGateWaypointSymbol = "X1-CD-AF5F" },
            "Construction" => new SupplyConstructionGoal { TradeSymbol = "FAB_MATS", ConstructionSiteWaypointSymbol = "X1-AB-I55", BuyWaypointSymbol = "X1-AB-009", Units = 40 },
            "ProbeDeployment" => new DeployProbeGoal { TargetWaypointSymbol = "X1-AB-009" },
            "Mining" => new MineAndSellGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = "X1-AB-AST", SellWaypointSymbol = "X1-AB-009" },
            "Survey" => new MoveToWaypointGoal { TargetWaypointSymbol = "X1-AB-009", Drifting = true },
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
    public async Task ExecuteAsync_WhenActiveGoalIsAMove_DispatchesToExecutor()
    {
        // B56, seen on the cluster on 2026-10-03: the survey plan gave SPECTER-F a move to B7 at 15:55:03Z (D54), and no step
        // of it ever ran: this service steps only the goal types it lists, and the move wasn't one of them. SPECTER-F stayed
        // at XB5C with a goal that never ended, so the survey plan gave it nothing else either.
        var move = new MoveToWaypointGoal { TargetWaypointSymbol = "X1-AB-B7", Drifting = true };
        var expected = GoalExecutionResult.WaitingForArrival("drifting");
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(move);
        _executor.CanExecute(move).Returns(true);
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), move, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().Be(expected);
        await _executor.Received(1).ExecuteStepAsync(Arg.Any<ShipModel>(), move, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Jump")]
    [InlineData("ExploreSystem")]
    public async Task ExecuteAsync_WhenActiveGoalExplores_DispatchesToExecutor(string kind)
    {
        // Exploring (asked on 2026-10-04) brings two goal types, which this service must list, as B56's move showed.
        ShipGoal goal = kind == "Jump"
            ? new JumpGoal { GateWaypointSymbol = "X1-AB-I55", DestinationGateWaypointSymbol = "X1-CD-AF5F" }
            : new ExploreSystemGoal { SystemSymbol = "X1-CD", Stops = ["X1-CD-AF5F"] };
        var expected = GoalExecutionResult.Progressing("exploring");
        _settings.GetAsync<bool>("Automation.Plan.Explore.Enabled", Arg.Any<CancellationToken>()).Returns(true);
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(goal);
        _executor.CanExecute(goal).Returns(true);
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), goal, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().Be(expected);
        await _executor.Received(1).ExecuteStepAsync(Arg.Any<ShipModel>(), goal, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("MineForShuttle")]
    [InlineData("CollectOre")]
    public async Task ExecuteAsync_WhenActiveGoalCollectsAtAFarAsteroid_DispatchesToExecutor(string kind)
    {
        // Slice 6.18 (D83) brings two goal types, which this service must list, as B56's move showed.
        ShipGoal goal = kind == "MineForShuttle"
            ? new MineForShuttleGoal { TradeSymbol = "GOLD_ORE", AsteroidWaypointSymbol = "X1-AB-B44", SellWaypointSymbol = "X1-AB-B7" }
            : new CollectOreGoal { AsteroidWaypointSymbol = "X1-AB-B44", SellWaypointSymbol = "X1-AB-B7" };
        var expected = GoalExecutionResult.WaitingForCooldown("waiting");
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip);
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(goal);
        _executor.CanExecute(goal).Returns(true);
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), goal, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().Be(expected);
        await _executor.Received(1).ExecuteStepAsync(Arg.Any<ShipModel>(), goal, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenActiveGoalIsAConstructionTrip_DispatchesToExecutor()
    {
        // Slice 6.6: the construction plan's trips are stepped like the other plans' goals.
        var trip = new SupplyConstructionGoal { TradeSymbol = "FAB_MATS", ConstructionSiteWaypointSymbol = "X1-AB-I55", BuyWaypointSymbol = "X1-AB-F49", Units = 40 };
        var expected = GoalExecutionResult.WaitingForArrival("to the market");
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(FullFuelShip with { CargoCapacity = 40, ShipType = "SHIP_LIGHT_SHUTTLE" });
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(trip);
        _executor.CanExecute(trip).Returns(true);
        _executor.ExecuteStepAsync(Arg.Any<ShipModel>(), trip, Arg.Any<ShipGoalContext>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateService().ExecuteAsync("SHIP-1", CancellationToken.None);

        result.Should().Be(expected);
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
