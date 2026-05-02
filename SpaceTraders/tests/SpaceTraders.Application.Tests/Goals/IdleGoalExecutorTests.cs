using FluentAssertions;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Phase 10: unit tests for <see cref="IdleGoalExecutor"/>.
/// </summary>
public sealed class IdleGoalExecutorTests
{
    private static readonly IdleGoalExecutor Executor = new();

    private static ShipModel Ship(string status = "IN_ORBIT") =>
        new("SHIP-1", "X1-AB", "X1-AB-WP1", status, "CRUISE", 80, 100);

    [Fact]
    public void CanExecute_ReturnsTrue_ForIdleGoal()
    {
        Executor.CanExecute(new IdleGoal()).Should().BeTrue();
    }

    [Fact]
    public void CanExecute_ReturnsFalse_ForOtherGoals()
    {
        Executor.CanExecute(new MoveToWaypointGoal { TargetWaypointSymbol = "X1-AB-WP2" }).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteStepAsync_ReturnsWaitingForArrival()
    {
        var result = await Executor.ExecuteStepAsync(Ship(), new IdleGoal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
    }
}
