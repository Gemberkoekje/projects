using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces;
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
/// where a market is beyond one tank (<see cref="TradeRoutePlanner.NextStop"/>). A market trades at most its trade volume at
/// once, so the ship buys and sells in batches, each at the price quoted then, and each moves the next quote (D79). A ship
/// can't change course in flight, so it reconsiders the trip where it lands, with the prices its arrival has just refreshed
/// (and the newest prices known for the other market):
/// <list type="bullet">
///   <item>at the buy market, before buying: when the trip is no longer lucrative, or the credits no other trip holds back
///   (D57, <see cref="TripReservations"/>) don't pay for a unit, it gives it up (<c>TradeDropped</c>) and the trading plan
///   chooses again from there. Otherwise it buys a batch at a time, while the next units still earn <c>Trade.MinProfitPerUnit</c>
///   against what their sale is expected to fetch (<see cref="TradeRoutePlanner.UnitsWorthBuying"/>);</item>
///   <item>at the sell market, before selling: when selling there is no longer lucrative and another
///   market pays more after fuel, it takes the cargo there (<c>TradeRerouted</c>), once per trip. Otherwise it sells a batch at
///   a time while each still earns the minimum over what the cargo cost; when one wouldn't, the rest goes where it fetches
///   more, on the same once-per-trip terms, or is sold there all the same.</item>
/// </list>
/// The goal keeps what the cargo cost and fetched, stored after each batch, and however the trip ends, sold or dropped, it is
/// booked with what its sales brought in (<see cref="ITripBook"/>, D46). A purchase on the route the credits were saved up for
/// ends that saving (<see cref="FullHoldSavings"/>, D56). Its flights are in CRUISE, which the arithmetic assumes: a ship
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

        var system = ship.SystemSymbol ?? string.Empty;
        var context = await tradeContexts.ReadAsync(system, ct);

        // D57: what the other trips hold back on their way to buy is theirs, construction trips' too (D64); what this one holds
        // back is its own to spend.
        var heldByOthers = TripReservations.HeldBack(await goals.GetActiveTradeGoalsAsync(ct), ship.Symbol)
            + TripReservations.HeldBack(await goals.GetActiveConstructionGoalsAsync(ct));
        var credits = Math.Max(0, context.CreditsForCargo - heldByOthers);

        // What the earlier batches bought is aboard, after a restart too; only a trip that has bought nothing yet is weighed
        // again as a whole, with the prices the arrival fetched and only the fuel still ahead.
        var bought = Aboard(ship, trade.TradeSymbol);
        if (bought == 0)
        {
            if (!TradeRoutePlanner.TryEvaluate(
                    context.Map,
                    ship,
                    trade.TradeSymbol,
                    trade.BuyWaypointSymbol,
                    trade.SellWaypointSymbol,
                    credits,
                    context.MinProfitPerUnit,
                    out var route,
                    out var check))
            {
                return check == TradeRouteCheck.NotLucrative
                    ? await DropNotLucrativeAsync(ship, trade, route, context.MinProfitPerUnit, ct)
                    : await DropAsync(ship, trade, NotPossible, ct);
            }

            if (!route.IsLucrative(context.MinProfitPerUnit))
            {
                return await DropNotLucrativeAsync(ship, trade, route, context.MinProfitPerUnit, ct);
            }
        }

        // D79: a batch at a time, each at the price quoted then, with the fuel still ahead kept back, while the next units
        // still earn the minimum against what their sale is expected to fetch at the sell market, as last seen.
        var fuelAhead = TradeRoutePlanner.TryPlanFlight(context.Map, ship, trade.SellWaypointSymbol, out var haul) ? haul.FuelCost : 0;
        var spendable = Math.Max(0, credits - fuelAhead);
        var free = ship.CargoCapacity - ship.CargoCurrent;
        var map = context.Map;
        var current = trade;
        var stop = NotPossible;
        while (free > 0
            && map.TryGetGood(trade.BuyWaypointSymbol, trade.TradeSymbol, out var atBuy) && atBuy.PurchasePrice > 0
            && map.TryGetGood(trade.SellWaypointSymbol, trade.TradeSymbol, out var atSell) && atSell.SellPrice > 0)
        {
            var affordable = (int)Math.Min(int.MaxValue, spendable / atBuy.PurchasePrice);
            var most = Math.Min(Math.Min(free, atBuy.TradeVolume > 0 ? atBuy.TradeVolume : free), affordable);
            var units = TradeRoutePlanner.UnitsWorthBuying(atBuy.PurchasePrice, atSell, bought, most, context.MinProfitPerUnit);
            if (units == 0)
            {
                stop = affordable == 0 ? NotPossible : NotLucrative;
                break;
            }

            var cost = await BuyBatchAsync(ship.Symbol, system, trade, units, ct);
            bought += units;
            free -= units;
            spendable -= cost;
            current = current with
            {
                Spent = current.Spent + cost,
                ReservedCredits = Math.Max(0, current.ReservedCredits - cost),
            };
            await goals.SetActiveGoalAsync(ship.Symbol, current, ct);
            map = (await tradeContexts.ReadAsync(system, ct)).Map;
        }

        if (bought == 0)
        {
            return await DropAsync(ship, trade, stop, ct);
        }

        // D56: the trip the credits were saved up for has bought; ships may be bought again.
        if (savings.TryGet(ship.Symbol, out var saving)
            && saving.RouteKey.Equals(TradeRoutePlanner.RouteKey(trade.TradeSymbol, trade.BuyWaypointSymbol, trade.SellWaypointSymbol), StringComparison.OrdinalIgnoreCase))
        {
            savings.Clear(ship.Symbol);
        }

        await goals.SetActiveGoalAsync(
            ship.Symbol,
            current with
            {
                CargoBought = true,
                Units = bought,
                PricePaidPerUnit = current.Spent / bought,
            },
            ct);
        return GoalExecutionResult.Progressing(
            $"Bought {bought} {trade.TradeSymbol} at {trade.BuyWaypointSymbol}; next, selling at {trade.SellWaypointSymbol}.");
    }

    /// <summary>One purchase of a batch, published for the ledger, and the market fetched again: the purchase moved its price (D25).</summary>
    /// <returns>What the batch cost.</returns>
    private async Task<long> BuyBatchAsync(string shipSymbol, string system, TradeBetweenMarketsGoal trade, int units, CancellationToken ct)
    {
        var result = await port.BuyCargoAsync(shipSymbol, trade.TradeSymbol, units, ct);
        await ships.UpdateCargoAsync(shipSymbol, result.Cargo, ct);
        await agents.SetCreditsAsync(bus, result.AgentCredits, ct);

        // The ledger and the credits-spent metric (B7), and the units bought from this market. The port's "revenue" is
        // the transaction's total.
        await bus.PublishAsync(new CargoPurchasedEvent(
            shipSymbol,
            new TradeSymbol(trade.TradeSymbol),
            units,
            result.Revenue,
            result.AgentCredits,
            trade.BuyWaypointSymbol));

        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} bought {Units} {TradeSymbol} at {WaypointSymbol} for {Cost} credits.",
            JournalEvents.CargoBought,
            shipSymbol,
            units,
            trade.TradeSymbol,
            trade.BuyWaypointSymbol,
            result.Revenue);

        await marketRefresher.RefreshAfterTradeAsync(system, trade.BuyWaypointSymbol, shipSymbol, ct);
        return result.Revenue;
    }

    private async Task<GoalExecutionResult> SellStepAsync(ShipModel ship, TradeBetweenMarketsGoal trade, CancellationToken ct)
    {
        var units = Aboard(ship, trade.TradeSymbol);
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

        var system = ship.SystemSymbol ?? string.Empty;
        var context = await tradeContexts.ReadAsync(system, ct);
        var sellsHere = context.Map.TryGetGood(trade.SellWaypointSymbol, trade.TradeSymbol, out var here) && here.SellPrice > 0;
        if (!Pays(here, trade, context.MinProfitPerUnit)
            && TryFindBetterSale(context.Map, ship, trade, units, sellsHere ? here.SellPrice : 0, out var elsewhere))
        {
            return await SellElsewhereAsync(context.Map, ship, trade, elsewhere, sellsHere ? here.SellPrice : 0, ct);
        }

        if (!sellsHere)
        {
            return await DropAsync(ship, trade, NotBoughtHere, ct);
        }

        // D79: a batch of the market's trade volume at a time, each at the price quoted then, while it still earns the minimum
        // over what the cargo cost. Each sale lowers the next quote: once one wouldn't pay, the rest goes where it fetches more
        // after fuel, once per trip, or with nowhere better is sold here all the same. A sale that never paid sells anyway.
        var sellAnyway = !Pays(here, trade, context.MinProfitPerUnit);
        var current = trade;
        var map = context.Map;
        var left = units;
        while (left > 0 && map.TryGetGood(trade.SellWaypointSymbol, trade.TradeSymbol, out var now) && now.SellPrice > 0)
        {
            if (!sellAnyway && !Pays(now, trade, context.MinProfitPerUnit))
            {
                if (TryFindBetterSale(map, ship, current, left, now.SellPrice, out var better))
                {
                    return await SellElsewhereAsync(map, ship, current, better, now.SellPrice, ct);
                }

                sellAnyway = true;
            }

            var batch = Math.Min(left, now.TradeVolume > 0 ? now.TradeVolume : left);
            var revenue = await SellBatchAsync(ship.Symbol, system, trade, batch, ct);
            left -= batch;
            current = current with { Earned = current.Earned + revenue };
            await goals.SetActiveGoalAsync(ship.Symbol, current, ct);
            map = (await tradeContexts.ReadAsync(system, ct)).Map;
        }

        if (left > 0)
        {
            // The market stopped buying the good part way: the trading plan sells the rest where it fetches most.
            return await DropAsync(ship, current, NotBoughtHere, ct);
        }

        await EndTripAsync(ship.Symbol, current, TripBook.Sold, ct);
        return GoalExecutionResult.Completed(
            $"Sold {units} {trade.TradeSymbol} at {trade.SellWaypointSymbol}; the trip is done.");
    }

    /// <summary>One sale of a batch, published for the ledger, and the market fetched again: the sale moved its price (D25).</summary>
    /// <returns>What the batch fetched.</returns>
    private async Task<long> SellBatchAsync(string shipSymbol, string system, TradeBetweenMarketsGoal trade, int units, CancellationToken ct)
    {
        var result = await port.SellCargoAsync(shipSymbol, trade.TradeSymbol, units, ct);
        await ships.UpdateCargoAsync(shipSymbol, result.Cargo, ct);
        await agents.SetCreditsAsync(bus, result.AgentCredits, ct);

        // The ledger and the credits-earned metric (B7), and the units sold to this market.
        await bus.PublishAsync(new ShipCargoSoldEvent(
            shipSymbol,
            new TradeSymbol(trade.TradeSymbol),
            units,
            result.Revenue,
            result.AgentCredits,
            trade.SellWaypointSymbol));

        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} sold {Units} {TradeSymbol} at {WaypointSymbol} for {Revenue} credits.",
            JournalEvents.CargoSold,
            shipSymbol,
            units,
            trade.TradeSymbol,
            trade.SellWaypointSymbol,
            result.Revenue);

        await marketRefresher.RefreshAfterTradeAsync(system, trade.SellWaypointSymbol, shipSymbol, ct);
        return result.Revenue;
    }

    /// <summary>Whether selling a unit at the market's quote earns the minimum over what it cost (D14).</summary>
    private static bool Pays(TradeGoodSnapshot good, TradeBetweenMarketsGoal trade, int minProfitPerUnit)
    {
        var margin = good.SellPrice - trade.PricePaidPerUnit;
        return good.SellPrice > 0 && margin > 0 && margin >= minProfitPerUnit;
    }

    /// <summary>
    /// Another market that pays more for what is aboard than this one, after the fuel to get there, while the sale hasn't moved
    /// yet: it moves once per trip, so it never flies in circles.
    /// </summary>
    private static bool TryFindBetterSale(TradeMarketMap map, ShipModel ship, TradeBetweenMarketsGoal trade, int units, long priceHere, out TradeSale elsewhere)
    {
        var found = TradeRoutePlanner.TryFindBestSale(map, ship, trade.TradeSymbol, units, out elsewhere);
        return found
            && !trade.SellWaypointChanged
            && !elsewhere.WaypointSymbol.Equals(trade.SellWaypointSymbol, StringComparison.OrdinalIgnoreCase)
            && elsewhere.NetRevenue > priceHere * units;
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

    /// <summary>
    /// Gives up a trip no longer worth it at the buy market (D14, D79): its units no longer earn the minimum a unit after fuel,
    /// or not even the first unit does.
    /// </summary>
    private async Task<GoalExecutionResult> DropNotLucrativeAsync(
        ShipModel ship,
        TradeBetweenMarketsGoal trade,
        TradeRoute route,
        int minProfitPerUnit,
        CancellationToken ct)
    {
        if (route.Units == 0)
        {
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} drops its {TradeSymbol} trip at {WaypointSymbol}: a unit bought at {BuyPrice} and sold at {SellPrice} at {SellWaypoint} earns {Margin}, under {MinProfitPerUnit} ({Reason}).",
                JournalEvents.TradeDropped,
                ship.Symbol,
                trade.TradeSymbol,
                trade.BuyWaypointSymbol,
                route.BuyPrice,
                route.SellPrice,
                trade.SellWaypointSymbol,
                route.SellPrice - route.BuyPrice,
                minProfitPerUnit,
                NotLucrative);
        }
        else
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
                minProfitPerUnit,
                NotLucrative);
        }

        await EndTripAsync(ship.Symbol, trade, NotLucrative, ct);
        return GoalExecutionResult.Completed($"The {trade.TradeSymbol} trip is no longer lucrative; dropped.");
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

    private static int Aboard(ShipModel ship, string tradeSymbol)
        => (ship.CargoInventory ?? [])
            .Where(item => item.Symbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Units);

    private static bool IsAt(ShipModel ship, string waypointSymbol)
        => string.Equals(ship.WaypointSymbol, waypointSymbol, StringComparison.OrdinalIgnoreCase);
}
