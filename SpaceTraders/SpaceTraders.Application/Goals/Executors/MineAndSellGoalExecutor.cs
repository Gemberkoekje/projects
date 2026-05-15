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
/// </summary>
public sealed class MineAndSellGoalExecutor(
    IShipRepository ships,
    IShipGoalRepository goals,
    IAgentRepository agents,
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
            var mineResult = await bus.InvokeAsync<ShipCommandResult>(
                new MineResourceVolumeCommand(
                    ship.Symbol,
                    miningGoal.TradeSymbol,
                    miningGoal.SourceWaypointSymbol,
                    Math.Max(1, ship.CargoCapacity)),
                ct);

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
}
