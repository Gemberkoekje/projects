using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// B1: one goal step for a ship that is already at its target. The bus runs
/// <see cref="NavigateToWaypointCommand"/> and <see cref="ShipNavigationCompletedEvent"/> through
/// their real handlers, inline, so a loop shows up as more than one executor step.
/// </summary>
public sealed class AlreadyAtDestinationLoopTests
{
    private const string Target = "X1-AB-TGT";
    private const int StepLimit = 10;

    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly INavigateSubCommand _navigate = Substitute.For<INavigateSubCommand>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private int _steps;
    private int _completedEvents;

    [Fact]
    public async Task ScoutShipInOrbitAtTarget_TakesOneStep()
    {
        var executor = new ScoutWaypointGoalExecutor(Substitute.For<IWaypointVisitService>(), _dock, _bus);

        await RunOneStepAsync(
            executor,
            Ship("SCOUT-1", "IN_ORBIT"),
            new ScoutWaypointGoal { TargetWaypointSymbol = Target });

        AssertOneStepWithoutNavigation();
    }

    [Fact]
    public async Task SurveyShipDockedAtTarget_TakesOneStep()
    {
        var executor = new SurveyWaypointGoalExecutor(
            Substitute.For<ISpaceTradersPort>(),
            Substitute.For<ISurveyRepository>(),
            _orbit,
            _bus,
            NullLogger<SurveyWaypointGoalExecutor>.Instance);

        await RunOneStepAsync(
            executor,
            Ship("SURVEYOR-1", "DOCKED"),
            new SurveyWaypointGoal { TargetWaypointSymbol = Target, TargetDepositSymbol = "IRON_ORE" });

        AssertOneStepWithoutNavigation();
    }

    [Fact]
    public async Task ProbeInOrbitAtTarget_TakesOneStep()
    {
        var executor = new DeployProbeGoalExecutor(
            _goals,
            Substitute.For<IProbeDeploymentPlanService>(),
            _dock,
            _bus,
            NullLogger<DeployProbeGoalExecutor>.Instance);

        await RunOneStepAsync(
            executor,
            Ship("PROBE-1", "IN_ORBIT"),
            new DeployProbeGoal { TargetWaypointSymbol = Target });

        AssertOneStepWithoutNavigation();
    }

    [Fact]
    public async Task MinerWithCargoInOrbitAtSellMarket_TakesOneStep()
    {
        var executor = new MineAndSellGoalExecutor(
            _ships,
            _goals,
            Substitute.For<IAgentRepository>(),
            Substitute.For<ISurveyRepository>(),
            Substitute.For<ISpaceTradersPort>(),
            _dock,
            _bus,
            NullLogger<MineAndSellGoalExecutor>.Instance);

        await RunOneStepAsync(
            executor,
            Ship("MINER-1", "IN_ORBIT") with { CargoCurrent = 10, CargoCapacity = 40, CargoInventory = [new CargoItemModel("IRON_ORE", 10)] },
            new MineAndSellGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = "X1-AB-AST", SellWaypointSymbol = Target });

        AssertOneStepWithoutNavigation();
    }

    private static ShipModel Ship(string symbol, string status) =>
        new(symbol, "X1-AB", Target, status, "CRUISE", 100, 100);

    private async Task RunOneStepAsync(IShipGoalExecutor executor, ShipModel ship, ShipGoal goal)
    {
        _ships.FindAsync(ship.Symbol, Arg.Any<CancellationToken>()).Returns(ship);
        _goals.GetActiveGoalAsync(ship.Symbol, Arg.Any<CancellationToken>()).Returns(goal);

        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var goalExecutor = new ShipGoalExecutorService(
            [new CountingExecutor(executor, () => _steps++)],
            _goals,
            _ships,
            Substitute.For<IScoutAllMarketplacesPlanService>(),
            settings,
            Substitute.For<IGoalStepCircuitBreaker>(),
            new ShipGoalStepGuard(),
            Substitute.For<IAutomationMetrics>(),
            NullLogger<ShipGoalExecutorService>.Instance);

        var navigateHandler = new NavigateToWaypointHandler(
            _ships,
            _goals,
            Substitute.For<IWaypointRepository>(),
            _orbit,
            _navigate,
            Substitute.For<IRefuelSubCommand>(),
            _bus,
            NullLogger<NavigateToWaypointHandler>.Instance);

        var completedHandler = new ShipNavigationCompletedHandler(
            goalExecutor,
            Substitute.For<IDashboardNotifier>(),
            NullLogger<ShipNavigationCompletedHandler>.Instance);

        _bus.InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(call => navigateHandler.Handle((NavigateToWaypointCommand)call[0], CancellationToken.None));

        // Deliver the event inline, like the local queue would, until the step limit stops a loop.
        _bus.PublishAsync(Arg.Any<ShipNavigationCompletedEvent>(), Arg.Any<DeliveryOptions?>())
            .Returns(call =>
            {
                _completedEvents++;
                return _steps < StepLimit
                    ? new ValueTask(completedHandler.Handle((ShipNavigationCompletedEvent)call[0], CancellationToken.None))
                    : ValueTask.CompletedTask;
            });

        await goalExecutor.ExecuteAsync(ship.Symbol, CancellationToken.None);
    }

    private void AssertOneStepWithoutNavigation()
    {
        _steps.Should().Be(1);
        _completedEvents.Should().Be(0);
        _navigate.ReceivedCalls().Should().BeEmpty();
    }

    private sealed class CountingExecutor(IShipGoalExecutor inner, Action onStep) : IShipGoalExecutor
    {
        public bool CanExecute(ShipGoal goal) => inner.CanExecute(goal);

        public Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
        {
            onStep();
            return inner.ExecuteStepAsync(ship, goal, ctx, ct);
        }
    }
}
