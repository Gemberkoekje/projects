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
///   <item>at the buy market, before buying: when the trip is no longer lucrative, or the markets no longer trade its
///   full hold in one go (D56) and the seller's supply is no longer ABUNDANT (D74), or the credits no other trip holds back
///   (D57, <see cref="TripReservations"/>) don't pay for it, it gives it up (<c>TradeDropped</c>) and the trading plan
///   chooses again from there. What it buys is what the markets trade at once now (<see cref="TradeRoutePlanner.UnitsAtOnce"/>);</item>
///   <item>at the sell market, before selling: when selling there is no longer lucrative and another
///   market pays more after fuel, it takes the cargo there (<c>TradeRerouted</c>), once per trip.</item>
/// </list>
/// The goal keeps what the cargo cost, and however the trip ends, sold or dropped, it is booked with what its
/// sales brought in (<see cref="ITripBook"/>, D46). A purchase of the full hold the credits were saved up for ends that
/// saving (<see cref="FullHoldSavings"/>, D56). Its flights are in CRUISE, which the arithmetic assumes: a ship
/// left in DRIFT is switched back before it flies (slice 6.10c).
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
    ITripBook trips,
    FullHoldSavings savings,
    ILogger<TradeBetweenMarketsGoalExecutor> logger) : IShipGoalExecutor
{
    private const string NotLucrative = "not_lucrative";
    private const string NotPossible = "not_possible";
    private const string NotFullHold = "not_full_hold";
    private const string NotBoughtHere = "not_bought_here";
    private const string CruiseMode = "CRUISE";

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

        // D57: what the other trips hold back on their way to buy is theirs, construction trips' too (D64); what this one holds
        // back is its own to spend.
        var heldByOthers = TripReservations.HeldBack(await goals.GetActiveTradeGoalsAsync(ct), ship.Symbol)
            + TripReservations.HeldBack(await goals.GetActiveConstructionGoalsAsync(ct));
        if (!TradeRoutePlanner.TryEvaluate(
                context.Map,
                ship,
                trade.TradeSymbol,
                trade.BuyWaypointSymbol,
                trade.SellWaypointSymbol,
                Math.Max(0, context.CreditsForCargo - heldByOthers),
                out var route))
        {
            // D56: a full hold in one purchase and one sale, as the markets trade now, or at an ABUNDANT seller what both
            // markets trade at once (D74), or no trip.
            var reason = context.Map.TryGetGood(trade.BuyWaypointSymbol, trade.TradeSymbol, out _)
                && context.Map.TryGetGood(trade.SellWaypointSymbol, trade.TradeSymbol, out _)
                && TradeRoutePlanner.UnitsAtOnce(context.Map, ship, trade.TradeSymbol, trade.BuyWaypointSymbol, trade.SellWaypointSymbol) == 0
                    ? NotFullHold
                    : NotPossible;
            return await DropAsync(ship, trade, reason, ct);
        }

        if (!route.IsLucrative(context.MinProfitPerUnit))
        {
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
            await EndTripAsync(ship.Symbol, trade, NotLucrative, ct);
            return GoalExecutionResult.Completed($"The {trade.TradeSymbol} trip is no longer lucrative; dropped.");
        }

        var result = await port.BuyCargoAsync(ship.Symbol, trade.TradeSymbol, route.Units, ct);
        await ships.UpdateCargoAsync(ship.Symbol, result.Cargo, ct);
        await agents.SetCreditsAsync(bus, result.AgentCredits, ct);

        // The ledger and the credits-spent metric (B7), and the units bought from this market. The port's "revenue" is
        // the transaction's total.
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

        // D56: the full hold the credits were saved up for is bought; ships may be bought again.
        if (savings.TryGet(ship.Symbol, out var saving)
            && saving.RouteKey.Equals(TradeRoutePlanner.RouteKey(trade.TradeSymbol, trade.BuyWaypointSymbol, trade.SellWaypointSymbol), StringComparison.OrdinalIgnoreCase))
        {
            savings.Clear(ship.Symbol);
        }

        // The purchase moved the price: the market again, while the ship is still there (D25).
        await marketRefresher.RefreshAfterTradeAsync(ship.SystemSymbol ?? string.Empty, trade.BuyWaypointSymbol, ship.Symbol, ct);

        await goals.SetActiveGoalAsync(
            ship.Symbol,
            trade with
            {
                CargoBought = true,
                Units = route.Units,
                PricePaidPerUnit = result.Revenue / route.Units,
                Spent = trade.Spent + result.Revenue,
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
            await EndTripAsync(ship.Symbol, trade, TripBook.NothingAboard, ct);
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
        var earned = 0L;
        for (var left = units; left > 0;)
        {
            var batch = Math.Min(left, batchSize);
            var result = await port.SellCargoAsync(ship.Symbol, trade.TradeSymbol, batch, ct);
            await ships.UpdateCargoAsync(ship.Symbol, result.Cargo, ct);
            await agents.SetCreditsAsync(bus, result.AgentCredits, ct);

            // The ledger and the credits-earned metric (B7), and the units sold to this market.
            await bus.PublishAsync(new ShipCargoSoldEvent(
                ship.Symbol,
                new TradeSymbol(trade.TradeSymbol),
                batch,
                result.Revenue,
                result.AgentCredits,
                trade.SellWaypointSymbol));

            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} sold {Units} {TradeSymbol} at {WaypointSymbol} for {Revenue} credits.",
                JournalEvents.CargoSold,
                ship.Symbol,
                batch,
                trade.TradeSymbol,
                trade.SellWaypointSymbol,
                result.Revenue);
            earned += result.Revenue;
            left -= batch;
        }

        // The sale moved the price: the market again, while the ship is still there (D25).
        await marketRefresher.RefreshAfterTradeAsync(ship.SystemSymbol ?? string.Empty, trade.SellWaypointSymbol, ship.Symbol, ct);

        await EndTripAsync(ship.Symbol, trade with { Earned = trade.Earned + earned }, TripBook.Sold, ct);
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

        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, TradeRoutePlanner.NextStop(map, ship, elsewhere.WaypointSymbol)) { FlightMode = CruiseMode }, ct);
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
        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, stop) { FlightMode = CruiseMode }, ct);
        return GoalExecutionResult.WaitingForArrival(
            stop.Equals(destination, StringComparison.OrdinalIgnoreCase)
                ? $"Navigating to {destination}."
                : $"Navigating to {destination}, refuelling at {stop} on the way.");
    }

    /// <summary>Gives the trip up: the trading plan chooses again, and sells any cargo where it fetches most.</summary>
    private async Task<GoalExecutionResult> DropAsync(ShipModel ship, TradeBetweenMarketsGoal trade, string reason, CancellationToken ct)
    {
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} drops its {TradeSymbol} trip at {WaypointSymbol}: it can't be carried on to {SellWaypoint} ({Reason}).",
            JournalEvents.TradeDropped,
            ship.Symbol,
            trade.TradeSymbol,
            ship.WaypointSymbol,
            trade.SellWaypointSymbol,
            reason);
        await EndTripAsync(ship.Symbol, trade, reason, ct);
        return GoalExecutionResult.Completed($"The {trade.TradeSymbol} trip can't be carried on ({reason}); dropped.");
    }

    /// <summary>
    /// Ends the trip: clears its goal, so the trading plan chooses the next, and books what it made (D46). A trip dropped
    /// with its cargo aboard books what the cargo cost; the trip that sells it books what it fetches.
    /// </summary>
    private async Task EndTripAsync(string shipSymbol, TradeBetweenMarketsGoal trade, string reason, CancellationToken ct)
    {
        await goals.ClearActiveGoalAsync(shipSymbol, ct);
        await trips.BookAsync(shipSymbol, trade, reason, ct);
    }

    private static bool IsAt(ShipModel ship, string waypointSymbol)
        => string.Equals(ship.WaypointSymbol, waypointSymbol, StringComparison.OrdinalIgnoreCase);
}
