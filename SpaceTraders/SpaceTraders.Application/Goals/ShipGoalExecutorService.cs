using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Goals;

public sealed class ShipGoalExecutorService(
    IEnumerable<IShipGoalExecutor> executors,
    IShipGoalRepository goals,
    IShipRepository ships,
    IScoutAllMarketplacesPlanService scoutPlanService,
    ISettingsRepository settings,
    IGoalStepCircuitBreaker circuitBreaker,
    IAutomationMetrics metrics,
    ILogger<ShipGoalExecutorService> logger) : IShipGoalExecutorService
{
    private const string MaxGoalStepsPerMinuteSetting = "Automation.CircuitBreaker.MaxGoalStepsPerMinute";
    private const int DefaultMaxGoalStepsPerMinute = 60;
    private const string RunawayReason = "runaway";

    public async Task<GoalExecutionResult?> ExecuteAsync(string shipSymbol, CancellationToken ct)
    {
        // Every goal step comes through here, whatever triggered it (the tick, an arrival, the
        // probe handler, startup recovery), so this is where the kill switch stops them all.
        if (!await settings.IsAutomationEnabledAsync(ct))
        {
            return null;
        }

        var ship = await ships.FindAsync(shipSymbol, ct);
        if (ship is null)
        {
            logger.LogWarning("ShipGoalExecutorService: ship {Ship} not found.", shipSymbol);
            return null;
        }

        var activeGoal = await goals.GetActiveGoalAsync(shipSymbol, ct);
        if (activeGoal is not ScoutWaypointGoal
            and not DeployProbeGoal
            and not MineAndSellGoal
            and not TradeBetweenMarketsGoal
            and not SurveyWaypointGoal)
        {
            return null;
        }

        // A blocked goal stays blocked until its plan replaces it.
        if (activeGoal.Status == GoalStatus.Blocked)
        {
            return null;
        }

        // A plan that is switched off doesn't move its ships. Their goals wait until it is back on.
        var plan = AutomationSwitches.PlanFor(activeGoal);
        if (plan is not null && !await settings.IsPlanEnabledAsync(plan.Value, ct))
        {
            return null;
        }

        var executor = executors.FirstOrDefault(e => e.CanExecute(activeGoal));
        if (executor is null)
        {
            logger.LogWarning(
                "ShipGoalExecutorService: no executor registered for goal kind {Kind} on ship {Ship}.",
                activeGoal.Kind,
                shipSymbol);
            return null;
        }

        var maxStepsPerMinute = await GetMaxGoalStepsPerMinuteAsync(ct);
        if (circuitBreaker.RecordStep(shipSymbol, maxStepsPerMinute, TimeProvider.System.GetUtcNow()))
        {
            await goals.BlockGoalAsync(shipSymbol, activeGoal.GoalId, RunawayReason, ct);
            metrics.GoalBreakerTripped(shipSymbol);
            logger.LogWarning(
                "ShipGoalExecutorService: ship {ShipSymbol} took more than {MaxSteps} goal steps in a minute; goal {GoalKind} blocked ({Reason}).",
                shipSymbol,
                maxStepsPerMinute,
                activeGoal.Kind,
                RunawayReason);
            return GoalExecutionResult.Blocked($"{RunawayReason}: more than {maxStepsPerMinute} goal steps in a minute.");
        }

        var result = await executor.ExecuteStepAsync(ship, activeGoal, new ShipGoalContext(), ct);

        if (result.Outcome == GoalExecutionOutcome.Completed && activeGoal is ScoutWaypointGoal)
        {
            logger.LogInformation(
                "ShipGoalExecutorService: ship {Ship} completed goal {Kind}; advancing scout plan.",
                shipSymbol,
                activeGoal.Kind);
            await scoutPlanService.AdvanceAsync(shipSymbol, ct);
        }

        return result;
    }

    private async Task<int> GetMaxGoalStepsPerMinuteAsync(CancellationToken ct)
    {
        var configured = await settings.GetAsync<int>(MaxGoalStepsPerMinuteSetting, ct);
        return configured > 0 ? configured : DefaultMaxGoalStepsPerMinute;
    }
}
