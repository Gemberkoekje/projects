using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="SurveyWaypointGoal"/>.
/// Navigates to the target waypoint, ensures proper orbit state, performs a survey,
/// and stores results in the survey repository for later use by mining operations.
/// </summary>
public sealed class SurveyWaypointGoalExecutor(
    ISpaceTradersPort port,
    ISurveyRepository surveys,
    IMessageBus bus,
    ILogger<SurveyWaypointGoalExecutor> logger) : IShipGoalExecutor
{
    public bool CanExecute(ShipGoal goal) => goal is SurveyWaypointGoal;

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

        var atTarget = string.Equals(
            ship.WaypointSymbol,
            surveyGoal.TargetWaypointSymbol,
            StringComparison.OrdinalIgnoreCase);

        if (!atTarget)
        {
            await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, surveyGoal.TargetWaypointSymbol), ct);
            return GoalExecutionResult.WaitingForArrival(
                $"Navigating to survey target {surveyGoal.TargetWaypointSymbol}.");
        }

        // At target; ensure we're in orbit
        if (atTarget && ship.LocalStatus == ShipLocalStatus.Docked)
        {
            await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, surveyGoal.TargetWaypointSymbol), ct);
            return GoalExecutionResult.WaitingForArrival(
                $"Entering orbit at survey waypoint {surveyGoal.TargetWaypointSymbol}.");
        }

        // Check if cooldown is active
        if (atTarget && ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > TimeProvider.System.GetUtcNow())
        {
            return GoalExecutionResult.WaitingForCooldown(
                "Waiting for survey cooldown.",
                ship.CooldownExpiresAt);
        }

        // At target, in orbit, no cooldown; perform survey
        if (atTarget && ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            try
            {
                var surveyResult = await port.SurveyAsync(ship.Symbol, ct);

                if (surveyResult?.Surveys != null && surveyResult.Surveys.Count > 0)
                {
                    await surveys.UpsertAsync(ship.Symbol, surveyResult.Surveys, ct);

                    logger.LogInformation(
                        "SurveyWaypointGoalExecutor: ship {ShipSymbol} surveyed {WaypointSymbol} and found {SurveyCount} survey(s).",
                        ship.Symbol,
                        surveyGoal.TargetWaypointSymbol,
                        surveyResult.Surveys.Count);
                }

                // Update cooldown if present
                if (surveyResult?.CooldownExpiresAt.HasValue == true)
                {
                    return GoalExecutionResult.WaitingForCooldown(
                        $"Survey completed at {surveyGoal.TargetWaypointSymbol}; cooldown active.",
                        surveyResult.CooldownExpiresAt);
                }

                return GoalExecutionResult.Completed(
                    $"Survey of {surveyGoal.TargetWaypointSymbol} completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "SurveyWaypointGoalExecutor: survey failed for ship {ShipSymbol} at {WaypointSymbol}.",
                    ship.Symbol,
                    surveyGoal.TargetWaypointSymbol);

                return GoalExecutionResult.Blocked($"Survey failed: {ex.Message}");
            }
        }

        return GoalExecutionResult.Blocked("Unexpected ship state during survey execution.");
    }
}
