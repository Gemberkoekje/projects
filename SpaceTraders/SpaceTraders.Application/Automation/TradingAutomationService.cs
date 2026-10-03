using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.SpareTime;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>The trading plan (PLAN.md slice 6.5).</summary>
public interface ITradingAutomationService
{
    /// <summary>
    /// One pass of the plan: gives every free trader a trip, and records the routes that are held and
    /// the lucrative ones that aren't.
    /// </summary>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The trading plan (PLAN.md slice 6.5). Every tick it gives each free trader a trip:
/// <list type="bullet">
///   <item>a trader is any ship with a cargo hold and a fuel tank that has no goal (or a blocked one),
///   no open assignment, and isn't in transit, and that the survey, mining and siphon plans, which go first,
///   left free: with the survey plan on, a ship that can survey trades only in its spare time, while the
///   spare-time plan is on (D34, slice 6.8), and otherwise never (D20);</item>
///   <item>a trader that holds cargo first sells it where it fetches the most after fuel, when that
///   earns anything;</item>
///   <item>otherwise it gets its best lucrative route (<see cref="TradeRoutePlanner.Rank"/>) that no
///   other trader holds: two traders never share a route. When several traders are free, the best
///   route goes first, to the trader it is best for. The credits are those no trip on its way to buy holds back, and a
///   trip holds back what its cargo costs from the moment it starts until it buys (D57, <see cref="TripReservations"/>);</item>
///   <item>with the spare-time plan on, a ship that gathers in its spare time (the command ship, when the survey
///   plan has nothing for it) trades only for a route that waits for it once its hold is sold, after the other
///   traders (D34): then it sells its hold first, and a spare-time trip that fills its hold is interrupted for it.
///   Otherwise the spare-time plan keeps it (D37).</item>
/// </list>
/// </summary>
/// <remarks>
/// The trip itself, with its check against the newest prices at each market, is the
/// <c>TradeBetweenMarketsGoalExecutor</c>'s. The plan buys its own cargo ships (D21, which replaced D16):
/// when no trader is left without a trip and a new ship would have a lucrative route from the shipyard, it
/// buys the next type in <c>Trade.ShipPurchases</c>, and once the list is bought one more of its last type, one at a
/// time and within the credit reserve, when the order ships are bought in lets it (D43, <see cref="IPurchaseOrder"/>).
/// </remarks>
public sealed class TradingAutomationService(
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipAssignmentRepository assignments,
    ITradeContextReader tradeContexts,
    IPlanRepository plans,
    ISettingsRepository settings,
    IShipyardRepository shipyards,
    IShipPurchaseService shipPurchases,
    SpareTimeInterruption interruption,
    IContractMineralPlanRepository contractPlans,
    ICargoJettison cargoJettison,
    IPurchaseOrder purchaseOrder,
    FullHoldSavings savings,
    ILogger<TradingAutomationService> logger) : ITradingAutomationService
{
    /// <summary>The most pending routes the plan's state keeps, best first.</summary>
    internal const int MaxPendingRoutes = 20;

    /// <summary>
    /// The setting that lists the cargo ships the plan buys, in order (D21): the Nth is bought while the
    /// fleet has fewer than N cargo ships; empty buys none.
    /// </summary>
    public const string ShipPurchasesSetting = "Trade.ShipPurchases";

    /// <summary>Credits that buy a full hold of anything: whether a new cargo ship would have work, whatever the credits now.</summary>
    private const long AnyCredits = long.MaxValue / 4;

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var board = await FleetRoleBoard.ReadAsync(settings, plans, cancellationToken);
        var earmarked = await ContractOreAsync(cancellationToken);
        var fleet = await ships.GetAllAsync(cancellationToken);
        var withAssignment = (await assignments.GetAllActiveAsync(cancellationToken))
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var held = new List<HeldRoute>();
        var free = new List<ShipModel>();

        // Ships that gather in their spare time (slice 6.8), free or on a spare-time trip that fills its hold, while
        // the spare-time plan is on: the survey plan, which goes first, had nothing for them, and they trade only for
        // a route that waits for them (D34). Any other surveyor surveys, and only that (D20), but for selling or
        // jettisoning a hold nothing else will (D42).
        var gatherers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var surveyorsWithCargo = new List<ShipModel>();
        foreach (var ship in fleet)
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            var hasAssignment = withAssignment.Contains(ship.Symbol);
            var gathers = board.SpareTimeOn && board.GathersInSpareTime(ship);
            if (goal is TradeBetweenMarketsGoal trade && trade.Status != GoalStatus.Blocked)
            {
                held.Add(new HeldRoute(ship.Symbol, trade));
            }
            else if ((gathers ? ship.IsTradingCapable : board.IsTrader(ship))
                && FleetRoles.IsFree(ship, goal, hasAssignment))
            {
                free.Add(ship);
                if (gathers)
                {
                    gatherers.Add(ship.Symbol);
                }
            }
            else if (gathers && SpareTimeInterruption.IsInterruptible(ship, goal, hasAssignment))
            {
                free.Add(ship);
                gatherers.Add(ship.Symbol);
                onTrip.Add(ship.Symbol);
            }
            else if (board.IsSurveyor(ship)
                && ship.CargoCurrent > 0
                && FleetRoles.IsFree(ship, goal, hasAssignment))
            {
                surveyorsWithCargo.Add(ship);
            }
        }

        // D56: a saving is kept only for a ship that trades, on a trip or not.
        savings.KeepOnly(fleet
            .Where(ship => board.IsTrader(ship)
                || (board.SpareTimeOn && board.GathersInSpareTime(ship) && ship.IsTradingCapable)
                || held.Any(route => route.ShipSymbol.Equals(ship.Symbol, StringComparison.OrdinalIgnoreCase)))
            .Select(ship => ship.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

        var heldKeys = held.Select(route => route.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new Dictionary<string, PendingRoute>(StringComparer.OrdinalIgnoreCase);
        var idle = 0;
        foreach (var system in free.GroupBy(ship => ship.SystemSymbol ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var context = await tradeContexts.ReadAsync(system.Key, cancellationToken);
            var traders = new List<ShipModel>();
            foreach (var ship in system.Where(ship => !gatherers.Contains(ship.Symbol)))
            {
                if (TradeRoutePlanner.TryFindBestCargoSale(context.Map, ship, mustSell: false, out var cargo, out var sale))
                {
                    var goal = await SellHeldCargoAsync(context.Map, ship, cargo, sale, cancellationToken);
                    held.Add(new HeldRoute(ship.Symbol, goal));
                    heldKeys.Add(held[^1].Key);
                }
                else
                {
                    // Cargo that doesn't pay for its sale would only take room from the route (D42).
                    traders.Add(await JettisonDeadCargoAsync(context.Map, ship, board, earmarked, cancellationToken));
                }
            }

            var credits = await AssignRoutesAsync(context, traders, held, heldKeys, pending, cancellationToken);
            idle += traders.Count;

            // After the other traders: a ship that gathers in its spare time takes a route that is left for it.
            foreach (var ship in system.Where(ship => gatherers.Contains(ship.Symbol)))
            {
                credits = await TradeInsteadOfGatheringAsync(context, credits, ship, onTrip.Contains(ship.Symbol), held, heldKeys, cancellationToken);
            }
        }

        // A surveyor with nothing to survey and no spare-time trip to fill its hold on sells it where that pays, and
        // jettisons the rest (D42): it would otherwise carry it for good.
        foreach (var system in surveyorsWithCargo.GroupBy(ship => ship.SystemSymbol ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var map = (await tradeContexts.ReadAsync(system.Key, cancellationToken)).Map;
            foreach (var ship in system)
            {
                if (TradeRoutePlanner.TryFindBestCargoSale(map, ship, mustSell: false, out var cargo, out var sale))
                {
                    held.Add(new HeldRoute(ship.Symbol, await SellHeldCargoAsync(map, ship, cargo, sale, cancellationToken)));
                    heldKeys.Add(held[^1].Key);
                }
                else
                {
                    await JettisonDeadCargoAsync(map, ship, board, earmarked, cancellationToken);
                }
            }
        }

        await BuyCargoShipAsync(fleet, heldKeys, idle, cancellationToken);

        await SaveStateAsync(held, pending, cancellationToken);
    }

    /// <summary>
    /// The contract's ore while the contract wants it (D23, D40): a ship that will deliver it keeps it aboard. Empty
    /// otherwise.
    /// </summary>
    private async Task<string> ContractOreAsync(CancellationToken cancellationToken)
        => await settings.IsPlanEnabledAsync(AutomationPlan.Contract, cancellationToken)
            && await contractPlans.GetAsync(cancellationToken) is { Status: ContractMineralPlanStatus.Active } contract
            && contract.UnitsFulfilled < contract.UnitsRequired
                ? contract.TradeSymbol
                : string.Empty;

    /// <summary>
    /// Jettisons what a free ship holds that nothing will sell or use (D42), when no good aboard pays for its sale:
    /// everything but the contract's ore on a ship that mines for the contract.
    /// </summary>
    /// <returns>The ship with the hold it has left.</returns>
    private async Task<ShipModel> JettisonDeadCargoAsync(
        TradeMarketMap map,
        ShipModel ship,
        FleetRoleBoard board,
        string contractOre,
        CancellationToken cancellationToken)
    {
        var current = ship;
        foreach (var (cargo, reason) in HeldCargo.ToJettison(
            map,
            ship,
            good => contractOre.Length > 0 && good.Equals(contractOre, StringComparison.OrdinalIgnoreCase) && board.MinesForContract(ship)))
        {
            if (await cargoJettison.JettisonAsync(current, cargo, reason, cancellationToken) is { } left)
            {
                current = current with { CargoInventory = left.Inventory, CargoCurrent = left.Units };
            }
        }

        return current;
    }

    /// <summary>
    /// Buys cargo ships (D21): the next in <c>Trade.ShipPurchases</c>, and once the list is bought, one more of its last type
    /// (D43), at the shipyard that sells it for the least, when every trader has a trip and the new ship would have a
    /// lucrative route from there that no trader holds, judged with the credits left after it. The purchase keeps the credit
    /// reserve (<c>FleetExpansion.MinCreditReserve</c>, <see cref="IShipPurchaseService"/>). The order ships are bought in
    /// decides when (<see cref="IPurchaseOrder"/>): a ship of the list is saved up for, whatever the routes, after the
    /// contract's drone, a surveyor and a drone for each scarce mineral; a ship beyond the list takes turns with the drones,
    /// and needs nothing while a trader has no trip or no new ship would have a route.
    /// </summary>
    private async Task BuyCargoShipAsync(IReadOnlyList<ShipModel> fleet, IReadOnlySet<string> heldKeys, int idle, CancellationToken cancellationToken)
    {
        var purchases = (await settings.GetAsync<string>(ShipPurchasesSetting, cancellationToken) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (purchases.Length == 0)
        {
            await purchaseOrder.ReportAsync(AutomationPlan.Trading, PurchaseNeed.None, cancellationToken);
            return;
        }

        var cargoShips = fleet.Count(FleetRoles.IsCargoShip);
        var inList = cargoShips < purchases.Length;
        var shipType = inList ? purchases[cargoShips] : purchases[^1];
        var systems = fleet
            .Select(ship => ship.SystemSymbol)
            .OfType<string>()
            .Where(system => system.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var offer = (await shipyards.GetAllAsync(cancellationToken))
            .Where(shipyard => systems.Contains(shipyard.SystemSymbol))
            .SelectMany(shipyard => shipyard.Ships
                .Where(ship => ship.Type.Equals(shipType, StringComparison.OrdinalIgnoreCase)
                    && ship.PurchasePrice > 0
                    && ship.CargoCapacity > 0
                    && ship.FuelCapacity > 0)
                .Select(ship => (Shipyard: shipyard, Ship: ship)))
            .OrderBy(candidate => candidate.Ship.PurchasePrice)
            .ThenBy(candidate => candidate.Shipyard.WaypointSymbol, StringComparer.Ordinal)
            .ToList();
        if (offer.Count == 0)
        {
            logger.LogDebug("Trading plan: no shipyard with a known price and hold for {ShipType}.", shipType);
            await purchaseOrder.ReportAsync(AutomationPlan.Trading, PurchaseNeed.None, cancellationToken);
            return;
        }

        var (shipyard, forSale) = offer[0];
        var context = await tradeContexts.ReadAsync(shipyard.SystemSymbol, cancellationToken);
        var newShip = new ShipModel(
            "NEW-" + shipType,
            shipyard.SystemSymbol,
            shipyard.WaypointSymbol,
            "DOCKED",
            "CRUISE",
            forSale.FuelCapacity,
            forSale.FuelCapacity,
            CargoCapacity: forSale.CargoCapacity);

        // Beyond the list a ship has nothing to buy while no new one would have work, so the drones' turn comes (D43).
        var hasWork = idle == 0 && TradeRoutePlanner.Rank(context.Map, newShip, AnyCredits, context.MinProfitPerUnit, heldKeys).Count > 0;
        var need = inList || hasWork
            ? new PurchaseNeed(inList ? PurchaseTier.CargoShips : PurchaseTier.Alternating, shipType, shipyard.WaypointSymbol, forSale.PurchasePrice)
            : PurchaseNeed.None;
        if (!await purchaseOrder.ReportAsync(AutomationPlan.Trading, need, cancellationToken) || idle > 0)
        {
            return;
        }

        var creditsForCargo = Math.Max(0, context.Credits - forSale.PurchasePrice - context.FuelReserveCredits);
        var routes = TradeRoutePlanner.Rank(context.Map, newShip, creditsForCargo, context.MinProfitPerUnit, heldKeys);
        if (routes.Count == 0)
        {
            logger.LogDebug(
                "Trading plan: a new {ShipType} from {Shipyard} would have no lucrative route; no purchase.",
                shipType,
                shipyard.WaypointSymbol);
            return;
        }

        var purchased = await shipPurchases.TryPurchaseAsync(shipType, shipyard.WaypointSymbol, cancellationToken);
        if (!purchased.IsSuccess)
        {
            logger.LogDebug(
                "Trading plan: {ShipType} purchase denied at {Shipyard} — {Reason}.",
                shipType,
                shipyard.WaypointSymbol,
                purchased.FailureReason ?? "Purchase failed.");
        }
    }

    /// <summary>
    /// A ship that gathers in its spare time (slice 6.8) trades only when a route waits for it once its hold is sold
    /// (D34): then it sells its hold first, one good a trip, by the rule every trader sells held cargo by
    /// (<see cref="TradeRoutePlanner.TryFindBestCargoSale"/>: a good no reachable market buys, or one that doesn't pay
    /// for the fuel to sell it, stays aboard), and takes its best route after. A spare-time trip that fills its hold is
    /// interrupted for that (<see cref="SpareTimeInterruption"/>). The route is judged from where selling the hold leaves
    /// the ship (<see cref="GatherPlanner.AfterSellingHold"/>), so it is still lucrative once the hold is sold and the
    /// ship doesn't turn back to gathering on the way. Without one, the spare-time plan keeps the ship, and it sells its
    /// hold once full (D37).
    /// </summary>
    /// <returns>The credits left for cargo.</returns>
    private async Task<long> TradeInsteadOfGatheringAsync(
        TradeContext context,
        long credits,
        ShipModel ship,
        bool onTrip,
        List<HeldRoute> held,
        HashSet<string> heldKeys,
        CancellationToken cancellationToken)
    {
        var routes = TradeRoutePlanner.Rank(context.Map, GatherPlanner.AfterSellingHold(context.Map, ship), credits, context.MinProfitPerUnit, heldKeys);
        if (routes.Count == 0)
        {
            return credits;
        }

        var sellsHold = TradeRoutePlanner.TryFindBestCargoSale(context.Map, ship, mustSell: false, out var cargo, out var sale);

        // Nothing aboard pays for its sale: the hold stays as it is, and the routes were judged from here.
        var goal = sellsHold ? HeldCargoGoal(context.Map, ship, cargo, sale) : RouteGoal(routes[0]);
        if (onTrip)
        {
            if (!await interruption.TryReplaceAsync(ship.Symbol, goal, "trade", cancellationToken))
            {
                return credits;
            }
        }
        else
        {
            await goals.SetActiveGoalAsync(ship.Symbol, goal, cancellationToken);
        }

        held.Add(new HeldRoute(ship.Symbol, goal));
        heldKeys.Add(held[^1].Key);
        if (sellsHold)
        {
            LogHeldCargoSale(ship, cargo, sale, goal);
            return credits;
        }

        LogRoute(ship, routes[0]);
        EndSavingFor(ship.Symbol, routes[0].Key);
        return credits - goal.ReservedCredits;
    }

    private async Task<TradeBetweenMarketsGoal> SellHeldCargoAsync(
        TradeMarketMap map,
        ShipModel ship,
        CargoItemModel cargo,
        TradeSale sale,
        CancellationToken cancellationToken)
    {
        var goal = HeldCargoGoal(map, ship, cargo, sale);
        await goals.SetActiveGoalAsync(ship.Symbol, goal, cancellationToken);
        LogHeldCargoSale(ship, cargo, sale, goal);
        return goal;
    }

    /// <summary>A trip that sells cargo the ship holds, from where it is, where it fetches the most after fuel.</summary>
    private static TradeBetweenMarketsGoal HeldCargoGoal(TradeMarketMap map, ShipModel ship, CargoItemModel cargo, TradeSale sale)
        => new()
        {
            TradeSymbol = cargo.Symbol,
            BuyWaypointSymbol = ship.WaypointSymbol ?? string.Empty,
            SellWaypointSymbol = sale.WaypointSymbol,
            Units = cargo.Units,
            ExpectedProfit = sale.NetRevenue,
            FeedsTradeSymbol = map.PricierGoodMadeFrom(sale.WaypointSymbol, cargo.Symbol),
            CargoBought = true,
        };

    private void LogHeldCargoSale(ShipModel ship, CargoItemModel cargo, TradeSale sale, TradeBetweenMarketsGoal goal)
    {
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} sells the {Units} {TradeSymbol} it holds, from {BuyWaypoint}, at {SellWaypoint} ({SellPrice} each); about {ExpectedProfit} credits after {FuelCost} for fuel.",
            JournalEvents.TradeStarted,
            ship.Symbol,
            cargo.Units,
            cargo.Symbol,
            goal.BuyWaypointSymbol,
            sale.WaypointSymbol,
            sale.SellPrice,
            sale.NetRevenue,
            sale.FuelCost);
    }

    /// <summary>
    /// Gives the free traders of one system their routes, best route first: each round, the trader whose
    /// best route ranks highest gets it, and that route is no longer open to the others.
    /// </summary>
    /// <returns>The credits left for cargo once the routes' purchases are counted.</returns>
    private async Task<long> AssignRoutesAsync(
        TradeContext context,
        List<ShipModel> traders,
        List<HeldRoute> held,
        HashSet<string> heldKeys,
        Dictionary<string, PendingRoute> pending,
        CancellationToken cancellationToken)
    {
        // Cargo leaves Trade.FuelReserveCredits for fuel (D24), and what the trips on their way to buy hold back (D57).
        var credits = Math.Max(0, context.CreditsForCargo - held.Sum(route => TripReservations.HeldBack(route.Goal)));

        // What each trader could do before anything is handed out: the routes the ShipLeftIdle rule
        // counts as work waiting for it (D13); and the full hold it saves up for, when it does (D56).
        foreach (var trader in traders)
        {
            NoteSaving(context, trader, credits, heldKeys);
            foreach (var route in TradeRoutePlanner.Rank(context.Map, trader, credits, context.MinProfitPerUnit, heldKeys))
            {
                pending[route.Key] = pending.TryGetValue(route.Key, out var known)
                    ? known.WithCandidate(trader.Symbol, route)
                    : new PendingRoute(route, [trader.Symbol]);
            }
        }

        while (traders.Count > 0)
        {
            var found = false;
            var bestShip = traders[0];
            var bestRoute = new TradeRoute(string.Empty, string.Empty, string.Empty, 0, 0, 0, 0, 0, string.Empty);
            foreach (var trader in traders)
            {
                var routes = TradeRoutePlanner.Rank(context.Map, trader, credits, context.MinProfitPerUnit, heldKeys);
                if (routes.Count > 0 && (!found || TradeRoutePlanner.CompareBestFirst(routes[0], bestRoute) < 0))
                {
                    bestShip = trader;
                    bestRoute = routes[0];
                    found = true;
                }
            }

            if (!found)
            {
                break;
            }

            var goal = await StartRouteAsync(bestShip, bestRoute, cancellationToken);
            held.Add(new HeldRoute(bestShip.Symbol, goal));
            heldKeys.Add(bestRoute.Key);
            pending.Remove(bestRoute.Key);
            credits -= goal.ReservedCredits;
            traders.Remove(bestShip);
        }

        foreach (var trader in traders)
        {
            logger.LogDebug(
                "Trading plan: no lucrative route for ship {ShipSymbol} at {WaypointSymbol} (at least {MinProfitPerUnit} per unit after fuel).",
                trader.Symbol,
                trader.WaypointSymbol,
                context.MinProfitPerUnit);
        }

        return credits;
    }

    /// <summary>
    /// Notes what a free trader saves up for (D56): its best route, credits aside, when the credits for cargo don't pay for
    /// that full hold and the trip's fuel; ships are bought after it (<see cref="FullHoldSavings"/>). Meanwhile it takes the
    /// best full hold it can pay for, or none. A trader whose best route is one it can pay for saves up for nothing more,
    /// unless that is the route it saved up for: then the saving lasts until the hold is bought.
    /// </summary>
    private void NoteSaving(TradeContext context, ShipModel trader, long credits, IReadOnlySet<string> heldKeys)
    {
        var routes = TradeRoutePlanner.Rank(context.Map, trader, AnyCredits, context.MinProfitPerUnit, heldKeys);
        if (routes.Count == 0)
        {
            savings.Clear(trader.Symbol);
            return;
        }

        var best = routes[0];
        var cost = ((long)best.Units * best.BuyPrice) + best.FuelCost;
        if (cost > credits)
        {
            if (savings.SaveFor(trader.Symbol, best.Key, cost))
            {
                logger.LogInformation(
                    "Trading plan: ship {ShipSymbol} saves up for a full hold of {Units} {TradeSymbol} from {BuyWaypoint} to {SellWaypoint}, {Cost} credits with its fuel, against {CreditsForCargo} for cargo now; ships are bought after it (D56).",
                    trader.Symbol,
                    best.Units,
                    best.TradeSymbol,
                    best.BuyWaypointSymbol,
                    best.SellWaypointSymbol,
                    cost,
                    credits);
            }
        }
        else if (savings.TryGet(trader.Symbol, out var saving) && !saving.RouteKey.Equals(best.Key, StringComparison.OrdinalIgnoreCase))
        {
            savings.Clear(trader.Symbol);
        }
    }

    private async Task<TradeBetweenMarketsGoal> StartRouteAsync(ShipModel ship, TradeRoute route, CancellationToken cancellationToken)
    {
        var goal = RouteGoal(route);
        await goals.SetActiveGoalAsync(ship.Symbol, goal, cancellationToken);
        LogRoute(ship, route);
        EndSavingFor(ship.Symbol, route.Key);
        return goal;
    }

    /// <summary>
    /// A trader that sets off for the hold it saved up for (D56) saves up no more: what the trip holds back takes its place
    /// (D57), so ships are still bought after the hold, and the credit floor doesn't count it twice.
    /// </summary>
    private void EndSavingFor(string shipSymbol, string routeKey)
    {
        if (savings.TryGet(shipSymbol, out var saving) && saving.RouteKey.Equals(routeKey, StringComparison.OrdinalIgnoreCase))
        {
            savings.Clear(shipSymbol);
        }
    }

    /// <summary>
    /// A trip along a route: buy at its buy market, sell at its sell market. It holds back what its cargo costs at the price
    /// it was chosen with until it buys (D57).
    /// </summary>
    private static TradeBetweenMarketsGoal RouteGoal(TradeRoute route)
        => new()
        {
            TradeSymbol = route.TradeSymbol,
            BuyWaypointSymbol = route.BuyWaypointSymbol,
            SellWaypointSymbol = route.SellWaypointSymbol,
            Units = route.Units,
            ExpectedProfit = route.Profit,
            FeedsTradeSymbol = route.FeedsTradeSymbol,
            ReservedCredits = route.Units * route.BuyPrice,
        };

    private void LogRoute(ShipModel ship, TradeRoute route)
    {
        if (route.FeedsProduction)
        {
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} trades {Units} {TradeSymbol} from {BuyWaypoint} ({BuyPrice} each) to {SellWaypoint} ({SellPrice} each), which makes {FeedsTradeSymbol} from it; about {ExpectedProfit} credits after {FuelCost} for fuel, the flight to the buy market included.",
                JournalEvents.TradeStarted,
                ship.Symbol,
                route.Units,
                route.TradeSymbol,
                route.BuyWaypointSymbol,
                route.BuyPrice,
                route.SellWaypointSymbol,
                route.SellPrice,
                route.FeedsTradeSymbol,
                route.Profit,
                route.FuelCost);
        }
        else
        {
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} trades {Units} {TradeSymbol} from {BuyWaypoint} ({BuyPrice} each) to {SellWaypoint} ({SellPrice} each); about {ExpectedProfit} credits after {FuelCost} for fuel, the flight to the buy market included.",
                JournalEvents.TradeStarted,
                ship.Symbol,
                route.Units,
                route.TradeSymbol,
                route.BuyWaypointSymbol,
                route.BuyPrice,
                route.SellWaypointSymbol,
                route.SellPrice,
                route.Profit,
                route.FuelCost);
        }
    }

    /// <summary>
    /// Records the held routes and the best pending ones. Only a change is written: the state is the
    /// same pass after pass while nothing happens, and the tick runs every 5 seconds.
    /// </summary>
    private async Task SaveStateAsync(
        IReadOnlyList<HeldRoute> held,
        IReadOnlyDictionary<string, PendingRoute> pending,
        CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var existing = await plans.GetAsync<TradingAutomationPlanState>(PlanTypes.TradingAutomation, cancellationToken);
        var firstSeen = (existing?.Opportunities ?? [])
            .GroupBy(opportunity => opportunity.OpportunityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().FirstObservedAt, StringComparer.OrdinalIgnoreCase);

        List<TradingAutomationOpportunityState> opportunities =
        [
            .. held
                .OrderBy(route => route.ShipSymbol, StringComparer.Ordinal)
                .Select(route => new TradingAutomationOpportunityState
                {
                    OpportunityKey = route.Key,
                    TradeSymbol = route.Goal.TradeSymbol,
                    BuyWaypointSymbol = route.Goal.BuyWaypointSymbol,
                    SellWaypointSymbol = route.Goal.SellWaypointSymbol,
                    Status = MarketAutomationOpportunityStatus.Assigned,
                    AssignedShipSymbol = route.ShipSymbol,
                    Units = route.Goal.Units,
                    ExpectedProfit = route.Goal.ExpectedProfit,
                    FeedsTradeSymbol = route.Goal.FeedsTradeSymbol,
                    FirstObservedAt = firstSeen.GetValueOrDefault(route.Key, now),
                    LastObservedAt = now,
                }),
            .. pending.Values
                .OrderBy(route => route.Best, Comparer<TradeRoute>.Create(TradeRoutePlanner.CompareBestFirst))
                .Take(MaxPendingRoutes)
                .Select(route => new TradingAutomationOpportunityState
                {
                    OpportunityKey = route.Best.Key,
                    TradeSymbol = route.Best.TradeSymbol,
                    BuyWaypointSymbol = route.Best.BuyWaypointSymbol,
                    SellWaypointSymbol = route.Best.SellWaypointSymbol,
                    Status = MarketAutomationOpportunityStatus.Pending,
                    Units = route.Best.Units,
                    ExpectedProfit = route.Best.Profit,
                    FeedsTradeSymbol = route.Best.FeedsTradeSymbol,
                    CandidateShipSymbols = route.CandidateShipSymbols,
                    FirstObservedAt = firstSeen.GetValueOrDefault(route.Best.Key, now),
                    LastObservedAt = now,
                }),
        ];

        if (existing is not null && SameRoutes(existing.Opportunities, opportunities))
        {
            return;
        }

        await plans.UpsertAsync(
            PlanTypes.TradingAutomation,
            new TradingAutomationPlanState
            {
                PlanId = existing?.PlanId ?? Guid.NewGuid(),
                Opportunities = opportunities,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now,
            },
            cancellationToken);
    }

    /// <summary>Whether two lists of routes say the same, apart from when they were seen.</summary>
    private static bool SameRoutes(
        IReadOnlyList<TradingAutomationOpportunityState> before,
        IReadOnlyList<TradingAutomationOpportunityState> after)
        => JsonSerializer.Serialize(before.Select(Undated), CompareOptions) == JsonSerializer.Serialize(after.Select(Undated), CompareOptions);

    private static TradingAutomationOpportunityState Undated(TradingAutomationOpportunityState opportunity)
        => opportunity with { FirstObservedAt = default, LastObservedAt = default };

    /// <summary>A route a trader holds through its goal.</summary>
    private sealed record HeldRoute(string ShipSymbol, TradeBetweenMarketsGoal Goal)
    {
        public string Key => TradeRoutePlanner.RouteKey(Goal.TradeSymbol, Goal.BuyWaypointSymbol, Goal.SellWaypointSymbol);
    }

    /// <summary>A lucrative route no trader holds, and the free traders that could have taken it.</summary>
    private sealed record PendingRoute(TradeRoute Best, IReadOnlyList<string> CandidateShipSymbols)
    {
        public PendingRoute WithCandidate(string shipSymbol, TradeRoute route)
            => new(
                TradeRoutePlanner.CompareBestFirst(route, Best) < 0 ? route : Best,
                [.. CandidateShipSymbols, shipSymbol]);
    }
}
