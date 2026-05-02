using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals;

/// <summary>
/// Phase 10: orchestrates goal-driven ship automation.
/// Loads the ship's active goal, builds <see cref="ShipGoalContext"/>, selects the matching
/// <see cref="IShipGoalExecutor"/>, and handles the <see cref="GoalExecutionResult"/> lifecycle.
/// Phase 12a: planner fallback removed; ships without an active goal are treated as idle.
/// </summary>
public sealed class ShipGoalExecutorService(
    IEnumerable<IShipGoalExecutor> executors,
    IShipGoalRepository goals,
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    ISurveyRepository surveys,
    INavigationPlanningService navigationPlanning,
    IConstructionRepository constructions,
    IWaypointRepository waypoints,
    IMarketRepository markets,
    IContractRepository contracts,
    ISettingsRepository settings,
    IAssignmentResolver assignmentResolver,
    IMessageBus bus,
    ILogger<ShipGoalExecutorService> logger) : IShipGoalExecutorService
{
    /// <summary>Fallback retry delay when a cooldown expiry time is unavailable.</summary>
    private const int CooldownRetrySeconds = 70;

    public async Task<GoalExecutionResult?> ExecuteAsync(string shipSymbol, CancellationToken ct)
    {
        var ship = await ships.FindAsync(shipSymbol, ct);
        if (ship is null)
        {
            logger.LogWarning("ShipGoalExecutorService: ship {Ship} not found.", shipSymbol);
            return null;
        }

        var activeGoal = await goals.GetActiveGoalAsync(shipSymbol, ct);

        // No active goal: ship is idle, nothing to do.
        if (activeGoal is null)
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

        var ctx = await BuildContextAsync(ship, activeGoal, ct);
        var result = await executor.ExecuteStepAsync(ship, activeGoal, ctx, ct);

        logger.LogInformation(
            "ShipGoalExecutorService: ship={Ship} goal={Kind} outcome={Outcome} reason={Reason}",
            shipSymbol,
            activeGoal.Kind,
            result.Outcome,
            result.Reason);

        await HandleResultAsync(ship, activeGoal, result, ct);
        return result;
    }

    private async Task HandleResultAsync(
        ShipModel ship,
        ShipGoal goal,
        GoalExecutionResult result,
        CancellationToken ct)
    {
        var now = TimeProvider.System.GetUtcNow();

        switch (result.Outcome)
        {
            case GoalExecutionOutcome.Completed:
                await bus.PublishAsync(new GoalCompletedEvent(
                    ship.Symbol, goal.GoalId, goal.Kind, Guid.NewGuid(), Guid.Empty, now));
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
                await bus.InvokeAsync(new AssignShipCommand(
                    ship.Symbol,
                    "Idle",
                    SystemSymbol: ship.SystemSymbol ?? string.Empty,
                    WaypointSymbol: ship.WaypointSymbol ?? string.Empty), ct);
                break;

            case GoalExecutionOutcome.Blocked:
                await bus.PublishAsync(new GoalBlockedEvent(
                    ship.Symbol, goal.GoalId, goal.Kind, result.Reason, Guid.NewGuid(), Guid.Empty, now));
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
                await bus.InvokeAsync(new AssignShipCommand(
                    ship.Symbol,
                    "Idle",
                    SystemSymbol: ship.SystemSymbol ?? string.Empty,
                    WaypointSymbol: ship.WaypointSymbol ?? string.Empty), ct);
                break;

            case GoalExecutionOutcome.WaitingForCooldown when result.CooldownExpiresAt.HasValue:
                var cooldownTick = new ShipAutomationTickEvent(
                    ship.Symbol,
                    "CooldownExpired",
                    result.CooldownExpiresAt.Value,
                    Guid.NewGuid(),
                    Guid.Empty);
                await bus.ScheduleAsync(cooldownTick, result.CooldownExpiresAt.Value);
                break;

            case GoalExecutionOutcome.WaitingForCooldown:
                // Cooldown without known expiry: schedule a short retry tick.
                var retryAt = now.AddSeconds(CooldownRetrySeconds);
                var retryTick = new ShipAutomationTickEvent(
                    ship.Symbol, "CooldownRetry", retryAt, Guid.NewGuid(), Guid.Empty);
                await bus.ScheduleAsync(retryTick, retryAt);
                break;

            case GoalExecutionOutcome.Progressing:
                // Publish immediate follow-up tick so the next step runs.
                // Command handlers for navigate/dock/orbit also publish ticks; duplicate ticks are
                // handled idempotently by the executor on the next invocation.
                await bus.PublishAsync(new ShipAutomationTickEvent(
                    ship.Symbol, "GoalStep", now, Guid.NewGuid(), Guid.Empty));
                break;

            case GoalExecutionOutcome.WaitingForArrival:
                // ShipInTransitEventHandler schedules the next tick at arrival time.
                break;
        }
    }

    private async Task<ShipGoalContext> BuildContextAsync(
        ShipModel ship,
        ShipGoal goal,
        CancellationToken ct)
    {
        var activeSurveys = ship.HasSurveyEquipment && !string.IsNullOrWhiteSpace(ship.WaypointSymbol)
            ? (await surveys.GetActiveByWaypointAsync(ship.WaypointSymbol, ct)).Count
            : 0;

        var navigationTarget = ResolveNavigationTarget(ship, goal);
        var recommendedFlightMode = string.Empty;
        if (!string.IsNullOrWhiteSpace(navigationTarget) && ship.FuelCapacity > 0)
        {
            var plan = await navigationPlanning.BuildPlanAsync(ship, navigationTarget, ct);
            if (plan is not null && !string.IsNullOrWhiteSpace(plan.RecommendedFlightMode))
            {
                recommendedFlightMode = plan.RecommendedFlightMode;
            }
        }

        var fuelMarket = string.Empty;
        var currentSellsFuel = false;
        if (!string.IsNullOrWhiteSpace(ship.SystemSymbol))
        {
            (fuelMarket, currentSellsFuel) = await ResolveFuelContextAsync(ship, ct);
        }

        var constructionComplete = false;
        if (goal is SupplyConstructionGoal supplyGoal && !string.IsNullOrWhiteSpace(supplyGoal.ConstructionSiteWaypointSymbol))
        {
            var site = await constructions.FindAsync(supplyGoal.ConstructionSiteWaypointSymbol, ct);
            constructionComplete = site is not null && site.IsComplete;
        }

        IReadOnlyList<ContractDto> activeContracts = [];
        MarketSnapshot? currentMarketSnapshot = null;
        var miningReserveHydrocarbonUnits = 0;
        var miningMinimumSellPrice = 0;
        var miningJettisonLowValueWhenFull = false;

        if (ship.LocalStatus == Domain.Enums.ShipLocalStatus.Docked)
        {
            activeContracts = await contracts.GetActiveAsync(ct);

            if (!string.IsNullOrWhiteSpace(ship.WaypointSymbol))
            {
                currentMarketSnapshot = await markets.FindSnapshotByWaypointAsync(ship.WaypointSymbol, ct);
            }

            if (goal is MineResourceGoal or SiphonResourceGoal)
            {
                miningReserveHydrocarbonUnits = await settings.GetAsync<int>("Mining.ReserveHydrocarbonUnits", ct);
                miningMinimumSellPrice = await settings.GetAsync<int>("Mining.MinimumSellPriceToKeepCargo", ct);
                miningJettisonLowValueWhenFull = await settings.GetAsync<bool>("Mining.JettisonLowValueWhenFull", ct);
            }
        }

        string? nearestSellMarket = null;
        if (goal is MineResourceGoal mineGoal)
        {
            nearestSellMarket = await assignmentResolver.FindNearestSellMarketAsync(ship, mineGoal.TradeSymbol, ct);
        }
        else if (goal is SiphonResourceGoal siphonGoal)
        {
            nearestSellMarket = await assignmentResolver.FindNearestSellMarketAsync(ship, siphonGoal.TradeSymbol, ct);
        }

        return new ShipGoalContext
        {
            ActiveSurveyCount = activeSurveys,
            RecommendedFlightMode = recommendedFlightMode,
            FuelMarketWaypoint = fuelMarket,
            CurrentWaypointSellsFuel = currentSellsFuel,
            ConstructionComplete = constructionComplete,
            ActiveContracts = activeContracts,
            CurrentMarketSnapshot = currentMarketSnapshot,
            MiningReserveHydrocarbonUnits = miningReserveHydrocarbonUnits,
            MiningMinimumSellPrice = miningMinimumSellPrice,
            MiningJettisonLowValueWhenFull = miningJettisonLowValueWhenFull,
            NearestSellMarket = nearestSellMarket,
        };
    }

    private static string ResolveNavigationTarget(ShipModel ship, ShipGoal goal)
    {
        return goal switch
        {
            MineResourceGoal mineGoal => ship.CargoCurrent > 0
                ? string.Empty  // sell market, determined later
                : mineGoal.SourceWaypointSymbol,
            SiphonResourceGoal siphonGoal => ship.CargoCurrent > 0
                ? string.Empty
                : siphonGoal.SourceWaypointSymbol,
            MoveToWaypointGoal moveGoal => moveGoal.TargetWaypointSymbol,
            SellCargoGoal sellGoal => sellGoal.DestinationWaypointSymbol,
            DeliverCargoGoal deliverGoal => deliverGoal.DeliveryWaypointSymbol,
            SupplyConstructionGoal supplyGoal => supplyGoal.ConstructionSiteWaypointSymbol,
            ScoutWaypointGoal scoutGoal => scoutGoal.TargetWaypointSymbol,
            PatrolMarketGoal patrolGoal => patrolGoal.TargetWaypointSymbol,
            _ => string.Empty,
        };
    }

    private async Task<(string FuelMarket, bool CurrentSellsFuel)> ResolveFuelContextAsync(
        ShipModel ship,
        CancellationToken ct)
    {
        var systemWaypoints = await waypoints.GetBySystemAsync(ship.SystemSymbol!, ct);
        if (systemWaypoints.Count == 0)
        {
            return (string.Empty, false);
        }

        var marketWaypoints = systemWaypoints
            .Where(w => w.HasMarket)
            .Select(w => w.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (marketWaypoints.Count == 0)
        {
            return (string.Empty, false);
        }

        var snapshots = await markets.GetAllSnapshotsAsync(ct);
        var fuelSellingMarkets = snapshots
            .Where(s => s.SystemSymbol.Equals(ship.SystemSymbol, StringComparison.OrdinalIgnoreCase))
            .Where(s => marketWaypoints.Contains(s.WaypointSymbol))
            .Where(s => s.TradeGoods.Any(g => g.Symbol.Equals("FUEL", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var fuelMarket = fuelSellingMarkets.FirstOrDefault()?.WaypointSymbol ?? string.Empty;
        var currentSellsFuel = !string.IsNullOrWhiteSpace(ship.WaypointSymbol)
            && fuelSellingMarkets.Any(s => s.WaypointSymbol.Equals(ship.WaypointSymbol, StringComparison.OrdinalIgnoreCase));

        return (fuelMarket, currentSellsFuel);
    }
}
