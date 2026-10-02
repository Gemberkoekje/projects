using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="SurveyWaypointGoal"/>: one survey (PLAN.md slice 6.4). The ship flies to the
/// asteroid, through refuelling stops when it must, enters orbit, waits for its cooldown, surveys and
/// stores what it found. Then the goal ends, and the survey plan gives the ship its next target, which
/// may be the same asteroid again.
/// </summary>
public sealed class SurveyWaypointGoalExecutor(
    ISpaceTradersPort port,
    IShipRepository ships,
    IShipGoalRepository goals,
    ISurveyKeeper surveyKeeper,
    ITradeContextReader tradeContexts,
    IOrbitSubCommand orbit,
    IDockSubCommand dock,
    IMessageBus bus,
    ILogger<SurveyWaypointGoalExecutor> logger) : IShipGoalExecutor
{
    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is SurveyWaypointGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var surveyGoal = (SurveyWaypointGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Survey ship is in transit.");
        }

        if (!string.Equals(ship.WaypointSymbol, surveyGoal.TargetWaypointSymbol, StringComparison.OrdinalIgnoreCase))
        {
            var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
            return await GoalFlight.TowardsAsync(context.Map, ship, surveyGoal.TargetWaypointSymbol, dock, bus, ct);
        }

        // An arrival docks the ship; a survey needs orbit.
        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            await orbit.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing(
                $"Entering orbit at survey waypoint {surveyGoal.TargetWaypointSymbol}.");
        }

        var now = TimeProvider.System.GetUtcNow();
        if (ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > now)
        {
            return GoalExecutionResult.WaitingForCooldown("Waiting for survey cooldown.", ship.CooldownExpiresAt);
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return GoalExecutionResult.Blocked("Unexpected ship state during survey execution.");
        }

        SurveyActionResult result;
        try
        {
            result = await port.SurveyAsync(ship.Symbol, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The survey plan gives the ship a target again on the next tick; a failure that repeats
            // shows as RepeatingError.
            logger.LogWarning(
                ex,
                "SurveyWaypointGoalExecutor: survey failed for ship {ShipSymbol} at {WaypointSymbol}.",
                ship.Symbol,
                surveyGoal.TargetWaypointSymbol);
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            return GoalExecutionResult.Blocked($"Survey failed: {ex.Message}");
        }

        await ships.UpdateCooldownAsync(ship.Symbol, result.CooldownExpiresAt ?? now.AddSeconds(result.CooldownSeconds), ct);
        await surveyKeeper.TakenAsync(ship.Symbol, surveyGoal.TargetDepositSymbol, result.Surveys, ct);
        await goals.ClearActiveGoalAsync(ship.Symbol, ct);

        return GoalExecutionResult.Completed(
            $"Surveyed {surveyGoal.TargetWaypointSymbol}: {result.Surveys.Count} survey(s).");
    }
}
