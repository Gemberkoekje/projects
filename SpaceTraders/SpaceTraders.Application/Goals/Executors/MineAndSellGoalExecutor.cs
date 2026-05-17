using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="MineAndSellGoal"/>.
/// Mines the target resource at the source waypoint and sells collected units at the configured market.
/// Prefers using cached surveys for increased extraction yield when available.
/// </summary>
public sealed class MineAndSellGoalExecutor(
    IShipRepository ships,
    IShipGoalRepository goals,
    IAgentRepository agents,
    ISurveyRepository surveys,
    ISpaceTradersPort port,
    IMessageBus bus,
    ILogger<MineAndSellGoalExecutor> logger) : IShipGoalExecutor
{
    public bool CanExecute(ShipGoal goal) => goal is MineAndSellGoal;

    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var miningGoal = (MineAndSellGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Mining ship is in transit.");
        }

        var cargoInventory = ship.CargoInventory ?? [];
        var targetUnits = cargoInventory
            .FirstOrDefault(i => i.Symbol.Equals(miningGoal.TradeSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        if (targetUnits <= 0)
        {
            // Try to find a survey for the target mineral at the source waypoint.
            var survey = await surveys.GetBestActiveSurveyAsync(
                miningGoal.SourceWaypointSymbol,
                miningGoal.TradeSymbol,
                ct);

            if (!IsUsableSurvey(survey))
            {
                await TryAssignSurveyGoalAsync(ship, miningGoal, ct);
                return GoalExecutionResult.WaitingForArrival(
                    $"Waiting for active survey for {miningGoal.TradeSymbol} at {miningGoal.SourceWaypointSymbol} before mining.");
            }

            var mineResult = await bus.InvokeAsync<ShipCommandResult>(
                new MineResourceVolumeCommand(
                    ship.Symbol,
                    miningGoal.TradeSymbol,
                    miningGoal.SourceWaypointSymbol,
                    Math.Max(1, ship.CargoCapacity),
                    survey),
                ct);

            if (mineResult is null)
            {
                logger.LogWarning(
                    "MineAndSellGoalExecutor: mining command returned null result for ship {ShipSymbol}.",
                    ship.Symbol);
                return GoalExecutionResult.Blocked("Mining command returned no result.");
            }

            if (!mineResult.Accepted)
            {
                return GoalExecutionResult.Blocked(
                    $"Mining command rejected at {mineResult.WaypointSymbol} with state {mineResult.Status}.");
            }

            if (mineResult.Status == ShipLocalStatus.InTransit)
            {
                return GoalExecutionResult.WaitingForArrival(
                    $"Navigating to mining source {miningGoal.SourceWaypointSymbol}.");
            }

            if (ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > TimeProvider.System.GetUtcNow())
            {
                return GoalExecutionResult.WaitingForCooldown(
                    "Waiting for extraction cooldown.",
                    ship.CooldownExpiresAt);
            }

            return GoalExecutionResult.Progressing($"Mining {miningGoal.TradeSymbol} in progress.");
        }

        var atSellWaypoint = string.Equals(
            ship.WaypointSymbol,
            miningGoal.SellWaypointSymbol,
            StringComparison.OrdinalIgnoreCase);

        if (!atSellWaypoint || ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, miningGoal.SellWaypointSymbol), ct);
            return GoalExecutionResult.WaitingForArrival(
                $"Navigating to sell waypoint {miningGoal.SellWaypointSymbol}.");
        }

        var sellResult = await port.SellCargoAsync(ship.Symbol, miningGoal.TradeSymbol, targetUnits, ct);
        await ships.UpdateCargoAsync(ship.Symbol, sellResult.Cargo, ct);

        var agent = await agents.GetAsync(ct);
        if (agent is not null)
        {
            await agents.UpsertAsync(agent with { Credits = sellResult.AgentCredits }, ct);
        }

        logger.LogInformation(
            "MineAndSellGoalExecutor: ship {ShipSymbol} sold {Units} {TradeSymbol} at {Waypoint} for {Revenue} credits.",
            ship.Symbol,
            targetUnits,
            miningGoal.TradeSymbol,
            miningGoal.SellWaypointSymbol,
            sellResult.Revenue);

        // Keep goal active; the orchestrator decides when the market opportunity ends.
        await goals.SetActiveGoalAsync(ship.Symbol, miningGoal, ct);

        return GoalExecutionResult.Progressing(
            $"Sold {targetUnits} {miningGoal.TradeSymbol} at {miningGoal.SellWaypointSymbol}; continuing mining loop.");
    }

    private async Task TryAssignSurveyGoalAsync(ShipModel minerShip, MineAndSellGoal miningGoal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(miningGoal.SourceWaypointSymbol)
            || string.IsNullOrWhiteSpace(miningGoal.TradeSymbol))
        {
            return;
        }

        var activeSurveyTargets = await goals.GetActiveSurveyTargetsAsync(ct);
        var target = (
            miningGoal.SourceWaypointSymbol.ToUpperInvariant(),
            miningGoal.TradeSymbol.ToUpperInvariant());

        if (activeSurveyTargets is not null && activeSurveyTargets.Contains(target))
        {
            return;
        }

        var surveyGoal = new SurveyWaypointGoal
        {
            TargetWaypointSymbol = miningGoal.SourceWaypointSymbol,
            TargetDepositSymbol = miningGoal.TradeSymbol,
        };

        var allShips = await ships.GetAllAsync(ct);
        var surveyShips = allShips
            .Where(candidate =>
                candidate.HasSurveyEquipment
                && candidate.LocalStatus != ShipLocalStatus.InTransit)
            .OrderByDescending(candidate =>
                candidate.Symbol.Equals(minerShip.Symbol, StringComparison.OrdinalIgnoreCase))
            .ThenBy(candidate => candidate.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (surveyShips.Count == 0)
        {
            return;
        }

        var surveyShipSymbol = string.Empty;
        var fallbackSurveyShipSymbol = string.Empty;

        foreach (var candidate in surveyShips)
        {
            var currentGoal = await goals.GetActiveGoalAsync(candidate.Symbol, ct);
            if (currentGoal is SurveyWaypointGoal existingSurveyGoal
                && existingSurveyGoal.TargetWaypointSymbol.Equals(miningGoal.SourceWaypointSymbol, StringComparison.OrdinalIgnoreCase)
                && existingSurveyGoal.TargetDepositSymbol.Equals(miningGoal.TradeSymbol, StringComparison.OrdinalIgnoreCase)
                && existingSurveyGoal.Status is not GoalStatus.Completed
                && existingSurveyGoal.Status is not GoalStatus.Blocked)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(fallbackSurveyShipSymbol))
            {
                fallbackSurveyShipSymbol = candidate.Symbol;
            }

            if (currentGoal is null
                || currentGoal.Status is GoalStatus.Completed or GoalStatus.Blocked
                || candidate.Symbol.Equals(minerShip.Symbol, StringComparison.OrdinalIgnoreCase))
            {
                surveyShipSymbol = candidate.Symbol;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(surveyShipSymbol))
        {
            surveyShipSymbol = fallbackSurveyShipSymbol;
        }

        if (string.IsNullOrWhiteSpace(surveyShipSymbol))
        {
            return;
        }

        await goals.SetActiveGoalAsync(surveyShipSymbol, surveyGoal, ct);
        logger.LogInformation(
            "MineAndSellGoalExecutor: assigned high-priority SurveyWaypointGoal on ship {SurveyShip} for {TradeSymbol} at {Waypoint}.",
            surveyShipSymbol,
            miningGoal.TradeSymbol,
            miningGoal.SourceWaypointSymbol);
    }

    private static bool IsUsableSurvey(SurveyModel survey) =>
        survey is not null
        && !string.IsNullOrWhiteSpace(survey.Signature)
        && !string.IsNullOrWhiteSpace(survey.WaypointSymbol)
        && !string.IsNullOrWhiteSpace(survey.Size)
        && survey.Expiration > TimeProvider.System.GetUtcNow();
}
