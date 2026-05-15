using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="TradeBetweenMarketsGoal"/>.
/// Buys the target good at the configured source market and sells it at the destination market.
/// </summary>
public sealed class TradeBetweenMarketsGoalExecutor(
    IShipRepository ships,
    IShipGoalRepository goals,
    IAgentRepository agents,
    ISpaceTradersPort port,
    IDockSubCommand dock,
    IMessageBus bus,
    ILogger<TradeBetweenMarketsGoalExecutor> logger) : IShipGoalExecutor
{
    public bool CanExecute(ShipGoal goal) => goal is TradeBetweenMarketsGoal;

    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var tradeGoal = (TradeBetweenMarketsGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Trade ship is in transit.");
        }

        var cargoInventory = ship.CargoInventory ?? [];
        var targetUnits = cargoInventory
            .FirstOrDefault(i => i.Symbol.Equals(tradeGoal.TradeSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        if (targetUnits <= 0)
        {
            var atBuyWaypoint = string.Equals(
                ship.WaypointSymbol,
                tradeGoal.BuyWaypointSymbol,
                StringComparison.OrdinalIgnoreCase);

            if (!atBuyWaypoint)
            {
                await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, tradeGoal.BuyWaypointSymbol), ct);
                return GoalExecutionResult.WaitingForArrival(
                    $"Navigating to buy waypoint {tradeGoal.BuyWaypointSymbol}.");
            }

            if (ship.LocalStatus == ShipLocalStatus.InOrbit)
            {
                await dock.ExecuteAsync(ship.Symbol, ct);
                return GoalExecutionResult.Progressing(
                    $"Docking at buy waypoint {tradeGoal.BuyWaypointSymbol}.");
            }

            var unitsToBuy = Math.Max(1, ship.CargoCapacity - ship.CargoCurrent);
            if (unitsToBuy <= 0)
            {
                return GoalExecutionResult.Blocked(
                    $"Ship {ship.Symbol} has no cargo capacity available to buy {tradeGoal.TradeSymbol}.");
            }

            var buyResult = await port.BuyCargoAsync(ship.Symbol, tradeGoal.TradeSymbol, unitsToBuy, ct);
            await ships.UpdateCargoAsync(ship.Symbol, buyResult.Cargo, ct);
            await UpdateAgentCreditsAsync(buyResult.AgentCredits, ct);

            logger.LogInformation(
                "TradeBetweenMarketsGoalExecutor: ship {ShipSymbol} bought {Units} {TradeSymbol} at {Waypoint} for {Cost} credits.",
                ship.Symbol,
                unitsToBuy,
                tradeGoal.TradeSymbol,
                tradeGoal.BuyWaypointSymbol,
                buyResult.Revenue);

            await goals.SetActiveGoalAsync(ship.Symbol, tradeGoal, ct);
            return GoalExecutionResult.Progressing(
                $"Bought {unitsToBuy} {tradeGoal.TradeSymbol} at {tradeGoal.BuyWaypointSymbol}; preparing to sell.");
        }

        var atSellWaypoint = string.Equals(
            ship.WaypointSymbol,
            tradeGoal.SellWaypointSymbol,
            StringComparison.OrdinalIgnoreCase);

        if (!atSellWaypoint)
        {
            await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, tradeGoal.SellWaypointSymbol), ct);
            return GoalExecutionResult.WaitingForArrival(
                $"Navigating to sell waypoint {tradeGoal.SellWaypointSymbol}.");
        }

        if (ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            await dock.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing(
                $"Docking at sell waypoint {tradeGoal.SellWaypointSymbol}.");
        }

        var sellResult = await port.SellCargoAsync(ship.Symbol, tradeGoal.TradeSymbol, targetUnits, ct);
        await ships.UpdateCargoAsync(ship.Symbol, sellResult.Cargo, ct);
        await UpdateAgentCreditsAsync(sellResult.AgentCredits, ct);

        logger.LogInformation(
            "TradeBetweenMarketsGoalExecutor: ship {ShipSymbol} sold {Units} {TradeSymbol} at {Waypoint} for {Revenue} credits.",
            ship.Symbol,
            targetUnits,
            tradeGoal.TradeSymbol,
            tradeGoal.SellWaypointSymbol,
            sellResult.Revenue);

        await goals.SetActiveGoalAsync(ship.Symbol, tradeGoal, ct);
        return GoalExecutionResult.Progressing(
            $"Sold {targetUnits} {tradeGoal.TradeSymbol} at {tradeGoal.SellWaypointSymbol}; continuing trade loop.");
    }

    private async Task UpdateAgentCreditsAsync(long credits, CancellationToken ct)
    {
        var agent = await agents.GetAsync(ct);
        if (agent is not null)
        {
            await agents.UpsertAsync(agent with { Credits = credits }, ct);
        }
    }
}
