using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Slice 6.62 (D147): ship steps run in their own loop beside the plans', and "a plan and a step never write one ship at the
/// same time". A plan's write of a ship's goal waits for the ship's step; the step's own writes don't wait for itself.
/// </summary>
public sealed class StepGuardedShipGoalRepositoryTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(10);

    private readonly IShipGoalRepository _stored = Substitute.For<IShipGoalRepository>();
    private readonly ShipGoalStepGuard _guard = new();
    private readonly StepGuardedShipGoalRepository _goals;

    public StepGuardedShipGoalRepositoryTests()
    {
        _goals = new StepGuardedShipGoalRepository(_stored, _guard, NullLogger<StepGuardedShipGoalRepository>.Instance);
    }

    [Fact]
    public async Task APlansWrite_WaitsForTheShipsStep_WhoseOwnWriteDoesNotWait()
    {
        var stepGoal = new MoveToWaypointGoal { TargetWaypointSymbol = "X1-A1-B2" };
        var planGoal = new MoveToWaypointGoal { TargetWaypointSymbol = "X1-A1-C3" };
        var stepWrote = new TaskCompletionSource();
        var stepEnds = new TaskCompletionSource();
        var step = Task.Run(async () =>
        {
            _guard.TryEnter("SHIP-1").Should().BeTrue();
            try
            {
                await _goals.SetActiveGoalAsync("SHIP-1", stepGoal);
                stepWrote.SetResult();
                await stepEnds.Task;
            }
            finally
            {
                _guard.Exit("SHIP-1");
            }
        });
        await stepWrote.Task.WaitAsync(Long);

        var planWrite = _goals.SetActiveGoalAsync("SHIP-1", planGoal);
        await Task.Delay(100);

        planWrite.IsCompleted.Should().BeFalse("the ship's step still runs");
        await _stored.DidNotReceive().SetActiveGoalAsync("SHIP-1", planGoal, Arg.Any<CancellationToken>());

        stepEnds.SetResult();
        await step.WaitAsync(Long);
        await planWrite.WaitAsync(Long);

        Received.InOrder(() =>
        {
            _stored.SetActiveGoalAsync("SHIP-1", stepGoal, Arg.Any<CancellationToken>());
            _stored.SetActiveGoalAsync("SHIP-1", planGoal, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task AWrite_ToAnotherShip_DoesNotWaitForAStep()
    {
        await Task.Run(() => _guard.TryEnter("SHIP-1"));

        await _goals.ClearActiveGoalAsync("SHIP-2").WaitAsync(Long);

        await _stored.Received(1).ClearActiveGoalAsync("SHIP-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStep_FindsItsShipHeld_WhileAPlanWritesItsGoal_AndFreeAfter()
    {
        var writing = new TaskCompletionSource();
        var writeEnds = new TaskCompletionSource();
        _stored.ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            writing.SetResult();
            await writeEnds.Task;
        });

        var write = _goals.ClearActiveGoalAsync("SHIP-1");
        await writing.Task.WaitAsync(Long);

        (await Task.Run(() => _guard.TryEnter("SHIP-1"))).Should().BeFalse("a plan writes the ship's goal");
        writeEnds.SetResult();
        await write.WaitAsync(Long);
        (await Task.Run(() => _guard.TryEnter("SHIP-1"))).Should().BeTrue();
    }

    [Fact]
    public async Task AWrite_ThatWaitsTooLong_IsMadeAnyway()
    {
        // A step that holds its ship for minutes is stuck; the plan's decision isn't lost to it.
        var goals = new StepGuardedShipGoalRepository(_stored, _guard, NullLogger<StepGuardedShipGoalRepository>.Instance)
        {
            WaitAtMost = TimeSpan.FromMilliseconds(50),
        };
        await Task.Run(() => _guard.TryEnter("SHIP-1"));

        await goals.BlockGoalAsync("SHIP-1", Guid.Empty, "runaway").WaitAsync(Long);

        await _stored.Received(1).BlockGoalAsync("SHIP-1", Guid.Empty, "runaway", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AWaitThatTimesOut_ClaimsNothing()
    {
        await Task.Run(() => _guard.TryEnter("SHIP-1"));

        (await _guard.EnterAsync("SHIP-1", TimeSpan.FromMilliseconds(50), CancellationToken.None)).Should().BeFalse();
        _guard.Exit("SHIP-1");

        (await _guard.EnterAsync("SHIP-1", Long, CancellationToken.None)).Should().BeTrue("the step let go, and the wait left nothing held");
    }

    [Fact]
    public async Task AShip_IsHeldHere_OnlyInTheCallThatEnteredIt()
    {
        var heldInTheStep = await Task.Run(() => _guard.TryEnter("SHIP-1") && _guard.IsHeldHere("SHIP-1"));

        heldInTheStep.Should().BeTrue();
        _guard.IsHeldHere("SHIP-1").Should().BeFalse("another call holds it");
    }

    [Fact]
    public async Task Reads_DoNotWaitForAStep()
    {
        await Task.Run(() => _guard.TryEnter("SHIP-1"));

        await _goals.GetActiveGoalAsync("SHIP-1").WaitAsync(Long);

        await _stored.Received(1).GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
    }
}
