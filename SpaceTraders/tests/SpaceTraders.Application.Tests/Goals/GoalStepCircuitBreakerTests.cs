using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Goals;

public sealed class GoalStepCircuitBreakerTests
{
    private const int RecursionCap = 1_000;

    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();

    public GoalStepCircuitBreakerTests()
    {
        _settings.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task LoopingExecutor_TripsTheBreaker_AndItsGoalIsBlocked()
    {
        ShipGoal goal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-MKT" };
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-MKT", "IN_ORBIT", "CRUISE", 100, 100));
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(_ => goal);
        _goals.When(g => g.BlockGoalAsync("SHIP-1", goal.GoalId, Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(call => goal = goal with { Status = GoalStatus.Blocked, StatusReason = call.ArgAt<string>(2) });
        _settings.GetAsync<int>("Automation.CircuitBreaker.MaxGoalStepsPerMinute", Arg.Any<CancellationToken>()).Returns(60);

        var executor = new LoopingExecutor();
        var service = CreateService(executor);
        executor.Service = service;

        await service.ExecuteAsync("SHIP-1", CancellationToken.None);

        executor.Steps.Should().Be(60);
        goal.Status.Should().Be(GoalStatus.Blocked);
        goal.StatusReason.Should().Be("runaway");
        _metrics.Received(1).GoalBreakerTripped("SHIP-1");

        // The blocked goal is not stepped again, so a looping goal stays stopped.
        var next = await service.ExecuteAsync("SHIP-1", CancellationToken.None);
        next.Should().BeNull();
        executor.Steps.Should().Be(60);
    }

    [Fact]
    public async Task MissingSetting_FallsBackTo60StepsAMinute()
    {
        ShipGoal goal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-MKT" };
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-MKT", "IN_ORBIT", "CRUISE", 100, 100));
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(goal);

        var executor = new LoopingExecutor();
        var service = CreateService(executor);
        executor.Service = service;

        await service.ExecuteAsync("SHIP-1", CancellationToken.None);

        executor.Steps.Should().Be(60);
        await _goals.Received(1).BlockGoalAsync("SHIP-1", goal.GoalId, "runaway", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RecordStep_TripsOnlyAboveTheLimitWithinOneMinute()
    {
        var breaker = new GoalStepCircuitBreaker();

        for (var step = 0; step < 60; step++)
        {
            breaker.RecordStep("SHIP-1", 60, Start.AddMilliseconds(500 * step)).Should().BeFalse();
        }

        breaker.RecordStep("SHIP-1", 60, Start.AddSeconds(30)).Should().BeTrue();
    }

    [Fact]
    public void RecordStep_ForgetsStepsOlderThanOneMinute()
    {
        var breaker = new GoalStepCircuitBreaker();

        // The tick alone: one step every 5 seconds, for ten minutes.
        for (var step = 0; step < 120; step++)
        {
            breaker.RecordStep("SHIP-1", 12, Start.AddSeconds(5 * step)).Should().BeFalse();
        }
    }

    [Fact]
    public void RecordStep_CountsEachShipSeparately()
    {
        var breaker = new GoalStepCircuitBreaker();

        for (var step = 0; step < 3; step++)
        {
            breaker.RecordStep("SHIP-1", 3, Start).Should().BeFalse();
            breaker.RecordStep("SHIP-2", 3, Start).Should().BeFalse();
        }

        breaker.RecordStep("SHIP-1", 3, Start).Should().BeTrue();
        breaker.RecordStep("SHIP-3", 3, Start).Should().BeFalse();
    }

    [Fact]
    public void RecordStep_StartsCountingAgainAfterTripping()
    {
        var breaker = new GoalStepCircuitBreaker();

        for (var step = 0; step < 3; step++)
        {
            breaker.RecordStep("SHIP-1", 3, Start);
        }

        breaker.RecordStep("SHIP-1", 3, Start).Should().BeTrue();
        breaker.RecordStep("SHIP-1", 3, Start).Should().BeFalse();
    }

    private ShipGoalExecutorService CreateService(IShipGoalExecutor executor) =>
        new(
            [executor],
            _goals,
            _ships,
            Substitute.For<IScoutAllMarketplacesPlanService>(),
            _settings,
            new GoalStepCircuitBreaker(),
            _metrics,
            NullLogger<ShipGoalExecutorService>.Instance);

    /// <summary>Runs the next goal step from inside each step, like a handler that re-triggers itself.</summary>
    private sealed class LoopingExecutor : IShipGoalExecutor
    {
        public ShipGoalExecutorService? Service { get; set; }

        public int Steps { get; private set; }

        public bool CanExecute(ShipGoal goal) => true;

        public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
        {
            Steps++;
            if (Steps < RecursionCap)
            {
                await Service!.ExecuteAsync(ship.Symbol, ct);
            }

            return GoalExecutionResult.Progressing("Looping.");
        }
    }
}
