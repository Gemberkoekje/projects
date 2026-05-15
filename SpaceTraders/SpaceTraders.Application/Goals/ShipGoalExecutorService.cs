using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals;

public sealed class ShipGoalExecutorService(
    IEnumerable<IShipGoalExecutor> executors,
    IShipGoalRepository goals,
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    ISurveyRepository surveys,
    INavigationPlanningService navigationPlanning,
    IWaypointRepository waypoints,
    IMarketRepository markets,
    IShipCapabilityRegistry capabilityRegistry,
    IShipEventScheduler scheduler,
    IShipGoalHistoryRepository goalHistory,
    IScoutAllMarketplacesPlanService scoutPlanService,
    IMessageBus bus,
    ILogger<ShipGoalExecutorService> logger) : IShipGoalExecutorService
{
    public async Task<GoalExecutionResult?> ExecuteAsync(string shipSymbol, CancellationToken ct)
    {
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
            and not TradeBetweenMarketsGoal)
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
}
