using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
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
///   no open assignment, and isn't in transit, and that the survey and mining plans, which go first, left
///   free: with the survey plan on, a ship that can survey never trades (D20, slice 6.4);</item>
///   <item>a trader that holds cargo first sells it where it fetches the most after fuel, when that
///   earns anything;</item>
///   <item>otherwise it gets its best lucrative route (<see cref="TradeRoutePlanner.Rank"/>) that no
///   other trader holds: two traders never share a route. When several traders are free, the best
///   route goes first, to the trader it is best for.</item>
/// </list>
/// </summary>
/// <remarks>
/// The trip itself, with its check against the newest prices at each market, is the
/// <c>TradeBetweenMarketsGoalExecutor</c>'s. The plan buys its own cargo ships (D21, which replaced D16):
/// when no trader is left without a trip and a new ship would have a lucrative route from the shipyard, it
/// buys the next type in <c>Trade.ShipPurchases</c>, one at a time and within the credit reserve.
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
    ILogger<TradingAutomationService> logger) : ITradingAutomationService
{
    /// <summary>The most pending routes the plan's state keeps, best first.</summary>
    internal const int MaxPendingRoutes = 20;

    /// <summary>
    /// The setting that lists the cargo ships the plan buys, in order (D21): the Nth is bought while the
    /// fleet has fewer than N cargo ships; empty buys none.
    /// </summary>
    public const string ShipPurchasesSetting = "Trade.ShipPurchases";

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var surveyOn = await settings.IsPlanEnabledAsync(AutomationPlan.Survey, cancellationToken);
        var fleet = await ships.GetAllAsync(cancellationToken);
        var withAssignment = (await assignments.GetAllActiveAsync(cancellationToken))
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var held = new List<HeldRoute>();
        var free = new List<ShipModel>();
        foreach (var ship in fleet)
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (goal is TradeBetweenMarketsGoal trade && trade.Status != GoalStatus.Blocked)
            {
                held.Add(new HeldRoute(ship.Symbol, trade));
            }
            else if (ship.IsTradingCapable
                && !FleetRoles.IsSurveyor(ship, surveyOn)
                && FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                free.Add(ship);
            }
        }

        var heldKeys = held.Select(route => route.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new Dictionary<string, PendingRoute>(StringComparer.OrdinalIgnoreCase);
        var idle = 0;
        foreach (var system in free.GroupBy(ship => ship.SystemSymbol ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var context = await tradeContexts.ReadAsync(system.Key, cancellationToken);
            var traders = new List<ShipModel>();
            foreach (var ship in system)
            {
                if (TryFindCargoSale(context.Map, ship, out var cargo, out var sale))
                {
                    var goal = await SellHeldCargoAsync(context.Map, ship, cargo, sale, cancellationToken);
                    held.Add(new HeldRoute(ship.Symbol, goal));
                    heldKeys.Add(held[^1].Key);
                }
                else
                {
                    traders.Add(ship);
                }
            }

            await AssignRoutesAsync(context, traders, held, heldKeys, pending, cancellationToken);
            idle += traders.Count;
        }

        // A new cargo ship only when every trader has a trip (D21).
        if (idle == 0)
        {
            await BuyCargoShipAsync(fleet, heldKeys, cancellationToken);
        }

        await SaveStateAsync(held, pending, cancellationToken);
    }

    /// <summary>
    /// Buys the next cargo ship in <c>Trade.ShipPurchases</c> (D21), at the shipyard that sells it for the
    /// least, when it would have a lucrative route from there that no trader holds. The purchase keeps the
    /// credit reserve (<c>FleetExpansion.MinCreditReserve</c>, <see cref="IShipPurchaseService"/>), and the
    /// route is judged with the credits left after it.
    /// </summary>
    private async Task BuyCargoShipAsync(IReadOnlyList<ShipModel> fleet, IReadOnlySet<string> heldKeys, CancellationToken cancellationToken)
    {
        var purchases = (await settings.GetAsync<string>(ShipPurchasesSetting, cancellationToken) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cargoShips = fleet.Count(FleetRoles.IsCargoShip);
        if (cargoShips >= purchases.Length)
        {
            return;
        }

        var shipType = purchases[cargoShips];
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
    /// For a trader that holds cargo: the good that fetches the most, after the fuel to where it sells
    /// best, when that is anything at all. A good no reachable market buys, or one that doesn't pay
    /// for the fuel to sell it, stays aboard and takes up room.
    /// </summary>
    private static bool TryFindCargoSale(TradeMarketMap map, ShipModel ship, out CargoItemModel cargo, out TradeSale sale)
    {
        cargo = new CargoItemModel(string.Empty, 0);
        sale = new TradeSale(string.Empty, 0, 0, 0);
        var found = false;
        foreach (var item in (ship.CargoInventory ?? []).Where(item => item.Units > 0))
        {
            if (TradeRoutePlanner.TryFindBestSale(map, ship, item.Symbol, item.Units, out var candidate)
                && candidate.NetRevenue > 0
                && (!found || candidate.NetRevenue > sale.NetRevenue))
            {
                cargo = item;
                sale = candidate;
                found = true;
            }
        }

        return found;
    }

    private async Task<TradeBetweenMarketsGoal> SellHeldCargoAsync(
        TradeMarketMap map,
        ShipModel ship,
        CargoItemModel cargo,
        TradeSale sale,
        CancellationToken cancellationToken)
    {
        var goal = new TradeBetweenMarketsGoal
        {
            TradeSymbol = cargo.Symbol,
            BuyWaypointSymbol = ship.WaypointSymbol ?? string.Empty,
            SellWaypointSymbol = sale.WaypointSymbol,
            Units = cargo.Units,
            ExpectedProfit = sale.NetRevenue,
            FeedsTradeSymbol = map.PricierGoodMadeFrom(sale.WaypointSymbol, cargo.Symbol),
            CargoBought = true,
        };
        await goals.SetActiveGoalAsync(ship.Symbol, goal, cancellationToken);

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
        return goal;
    }

    /// <summary>
    /// Gives the free traders of one system their routes, best route first: each round, the trader whose
    /// best route ranks highest gets it, and that route is no longer open to the others.
    /// </summary>
    private async Task AssignRoutesAsync(
        TradeContext context,
        List<ShipModel> traders,
        List<HeldRoute> held,
        HashSet<string> heldKeys,
        Dictionary<string, PendingRoute> pending,
        CancellationToken cancellationToken)
    {
        // Cargo leaves Trade.FuelReserveCredits for fuel (D24).
        var credits = context.CreditsForCargo;

        // What each trader could do before anything is handed out: the routes the ShipLeftIdle rule
        // counts as work waiting for it (D13).
        foreach (var trader in traders)
        {
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
            credits -= bestRoute.Units * bestRoute.BuyPrice;
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
    }

    private async Task<TradeBetweenMarketsGoal> StartRouteAsync(ShipModel ship, TradeRoute route, CancellationToken cancellationToken)
    {
        var goal = new TradeBetweenMarketsGoal
        {
            TradeSymbol = route.TradeSymbol,
            BuyWaypointSymbol = route.BuyWaypointSymbol,
            SellWaypointSymbol = route.SellWaypointSymbol,
            Units = route.Units,
            ExpectedProfit = route.Profit,
            FeedsTradeSymbol = route.FeedsTradeSymbol,
        };
        await goals.SetActiveGoalAsync(ship.Symbol, goal, cancellationToken);

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

        return goal;
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
