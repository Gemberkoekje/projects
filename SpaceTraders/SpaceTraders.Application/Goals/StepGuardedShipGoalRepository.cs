using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Goals;

/// <summary>
/// The ships' goals, as every caller reads and writes them (PLAN.md slice 6.62, D147: "a plan and a step never write one ship
/// at the same time"). Ship steps run in their own loop beside the plans', so a plan may give a ship a goal while the ship's
/// step works on the old one, and the step would then write its progress over the plan's goal. A write here waits until no
/// step of the ship runs, and holds the ship's next step off until it is done (<see cref="IShipGoalStepGuard"/>); a ship's own
/// step, which holds the ship already, writes without waiting.
/// </summary>
/// <remarks>
/// Reads go straight through: a plan decides on what it read, and the ship may move on meanwhile, as an arrival's step always
/// could. A write that waits longer than <see cref="WaitAtMost"/> for a step is made anyway, as before this slice, and logged:
/// a step that holds its ship that long is stuck, and the plan's decision shouldn't be lost to it.
/// </remarks>
/// <param name="goals">The goals as stored.</param>
/// <param name="stepGuard">Who holds each ship.</param>
/// <param name="logger">Where a write that couldn't wait is logged.</param>
public sealed class StepGuardedShipGoalRepository(
    IShipGoalRepository goals,
    IShipGoalStepGuard stepGuard,
    ILogger<StepGuardedShipGoalRepository> logger) : IShipGoalRepository
{
    /// <summary>
    /// How long a write waits for a step of its ship at most: 2 minutes, where a step takes seconds, its API calls queued with
    /// the rest. Shorter in tests.
    /// </summary>
    internal TimeSpan WaitAtMost { get; init; } = TimeSpan.FromMinutes(2);

    /// <inheritdoc />
    public Task<ShipGoal?> GetActiveGoalAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => goals.GetActiveGoalAsync(shipSymbol, cancellationToken);

    /// <inheritdoc />
    public Task SetActiveGoalAsync(string shipSymbol, ShipGoal goal, CancellationToken cancellationToken = default)
        => WriteAsync(shipSymbol, () => goals.SetActiveGoalAsync(shipSymbol, goal, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task ClearActiveGoalAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => WriteAsync(shipSymbol, () => goals.ClearActiveGoalAsync(shipSymbol, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task UpdateGoalStatusAsync(string shipSymbol, Guid goalId, GoalStatus status, CancellationToken cancellationToken = default)
        => WriteAsync(shipSymbol, () => goals.UpdateGoalStatusAsync(shipSymbol, goalId, status, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task BlockGoalAsync(string shipSymbol, Guid goalId, string reason, CancellationToken cancellationToken = default)
        => WriteAsync(shipSymbol, () => goals.BlockGoalAsync(shipSymbol, goalId, reason, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> GetActiveScoutTargetsAsync(CancellationToken cancellationToken = default)
        => goals.GetActiveScoutTargetsAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, TradeBetweenMarketsGoal>> GetActiveTradeGoalsAsync(CancellationToken cancellationToken = default)
        => goals.GetActiveTradeGoalsAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, SupplyConstructionGoal>> GetActiveConstructionGoalsAsync(CancellationToken cancellationToken = default)
        => goals.GetActiveConstructionGoalsAsync(cancellationToken);

    /// <summary>Makes a write of the ship's goal while no step of the ship runs, unless the caller is that step.</summary>
    private async Task WriteAsync(string shipSymbol, Func<Task> write, CancellationToken cancellationToken)
    {
        if (stepGuard.IsHeldHere(shipSymbol))
        {
            await write();
            return;
        }

        if (!await stepGuard.EnterAsync(shipSymbol, WaitAtMost, cancellationToken))
        {
            logger.LogWarning(
                "A goal step of ship {ShipSymbol} has held it for more than {Minutes} minutes; its goal is written without waiting for the step.",
                shipSymbol,
                WaitAtMost.TotalMinutes);
            await write();
            return;
        }

        try
        {
            await write();
        }
        finally
        {
            stepGuard.Exit(shipSymbol);
        }
    }
}
