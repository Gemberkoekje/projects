using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="TradeBetweenMarketsGoal"/>: one trip of a trade route (PLAN.md slice 6.5).
/// The ship flies to the buy market, buys, flies to the sell market and sells, refuelling on the way
/// where a market is beyond one tank (<see cref="TradeRoutePlanner.NextStop"/>). A ship can't change
/// course in flight, so it reconsiders the trip where it lands, with the prices its arrival has just
/// refreshed (and the newest prices known for the other market):
/// <list type="bullet">
///   <item>at the buy market, before buying: when the trip is no longer lucrative, it gives it up
///   (<c>TradeDropped</c>) and the trading plan chooses again from there;</item>
///   <item>at the sell market, before selling: when selling there is no longer lucrative and another
///   market pays more after fuel, it takes the cargo there (<c>TradeRerouted</c>), once per trip.</item>
/// </list>
/// </summary>
public sealed class TradeBetweenMarketsGoalExecutor(
    IShipRepository ships,
    IShipGoalRepository goals,
    IAgentRepository agents,
    ISpaceTradersPort port,
    ITradeContextReader tradeContexts,
    IMarketRefresher marketRefresher,
    IDockSubCommand dock,
    IMessageBus bus,
    ILogger<TradeBetweenMarketsGoalExecutor> logger) : IShipGoalExecutor
{
    private const string NotLucrative = "not_lucrative";
    private const string NotPossible = "not_possible";
    private const string NotBoughtHere = "not_bought_here";

    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is TradeBetweenMarketsGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var trade = (TradeBetweenMarketsGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Trade ship is in transit.");
        }

        return trade.CargoBought
            ? await SellStepAsync(ship, trade, ct)
            : await BuyStepAsync(ship, trade, ct);
    }

    private async Task<GoalExecutionResult> BuyStepAsync(ShipModel ship, TradeBetweenMarketsGoal trade, CancellationToken ct)
    {
        if (!IsAt(ship, trade.BuyWaypointSymbol))
        {
            return await FlyTowardsAsync(ship, trade.BuyWaypointSymbol, ct);
        }

        if (ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            await dock.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing($"Docking at buy waypoint {trade.BuyWaypointSymbol}.");
        }

        var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
        if (!TradeRoutePlanner.TryEvaluate(
                context.Map,
                ship,
                trade.TradeSymbol,
                trade.BuyWaypointSymbol,
                trade.SellWaypointSymbol,
                context.CreditsForCargo,
                out var route))
        {
            return await DropAsync(ship, trade, NotPossible, ct);
        }

        if (!route.IsLucrative(context.MinProfitPerUnit))
        {
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} drops its {TradeSymbol} trip at {WaypointSymbol}: buying {Units} at {BuyPrice} and selling at {SellPrice} at {SellWaypoint} earns {ExpectedProfit} after fuel, under {MinProfitPerUnit} a unit ({Reason}).",
                JournalEvents.TradeDropped,
                ship.Symbol,
                trade.TradeSymbol,
                trade.BuyWaypointSymbol,
                route.Units,
                route.BuyPrice,
                route.SellPrice,
                trade.SellWaypointSymbol,
                route.Profit,
                context.MinProfitPerUnit,
                NotLucrative);
            return GoalExecutionResult.Completed($"The {trade.TradeSymbol} trip is no longer lucrative; dropped.");
        }

        var result = await port.BuyCargoAsync(ship.Symbol, trade.TradeSymbol, route.Units, ct);
        await ships.UpdateCargoAsync(ship.Symbol, result.Cargo, ct);
        await agents.SetCreditsAsync(bus, result.AgentCredits, ct);

        // The ledger and the credits-spent metric (B7). The port's "revenue" is the transaction's total.
        await bus.PublishAsync(new CargoPurchasedEvent(
            ship.Symbol,
            new TradeSymbol(trade.TradeSymbol),
            route.Units,
            result.Revenue,
            result.AgentCredits,
            trade.BuyWaypointSymbol));

        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} bought {Units} {TradeSymbol} at {WaypointSymbol} for {Cost} credits.",
            JournalEvents.CargoBought,
            ship.Symbol,
            route.Units,
            trade.TradeSymbol,
            trade.BuyWaypointSymbol,
            result.Revenue);

        // The purchase moved the price: the market again, while the ship is still there (D25).
        await marketRefresher.RefreshAfterTradeAsync(ship.SystemSymbol ?? string.Empty, trade.BuyWaypointSymbol, ship.Symbol, ct);

        await goals.SetActiveGoalAsync(
            ship.Symbol,
            trade with
            {
                CargoBought = true,
                Units = route.Units,
                PricePaidPerUnit = result.Revenue / route.Units,
            },
            ct);
        return GoalExecutionResult.Progressing(
            $"Bought {route.Units} {trade.TradeSymbol} at {trade.BuyWaypointSymbol}; next, selling at {trade.SellWaypointSymbol}.");
    }

    private async Task<GoalExecutionResult> SellStepAsync(ShipModel ship, TradeBetweenMarketsGoal trade, CancellationToken ct)
    {
        var units = (ship.CargoInventory ?? [])
            .Where(item => item.Symbol.Equals(trade.TradeSymbol, StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Units);
        if (units <= 0)
        {
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            return GoalExecutionResult.Completed($"No {trade.TradeSymbol} aboard; nothing left to sell.");
        }

        if (!IsAt(ship, trade.SellWaypointSymbol))
        {
            return await FlyTowardsAsync(ship, trade.SellWaypointSymbol, ct);
        }

        if (ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            await dock.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing($"Docking at sell waypoint {trade.SellWaypointSymbol}.");
        }

        var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
        var sellsHere = context.Map.TryGetGood(trade.SellWaypointSymbol, trade.TradeSymbol, out var here) && here.SellPrice > 0;
        var marginHere = here.SellPrice - trade.PricePaidPerUnit;
        var lucrativeHere = sellsHere && marginHere > 0 && marginHere >= context.MinProfitPerUnit;
        if (!lucrativeHere
            && !trade.SellWaypointChanged
            && TradeRoutePlanner.TryFindBestSale(context.Map, ship, trade.TradeSymbol, units, out var elsewhere)
            && !elsewhere.WaypointSymbol.Equals(trade.SellWaypointSymbol, StringComparison.OrdinalIgnoreCase)
            && elsewhere.NetRevenue > (sellsHere ? (long)here.SellPrice * units : 0))
        {
            return await SellElsewhereAsync(context.Map, ship, trade, elsewhere, sellsHere ? here.SellPrice : 0, ct);
        }

        if (!sellsHere)
        {
            return await DropAsync(ship, trade, NotBoughtHere, ct);
        }

        // One sale may not exceed the market's trade volume, so a larger load goes in several.
        var batchSize = here.TradeVolume > 0 ? here.TradeVolume : units;
        for (var left = units; left > 0;)
        {
            var batch = Math.Min(left, batchSize);
            var result = await port.SellCargoAsync(ship.Symbol, trade.TradeSymbol, batch, ct);
            await ships.UpdateCargoAsync(ship.Symbol, result.Cargo, ct);
            await agents.SetCreditsAsync(bus, result.AgentCredits, ct);

            // The ledger and the credits-earned metric (B7).
            await bus.PublishAsync(new ShipCargoSoldEvent(
                ship.Symbol,
                new TradeSymbol(trade.TradeSymbol),
                batch,
                result.Revenue,
                result.AgentCredits));

            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} sold {Units} {TradeSymbol} at {WaypointSymbol} for {Revenue} credits.",
                JournalEvents.CargoSold,
                ship.Symbol,
                batch,
                trade.TradeSymbol,
                trade.SellWaypointSymbol,
                result.Revenue);
            left -= batch;
        }

        // The sale moved the price: the market again, while the ship is still there (D25).
        await marketRefresher.RefreshAfterTradeAsync(ship.SystemSymbol ?? string.Empty, trade.SellWaypointSymbol, ship.Symbol, ct);

        await goals.ClearActiveGoalAsync(ship.Symbol, ct);
        return GoalExecutionResult.Completed(
            $"Sold {units} {trade.TradeSymbol} at {trade.SellWaypointSymbol}; the trip is done.");
    }

    private async Task<GoalExecutionResult> SellElsewhereAsync(
        TradeMarketMap map,
        ShipModel ship,
        TradeBetweenMarketsGoal trade,
        TradeSale elsewhere,
        long priceHere,
        CancellationToken ct)
    {
        await goals.SetActiveGoalAsync(
            ship.Symbol,
            trade with { SellWaypointSymbol = elsewhere.WaypointSymbol, SellWaypointChanged = true },
            ct);

        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} takes its {TradeSymbol} from {WaypointSymbol} ({SellPrice} each) to {SellWaypoint} ({NewSellPrice} each, {FuelCost} for fuel) ({Reason}).",
            JournalEvents.TradeRerouted,
            ship.Symbol,
            trade.TradeSymbol,
            trade.SellWaypointSymbol,
            priceHere,
            elsewhere.WaypointSymbol,
            elsewhere.SellPrice,
            elsewhere.FuelCost,
            NotLucrative);

        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, TradeRoutePlanner.NextStop(map, ship, elsewhere.WaypointSymbol)), ct);
        return GoalExecutionResult.WaitingForArrival(
            $"Selling at {elsewhere.WaypointSymbol} instead of {trade.SellWaypointSymbol}.");
    }

    /// <summary>
    /// Flies towards a market: straight there when one tank will do, otherwise to the first market on
    /// the way where it can refuel. Each arrival refreshes that market's prices and steps the goal again.
    /// </summary>
    private async Task<GoalExecutionResult> FlyTowardsAsync(ShipModel ship, string destination, CancellationToken ct)
    {
        var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
        var stop = TradeRoutePlanner.NextStop(context.Map, ship, destination);
        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, stop), ct);
        return GoalExecutionResult.WaitingForArrival(
            stop.Equals(destination, StringComparison.OrdinalIgnoreCase)
                ? $"Navigating to {destination}."
                : $"Navigating to {destination}, refuelling at {stop} on the way.");
    }

    /// <summary>Gives the trip up: the trading plan chooses again, and sells any cargo where it fetches most.</summary>
    private async Task<GoalExecutionResult> DropAsync(ShipModel ship, TradeBetweenMarketsGoal trade, string reason, CancellationToken ct)
    {
        await goals.ClearActiveGoalAsync(ship.Symbol, ct);
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} drops its {TradeSymbol} trip at {WaypointSymbol}: it can't be carried on to {SellWaypoint} ({Reason}).",
            JournalEvents.TradeDropped,
            ship.Symbol,
            trade.TradeSymbol,
            ship.WaypointSymbol,
            trade.SellWaypointSymbol,
            reason);
        return GoalExecutionResult.Completed($"The {trade.TradeSymbol} trip can't be carried on ({reason}); dropped.");
    }

    private static bool IsAt(ShipModel ship, string waypointSymbol)
        => string.Equals(ship.WaypointSymbol, waypointSymbol, StringComparison.OrdinalIgnoreCase);
}
