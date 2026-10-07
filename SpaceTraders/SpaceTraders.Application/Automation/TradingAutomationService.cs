using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Probes;
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
    /// the lucrative ones that aren't, and why the other goods with a price gap aren't traded (D76).
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
///   <item>otherwise it gets its best lucrative route (<see cref="TradeRoutePlanner.Rank(TradeMarketMap, ShipModel, long, int, IReadOnlySet{string}, HeldBuys)"/>) that no
///   other trader holds: two traders never share a route. Nor is it sent for a good at a market where another trip is on its
///   way to buy it: one buyer at a time (D80, <see cref="HeldBuys"/>). When several traders are free, the best route goes
///   first, to the trader it is best for. A route carries as many units as each earn <c>Trade.MinProfitPerUnit</c>, in batches
///   of each market's trade volume (D79). The credits are those no trip on its way to buy holds back, and a
///   trip holds back what its cargo costs from the moment it starts until it buys (D57, <see cref="TripReservations"/>);</item>
///   <item>across systems (slice 6.29, D96): a ship whose role is trading takes routes across the systems within the trade
///   reach of the system it is in (<see cref="ITradeContextReader.ReadReachAsync"/>), through the gates; every other trader,
///   whose own work is in its system (a drone, a builder, a shuttle kept for a collection point), takes routes there. A trader
///   stays where its last sale leaves it, and cargo it holds is sold, or jettisoned, in its system;</item>
///   <item>with the spare-time plan on, a ship that gathers in its spare time (the command ship, when the survey
///   plan has nothing for it) trades only for a route that waits for it once its hold is sold, after the other
///   traders (D34): then it sells its hold first, and a spare-time trip that fills its hold is interrupted for it.
///   Otherwise the spare-time plan keeps it (D37).</item>
/// </list>
/// Its state lists the routes traders hold and the best that wait, and for each other good with a price gap why no route of it
/// is listed: the check its route failed for the free trader that got furthest with it (<see cref="TradeRoutePlanner.Judge(TradeMarketMap, ShipModel, long, int, IReadOnlySet{string}, HeldBuys)"/>,
/// slice 2.18, D76).
/// </summary>
/// <remarks>
/// The trip itself, with its check against the newest prices at each market, is the
/// <c>TradeBetweenMarketsGoalExecutor</c>'s. The plan buys its own cargo ships (D21, which replaced D16):
/// when no trader is left without a trip and a new ship would have a lucrative route from the shipyard, it
/// buys the next type in <c>Trade.ShipPurchases</c>, and once the list is bought the ship with the largest hold that a
/// shipyard within the trade reach lists, not SCARCE there (slice 6.33, D112), one at a time and within the credit reserve,
/// when the order ships are bought in lets it (D43, <see cref="IPurchaseOrder"/>).
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
    IConstructionSites constructionSites,
    PassedOverShips passedOver,
    TradeShipDemand demand,
    IAgentRepository agents,
    IGateNetwork gates,
    ILogger<TradingAutomationService> logger) : ITradingAutomationService
{
    /// <summary>The most pending routes the plan's state keeps, best first.</summary>
    internal const int MaxPendingRoutes = 20;

    /// <summary>A good with a price gap whose route waits for a free trader, as the state names it (B66).</summary>
    private const string Waiting = "waiting";

    /// <summary>The supply at which no cargo ship beyond the list is bought (D112).</summary>
    private const string ScarceSupply = "SCARCE";

    /// <summary>The plans whose ships trade only when their own plan passed them over (B63, D58).</summary>
    private static readonly AutomationPlan[] GatheringPlans = [AutomationPlan.Mining, AutomationPlan.Siphon, AutomationPlan.Construction];

    /// <summary>
    /// The setting that lists the cargo ships the plan buys, in order (D21): the Nth is bought while the
    /// fleet has fewer than N cargo ships; empty buys none.
    /// </summary>
    public const string ShipPurchasesSetting = "Trade.ShipPurchases";

    /// <summary>
    /// The setting for what a route must earn, from the shipyard, to count as one a new cargo ship beyond the list would take
    /// (D88).
    /// </summary>
    public const string ShipPurchaseMinRouteProfitSetting = "Trade.ShipPurchaseMinRouteProfit";

    /// <summary>
    /// The setting for how long such a route must have waited, every trader busy, before a cargo ship beyond the list is bought
    /// (D88).
    /// </summary>
    public const string ShipPurchaseWaitMinutesSetting = "Trade.ShipPurchaseWaitMinutes";

    /// <summary>Credits that buy a full hold of anything: whether a new cargo ship would have work, whatever the credits now.</summary>
    private const long AnyCredits = long.MaxValue / 4;

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var board = await FleetRoleBoard.ReadAsync(settings, plans, cancellationToken);
        var earmarked = await ContractOreAsync(cancellationToken);
        var materials = await ConstructionMaterialsAsync(cancellationToken);
        var fleet = await ships.GetAllAsync(cancellationToken);
        var active = await assignments.GetAllActiveAsync(cancellationToken);
        var withAssignment = active
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var held = new List<HeldRoute>();
        var free = new List<ShipModel>();

        // B63: a ship a gathering plan works with trades only when that plan passed it over at its pass in this tick. An
        // arrival's goal step, outside the tick, can free it after that pass; it then waits for its own plan's next one.
        var gatheringPlansOn = new List<AutomationPlan>();
        foreach (var plan in GatheringPlans)
        {
            if (await settings.IsPlanEnabledAsync(plan, cancellationToken))
            {
                gatheringPlansOn.Add(plan);
            }
        }

        // Ships that gather in their spare time (slice 6.8), free or on a spare-time trip that fills its hold, while
        // the spare-time plan is on: the survey plan, which goes first, had nothing for them, and they trade only for
        // a route that waits for them (D34). Any other surveyor surveys, and only that (D20), but for selling or
        // jettisoning a hold nothing else will (D42).
        var gatherers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var surveyorsWithCargo = new List<ShipModel>();
        var goalsOf = new Dictionary<string, ShipGoal?>(StringComparer.OrdinalIgnoreCase);
        foreach (var ship in fleet)
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            goalsOf[ship.Symbol] = goal;
            var hasAssignment = withAssignment.Contains(ship.Symbol);
            var gathers = board.SpareTimeOn && board.GathersInSpareTime(ship);
            if (goal is TradeBetweenMarketsGoal trade && trade.Status != GoalStatus.Blocked)
            {
                held.Add(new HeldRoute(ship.Symbol, trade));
            }
            else if ((gathers ? ship.IsTradingCapable : board.IsTrader(ship) && passedOver.MayTrade(ship.Symbol, gatheringPlansOn))
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
        var judged = new Dictionary<string, JudgedSystem>(StringComparer.OrdinalIgnoreCase);
        var idle = 0;

        // D96, D83, D86: the shuttles kept for a collection point collect at home once a drone is parked there.
        var collectionShuttles = await CollectionShuttlesAsync(cancellationToken);

        // A construction trip on its way to buy holds back its cargo as a trade trip does (slice 6.6, D64), and its material at its
        // buy market against other trips (D80).
        var constructionTrips = await goals.GetActiveConstructionGoalsAsync(cancellationToken);
        var constructionHolds = TripReservations.HeldBack(constructionTrips);
        foreach (var system in free.GroupBy(ship => ship.SystemSymbol ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            // Slice 6.29 (D96): the systems within the trade reach. A ship whose role is trading takes routes across them; one
            // whose own work is in its system trades there, and sells what it holds there.
            var context = await tradeContexts.ReadReachAsync(system.Key, cancellationToken);
            var inSystem = context.Map.WithoutJumps();
            TradeMarketMap MapOf(ShipModel ship) => CrossesSystems(board, ship, collectionShuttles) ? context.Map : inSystem;

            var traders = new List<ShipModel>();
            foreach (var ship in system.Where(ship => !gatherers.Contains(ship.Symbol)))
            {
                if (TradeRoutePlanner.TryFindBestCargoSale(inSystem, ship, mustSell: false, out var cargo, out var sale))
                {
                    var goal = await SellHeldCargoAsync(inSystem, ship, cargo, sale, cancellationToken);
                    held.Add(new HeldRoute(ship.Symbol, goal));
                    heldKeys.Add(held[^1].Key);
                }
                else
                {
                    // Cargo that doesn't pay for its sale would only take room from the route (D42).
                    traders.Add(await JettisonDeadCargoAsync(inSystem, ship, board, earmarked, materials, cancellationToken));
                }
            }

            // D76: why the goods with a price gap that no route will carry aren't traded, as the free traders' checks found it.
            // Without a free trader the plan checks no route here, and what it found before stays.
            var judgements = new List<TradeRouteJudgement>();
            if (traders.Count > 0)
            {
                judged[system.Key] = new JudgedSystem(context.Map, judgements);
            }

            var credits = await AssignRoutesAsync(context, MapOf, traders, held, heldKeys, pending, judgements, constructionTrips, constructionHolds, cancellationToken);
            idle += traders.Count;

            // After the other traders: a ship that gathers in its spare time takes a route that is left for it, in its system.
            foreach (var ship in system.Where(ship => gatherers.Contains(ship.Symbol)))
            {
                credits = await TradeInsteadOfGatheringAsync(context with { Map = inSystem }, credits, ship, onTrip.Contains(ship.Symbol), held, heldKeys, HeldBuysOf(held, constructionTrips), cancellationToken);
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
                    await JettisonDeadCargoAsync(map, ship, board, earmarked, materials, cancellationToken);
                }
            }
        }

        // Business stays where our ships work: not where the command ship explores (asked on 2026-10-04).
        await BuyCargoShipAsync(fleet, goalsOf, board, BusinessSystems.Of(await agents.GetAsync(cancellationToken)), heldKeys, HeldBuysOf(held, constructionTrips), idle, cancellationToken);

        await SaveStateAsync(held, pending, judged, cancellationToken);
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
    /// The materials the home system's jump gate still needs while the construction plan is on (slice 6.6, D68): a ship that
    /// holds them keeps them, and the construction plan, which comes first, has it supply them. Empty with the plan off.
    /// </summary>
    private async Task<IReadOnlySet<string>> ConstructionMaterialsAsync(CancellationToken cancellationToken)
        => await settings.IsPlanEnabledAsync(AutomationPlan.Construction, cancellationToken)
            ? (await constructionSites.CachedNeedingMaterialsAsync(cancellationToken))
                .SelectMany(site => site.Materials.Where(material => material.Fulfilled < material.Required))
                .Select(material => material.TradeSymbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The shuttles the mining plan keeps for its collection points (slice 6.18, D83): each trades until one of its point's drones
    /// is parked there (D86), and then collects, at home.
    /// </summary>
    private async Task<IReadOnlySet<string>> CollectionShuttlesAsync(CancellationToken cancellationToken)
        => ((await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, cancellationToken))?.CollectionPoints ?? [])
            .SelectMany(point => point.ShuttleSymbols)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a trader takes routes across systems (slice 6.29, D96): one whose role is trading, the trade role on the board (with
    /// the board off, a cargo ship, or an explorer the explore plan has no system for, D102), and not a shuttle kept for a
    /// collection point (D83, D86). A drone, which gathers first (D58), a builder (D65), or the survey ship in its spare time (D34)
    /// trades in its own system between its trips, as its own work is there, and every other plan works at home (D60): a trip
    /// abroad would leave that work undone.
    /// </summary>
    private static bool CrossesSystems(FleetRoleBoard board, ShipModel ship, IReadOnlySet<string> collectionShuttles)
        => !collectionShuttles.Contains(ship.Symbol)
            && (board.RolesOn ? board.RoleOf(ship) == FleetRole.Trade : FleetRoles.IsCargoShip(ship) || FleetRoles.IsExplorer(ship));

    /// <summary>
    /// Jettisons what a free ship holds that nothing will sell or use (D42), when no good aboard pays for its sale:
    /// everything but the contract's ore on a ship that mines for the contract, and the materials a jump gate still needs
    /// (slice 6.6).
    /// </summary>
    /// <returns>The ship with the hold it has left.</returns>
    private async Task<ShipModel> JettisonDeadCargoAsync(
        TradeMarketMap map,
        ShipModel ship,
        FleetRoleBoard board,
        string contractOre,
        IReadOnlySet<string> constructionMaterials,
        CancellationToken cancellationToken)
    {
        var current = ship;
        foreach (var (cargo, reason) in HeldCargo.ToJettison(
            map,
            ship,
            good => (contractOre.Length > 0 && good.Equals(contractOre, StringComparison.OrdinalIgnoreCase) && board.MinesForContract(ship))
                || constructionMaterials.Contains(good)))
        {
            if (await cargoJettison.JettisonAsync(current, cargo, reason, cancellationToken) is { } left)
            {
                current = current with { CargoInventory = left.Inventory, CargoCurrent = left.Units };
            }
        }

        return current;
    }

    /// <summary>
    /// Buys cargo ships (D21): the next in <c>Trade.ShipPurchases</c>, at the home shipyard that sells it for the least; once the
    /// list is bought, the ship with the largest hold that a shipyard within the trade reach lists, not SCARCE there (slice 6.33,
    /// D112, <see cref="LargestHoldAsync"/>). Either is bought when every trader has a trip and the traders can't keep up: a
    /// route worth <c>Trade.ShipPurchaseMinRouteProfit</c> that the new ship would have from there, and no trader holds, has
    /// waited <c>Trade.ShipPurchaseWaitMinutes</c> for a ship (D88, <see cref="TradeShipDemand"/>); the purchase is then judged
    /// with the credits left after it. The purchase keeps the credit reserve (<c>FleetExpansion.MinCreditReserve</c>,
    /// <see cref="IShipPurchaseService"/>). The order ships are bought in decides when (<see cref="IPurchaseOrder"/>): a ship
    /// of the list is saved up for, whatever the routes, after the contract's drone, a surveyor and a drone for each scarce
    /// mineral; a ship beyond the list takes turns with the drones, and needs nothing until a route has waited so.
    /// </summary>
    private async Task BuyCargoShipAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyDictionary<string, ShipGoal?> goalsOf,
        FleetRoleBoard board,
        IReadOnlyList<string> businessSystems,
        IReadOnlySet<string> heldKeys,
        HeldBuys heldBuys,
        int idle,
        CancellationToken cancellationToken)
    {
        var purchases = (await settings.GetAsync<string>(ShipPurchasesSetting, cancellationToken) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (purchases.Length == 0)
        {
            await purchaseOrder.ReportAsync(AutomationPlan.Trading, PurchaseNeed.None, cancellationToken);
            return;
        }

        // A shuttle collecting at a far asteroid (D83) was bought for that, not from the list.
        var cargoShips = fleet.Count(ship => FleetRoles.IsCargoShip(ship) && !board.IsCollector(ship));
        var inList = cargoShips < purchases.Length;
        var offer = inList
            ? await ListedAsync(purchases[cargoShips], businessSystems, cancellationToken)
            : await LargestHoldAsync(fleet, goalsOf, businessSystems, cancellationToken);
        if (offer is not { } found)
        {
            await purchaseOrder.ReportAsync(AutomationPlan.Trading, PurchaseNeed.None, cancellationToken);
            return;
        }

        // Slice 6.29 (D96): a cargo ship trades across the systems in reach, so the routes that wait for one count there too.
        var (shipyard, forSale) = found;
        var shipType = forSale.Type;
        var context = await tradeContexts.ReadReachAsync(shipyard.SystemSymbol, cancellationToken);
        var newShip = AsBought(shipyard, forSale);

        // D88: beyond the list a cargo ship adds value only when the traders can't keep up: a route worth
        // Trade.ShipPurchaseMinRouteProfit from the shipyard, that no trader holds, has waited Trade.ShipPurchaseWaitMinutes with
        // every trader busy. A stable market never gets there; till then the drones' turn comes (D43).
        var minRouteProfit = await settings.GetAsync<long>(ShipPurchaseMinRouteProfitSetting, cancellationToken);
        var waited = demand.Note(
            TradeRoutePlanner.Rank(context.Map, newShip, AnyCredits, context.MinProfitPerUnit, heldKeys, heldBuys)
                .Where(route => route.Profit >= minRouteProfit)
                .Select(route => route.Key),
            TimeProvider.System.GetUtcNow());
        var hasWork = idle == 0
            && waited >= TimeSpan.FromMinutes(await settings.GetAsync<int>(ShipPurchaseWaitMinutesSetting, cancellationToken));
        var need = inList || hasWork
            ? new PurchaseNeed(inList ? PurchaseTier.CargoShips : PurchaseTier.Alternating, shipType, shipyard.WaypointSymbol, forSale.PurchasePrice)
            : PurchaseNeed.None;
        if (!await purchaseOrder.ReportAsync(AutomationPlan.Trading, need, cancellationToken) || idle > 0)
        {
            return;
        }

        var creditsForCargo = Math.Max(0, context.Credits - forSale.PurchasePrice - context.FuelReserveCredits);
        var routes = TradeRoutePlanner.Rank(context.Map, newShip, creditsForCargo, context.MinProfitPerUnit, heldKeys, heldBuys);
        if (routes.Count == 0)
        {
            logger.LogDebug(
                "Trading plan: a new {ShipType} from {Shipyard} would have no lucrative route; no purchase.",
                shipType,
                shipyard.WaypointSymbol);
            return;
        }

        // D112: beyond the list the ship isn't bought where the shipyard has it SCARCE now, whatever the cache said.
        var purchased = inList
            ? await shipPurchases.TryPurchaseAsync(shipType, shipyard.WaypointSymbol, cancellationToken)
            : await shipPurchases.TryPurchaseUnlessScarceAsync(shipType, shipyard.WaypointSymbol, cancellationToken);
        if (!purchased.IsSuccess)
        {
            logger.LogDebug(
                "Trading plan: {ShipType} purchase denied at {Shipyard} — {Reason}.",
                shipType,
                shipyard.WaypointSymbol,
                purchased.FailureReason ?? "Purchase failed.");
        }
    }

    /// <summary>The next ship of <c>Trade.ShipPurchases</c> (D21), at the shipyard at home that sells it for the least.</summary>
    /// <returns>The shipyard and the ship as it lists it; null when no shipyard at home lists it with a price and a hold.</returns>
    private async Task<(ShipyardWaypointDto Shipyard, ShipyardShipDto Ship)?> ListedAsync(
        string shipType,
        IReadOnlyList<string> businessSystems,
        CancellationToken cancellationToken)
    {
        var systems = businessSystems.ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            .FirstOrDefault();
        if (offer.Shipyard is null)
        {
            logger.LogDebug("Trading plan: no shipyard with a known price and hold for {ShipType}.", shipType);
            return null;
        }

        return offer;
    }

    /// <summary>
    /// The cargo ship bought once <c>Trade.ShipPurchases</c> is (slice 6.33, D112), asked on 2026-10-07: "when buying a new trade
    /// ship (purchasing order 9) it should pick whatever the known ship with the highest cargo capacity is, as long as it is not
    /// scarce", with "Within the trade reach" and "Next largest". Of the ships the shipyards within <c>Trade.MaxHaulDistance</c>
    /// jumps of home list with their hold, those that would be cargo ships (<see cref="FleetRoles.IsCargoShip"/>: a hold and a
    /// tank, nothing to mine, siphon or survey with, no explorer) and that the shipyard doesn't have SCARCE, as cached: the
    /// largest hold, of equal holds the cheapest, then the nearest to home. A shipyard abroad counts only where a probe of ours
    /// answers a purchase's call for a ship (D30), so the purchase can't wait for a ship that never comes.
    /// </summary>
    /// <returns>The shipyard and the ship as it lists it; null when no shipyard within the reach lists one.</returns>
    private async Task<(ShipyardWaypointDto Shipyard, ShipyardShipDto Ship)?> LargestHoldAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyDictionary<string, ShipGoal?> goalsOf,
        IReadOnlyList<string> businessSystems,
        CancellationToken cancellationToken)
    {
        if (businessSystems.FirstOrDefault() is not { Length: > 0 } home)
        {
            return null;
        }

        var reach = await settings.GetAsync<int>(TradeContextReader.MaxHaulDistanceSetting, cancellationToken) is var maxJumps and > 0
            ? maxJumps
            : TradeContextReader.DefaultMaxHaulDistance;
        var network = await gates.ReadAsync(cancellationToken);
        var jumps = network is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [home] = 0 }
            : ExploreAtlas.Reachable(network, home, TimeProvider.System.GetUtcNow());
        var answered = await AnsweredAsync(fleet, goalsOf, home, cancellationToken);
        var offer = (await shipyards.GetAllAsync(cancellationToken))
            .Where(shipyard => jumps.TryGetValue(shipyard.SystemSymbol, out var away)
                && away <= reach
                && answered.Contains(shipyard.SystemSymbol))
            .SelectMany(shipyard => shipyard.Ships
                .Where(ship => ship.PurchasePrice > 0
                    && !ScarceSupply.Equals(ship.Supply, StringComparison.OrdinalIgnoreCase)
                    && FleetRoles.IsCargoShip(AsBought(shipyard, ship)))
                .Select(ship => (Shipyard: shipyard, Ship: ship)))
            .OrderByDescending(candidate => candidate.Ship.CargoCapacity)
            .ThenBy(candidate => candidate.Ship.PurchasePrice)
            .ThenBy(candidate => jumps[candidate.Shipyard.SystemSymbol])
            .ThenBy(candidate => candidate.Shipyard.WaypointSymbol, StringComparer.Ordinal)
            .FirstOrDefault();
        if (offer.Shipyard is null)
        {
            logger.LogDebug("Trading plan: no shipyard within the trade reach lists a cargo ship that isn't SCARCE (D112).");
            return null;
        }

        return offer;
    }

    /// <summary>
    /// The systems where a purchase's call for one of our ships (D30) is answered: home, where the plans have always bought; and
    /// each system a probe of ours counts for (<see cref="ProbePlanner.Whereabouts"/>), there or on its way there, while the
    /// probe plan, which answers the calls, is on.
    /// </summary>
    private async Task<HashSet<string>> AnsweredAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyDictionary<string, ShipGoal?> goalsOf,
        string home,
        CancellationToken cancellationToken)
    {
        var answered = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { home };
        if (await settings.IsPlanEnabledAsync(AutomationPlan.ProbeDeployment, cancellationToken))
        {
            foreach (var probe in fleet.Where(FleetRoles.IsProbe))
            {
                answered.Add(ProbePlanner.Whereabouts(probe, goalsOf.GetValueOrDefault(probe.Symbol)).SystemSymbol);
            }
        }

        return answered;
    }

    /// <summary>A ship for sale as the plans would see it once bought: at the shipyard, docked, its tank full and its hold empty.</summary>
    private static ShipModel AsBought(ShipyardWaypointDto shipyard, ShipyardShipDto forSale)
        => new(
            "NEW-" + forSale.Type,
            shipyard.SystemSymbol,
            shipyard.WaypointSymbol,
            "DOCKED",
            "CRUISE",
            forSale.FuelCapacity,
            forSale.FuelCapacity,
            CargoCapacity: forSale.CargoCapacity,
            ShipType: forSale.Type,
            MountSymbols: forSale.Mounts);

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
        HeldBuys heldBuys,
        CancellationToken cancellationToken)
    {
        var routes = TradeRoutePlanner.Rank(context.Map, GatherPlanner.AfterSellingHold(context.Map, ship), credits, context.MinProfitPerUnit, heldKeys, heldBuys);
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
    /// best route ranks highest gets it, and that route is no longer open to the others. A route's rate counts its trip from
    /// where each trader is (D95), so of two traders the nearer one gets a route both could fly. Each trader's routes are those
    /// of its own map (<paramref name="mapOf"/>): the systems in reach, or its own system (slice 6.29).
    /// </summary>
    /// <param name="mapOf">The map a trader's routes come from.</param>
    /// <param name="judgements">Gets how each trader's routes with a price gap fared (D76).</param>
    /// <returns>The credits left for cargo once the routes' purchases are counted.</returns>
    private async Task<long> AssignRoutesAsync(
        TradeContext context,
        Func<ShipModel, TradeMarketMap> mapOf,
        List<ShipModel> traders,
        List<HeldRoute> held,
        HashSet<string> heldKeys,
        Dictionary<string, PendingRoute> pending,
        List<TradeRouteJudgement> judgements,
        IReadOnlyDictionary<string, SupplyConstructionGoal> constructionTrips,
        long constructionHolds,
        CancellationToken cancellationToken)
    {
        // Cargo leaves Trade.FuelReserveCredits for fuel (D24), and what the trade and construction trips on their way to buy
        // hold back (D57, D64).
        var credits = Math.Max(0, context.CreditsForCargo - held.Sum(route => TripReservations.HeldBack(route.Goal)) - constructionHolds);

        // What each trader could do before anything is handed out: the routes the ShipLeftIdle rule
        // counts as work waiting for it (D13); and the trip it saves up for, when it does (D56). Its lucrative routes
        // are Rank's; the others say which check they fail (D76).
        var heldBuys = HeldBuysOf(held, constructionTrips);
        foreach (var trader in traders)
        {
            NoteSaving(mapOf(trader), context.MinProfitPerUnit, trader, credits, heldKeys, heldBuys);
            var judged = TradeRoutePlanner.Judge(mapOf(trader), trader, credits, context.MinProfitPerUnit, heldKeys, heldBuys);
            judgements.AddRange(judged);
            foreach (var route in judged.Where(judgement => judgement.Check == TradeRouteCheck.Lucrative).Select(judgement => judgement.Route))
            {
                pending[route.Key] = pending.TryGetValue(route.Key, out var known)
                    ? known.WithCandidate(trader.Symbol, route)
                    : new PendingRoute(route, [trader.Symbol]);
            }
        }

        while (traders.Count > 0)
        {
            // D80: a route given out this pass holds its good at its buy market as a trip from an earlier pass does.
            heldBuys = HeldBuysOf(held, constructionTrips);
            var found = false;
            var bestShip = traders[0];
            var bestRoute = new TradeRoute(string.Empty, string.Empty, string.Empty, 0, 0, 0, 0, 0, string.Empty);
            foreach (var trader in traders)
            {
                var routes = TradeRoutePlanner.Rank(mapOf(trader), trader, credits, context.MinProfitPerUnit, heldKeys, heldBuys);
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
    /// all the units that trip would carry (D79) and its fuel; ships are bought after it (<see cref="FullHoldSavings"/>).
    /// Meanwhile it takes the best trip it can pay for: fewer units, or another route. A trader whose best route is one it can
    /// pay for saves up for nothing more, unless that is the route it saved up for: then the saving lasts until it buys.
    /// </summary>
    private void NoteSaving(TradeMarketMap map, int minProfitPerUnit, ShipModel trader, long credits, IReadOnlySet<string> heldKeys, HeldBuys heldBuys)
    {
        var routes = TradeRoutePlanner.Rank(map, trader, AnyCredits, minProfitPerUnit, heldKeys, heldBuys);
        if (routes.Count == 0)
        {
            savings.Clear(trader.Symbol);
            return;
        }

        // Slice 6.29: a trip through the gates pays its antimatter too, and keeps the credit floor besides (D63), which ship
        // purchases keep anyway.
        var best = routes[0];
        var cost = ((long)best.Units * best.BuyPrice) + best.FuelCost + best.AntimatterCost;
        if (cost + (best.Jumps > 0 ? map.Gates.CreditFloor : 0) > credits)
        {
            if (savings.SaveFor(trader.Symbol, best.Key, cost))
            {
                logger.LogInformation(
                    "Trading plan: ship {ShipSymbol} saves up for {Units} {TradeSymbol} from {BuyWaypoint} to {SellWaypoint}, {Cost} credits with its fuel, against {CreditsForCargo} for cargo now; ships are bought after it (D56).",
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
    /// A trip along a route: buy at its buy market, sell at its sell market. It holds back what its cargo is expected to cost,
    /// batch by batch, until it buys (D57, D79).
    /// </summary>
    private static TradeBetweenMarketsGoal RouteGoal(TradeRoute route)
        => new()
        {
            TradeSymbol = route.TradeSymbol,
            BuyWaypointSymbol = route.BuyWaypointSymbol,
            SellWaypointSymbol = route.SellWaypointSymbol,
            Units = route.Units,
            ExpectedProfit = route.Profit,
            ExpectedSeconds = WholeSeconds(route.Seconds),
            Jumps = route.Jumps,
            FeedsTradeSymbol = route.FeedsTradeSymbol,
            ReservedCredits = route.CargoCost,
        };

    /// <summary>A trip's time as the goal and the state keep it (D95): whole seconds, so a fraction never rewrites the state.</summary>
    private static int WholeSeconds(double seconds) => (int)Math.Round(seconds, MidpointRounding.AwayFromZero);

    /// <summary>
    /// What the trade and construction trips on their way to buy hold at their buy markets (D80): the trips of earlier passes,
    /// and the routes this pass has given out so far.
    /// </summary>
    private static HeldBuys HeldBuysOf(IEnumerable<HeldRoute> held, IReadOnlyDictionary<string, SupplyConstructionGoal> constructionTrips)
        => HeldBuys.Of(held.Select(route => route.Goal), constructionTrips.Values);

    private void LogRoute(ShipModel ship, TradeRoute route)
    {
        // D95: the journal gives the rate the route was ranked by, and the trip's time it was worked out over.
        var creditsPerHour = (long)Math.Round(route.CreditsPerHour);
        var tripMinutes = TripTime.Minutes(route.Seconds);
        if (route.Jumps > 0)
        {
            // Slice 6.29 (D96): a route through the gates names its jumps, and what their antimatter costs.
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} trades {Units} {TradeSymbol} from {BuyWaypoint} ({BuyPrice} each) to {SellWaypoint} ({SellPrice} each), {Jumps} jumps through the gates; about {ExpectedProfit} credits after {FuelCost} for fuel and {AntimatterCost} for antimatter, the way to the buy market included: {CreditsPerHour} an hour over about {TripMinutes} minutes.",
                JournalEvents.TradeStarted,
                ship.Symbol,
                route.Units,
                route.TradeSymbol,
                route.BuyWaypointSymbol,
                route.BuyPrice,
                route.SellWaypointSymbol,
                route.SellPrice,
                route.Jumps,
                route.Profit,
                route.FuelCost,
                route.AntimatterCost,
                creditsPerHour,
                tripMinutes);
        }
        else if (route.FeedsConstruction)
        {
            // D89: a route that feeds the jump gate's materials comes before every other; the journal says so.
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} trades {Units} {TradeSymbol} from {BuyWaypoint} ({BuyPrice} each) to {SellWaypoint} ({SellPrice} each), which makes the jump gate's {ConstructionMaterial} from it (D89); about {ExpectedProfit} credits after {FuelCost} for fuel, the flight to the buy market included: {CreditsPerHour} an hour over about {TripMinutes} minutes.",
                JournalEvents.TradeStarted,
                ship.Symbol,
                route.Units,
                route.TradeSymbol,
                route.BuyWaypointSymbol,
                route.BuyPrice,
                route.SellWaypointSymbol,
                route.SellPrice,
                route.ConstructionMaterial,
                route.Profit,
                route.FuelCost,
                creditsPerHour,
                tripMinutes);
        }
        else if (route.FeedsTradeSymbol.Length > 0)
        {
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} trades {Units} {TradeSymbol} from {BuyWaypoint} ({BuyPrice} each) to {SellWaypoint} ({SellPrice} each), which makes {FeedsTradeSymbol} from it; about {ExpectedProfit} credits after {FuelCost} for fuel, the flight to the buy market included: {CreditsPerHour} an hour over about {TripMinutes} minutes.",
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
                route.FuelCost,
                creditsPerHour,
                tripMinutes);
        }
        else
        {
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} trades {Units} {TradeSymbol} from {BuyWaypoint} ({BuyPrice} each) to {SellWaypoint} ({SellPrice} each); about {ExpectedProfit} credits after {FuelCost} for fuel, the flight to the buy market included: {CreditsPerHour} an hour over about {TripMinutes} minutes.",
                JournalEvents.TradeStarted,
                ship.Symbol,
                route.Units,
                route.TradeSymbol,
                route.BuyWaypointSymbol,
                route.BuyPrice,
                route.SellWaypointSymbol,
                route.SellPrice,
                route.Profit,
                route.FuelCost,
                creditsPerHour,
                tripMinutes);
        }
    }

    /// <summary>
    /// Records the held routes and the best pending ones, and why the other goods with a price gap aren't traded (D76). Only a
    /// change is written: the state is the same pass after pass while nothing happens, and the tick runs every 5 seconds.
    /// </summary>
    private async Task SaveStateAsync(
        IReadOnlyList<HeldRoute> held,
        IReadOnlyDictionary<string, PendingRoute> pending,
        IReadOnlyDictionary<string, JudgedSystem> judged,
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
                    ExpectedSeconds = route.Goal.ExpectedSeconds,
                    Jumps = route.Goal.Jumps,
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
                    ExpectedSeconds = WholeSeconds(route.Best.Seconds),
                    Jumps = route.Best.Jumps,
                    FeedsTradeSymbol = route.Best.FeedsTradeSymbol,
                    CandidateShipSymbols = route.CandidateShipSymbols,
                    FirstObservedAt = firstSeen.GetValueOrDefault(route.Best.Key, now),
                    LastObservedAt = now,
                }),
        ];

        // D76, B66: a good that a trader's route carries is traded; every other good with a price gap says why not, one whose
        // route waits for a free trader included: the waiting routes go once no trader is free, and its reason stays. A system
        // without a free trader at this pass keeps what the plan found there before. Routes reach across systems (slice 6.29),
        // so a good counts as traded, or waiting, wherever its route buys it.
        var traded = GoodsOf(opportunities.Where(route => route.Status == MarketAutomationOpportunityStatus.Assigned));
        var waiting = GoodsOf(opportunities.Where(route => route.Status == MarketAutomationOpportunityStatus.Pending));
        List<TradingAutomationGoodNotTradedState> notTraded =
        [
            .. (existing?.NotTraded ?? [])
                .Where(good => !judged.ContainsKey(good.SystemSymbol))
                .Concat(judged.SelectMany(system => NotTradedIn(system.Key, system.Value, waiting, now)))
                .Where(good => !traded.Contains(good.TradeSymbol))
                .OrderBy(good => good.SystemSymbol, StringComparer.Ordinal),
        ];

        if (existing is not null && SameRoutes(existing.Opportunities, opportunities) && SameGoods(existing.NotTraded, notTraded))
        {
            return;
        }

        await plans.UpsertAsync(
            PlanTypes.TradingAutomation,
            new TradingAutomationPlanState
            {
                PlanId = existing?.PlanId ?? Guid.NewGuid(),
                Opportunities = opportunities,
                NotTraded = notTraded,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now,
            },
            cancellationToken);
    }

    /// <summary>
    /// Why the goods with a price gap in a system aren't traded (D76): for each, the route and free trader that got furthest
    /// through the checks. A good whose furthest route is lucrative waits for a free trader when one of its routes is among the
    /// waiting routes the state keeps (B66), and otherwise ranks below them.
    /// </summary>
    private static IEnumerable<TradingAutomationGoodNotTradedState> NotTradedIn(
        string systemSymbol,
        JudgedSystem system,
        IReadOnlySet<string> waiting,
        DateTimeOffset now)
        => TradeRouteJudgement.FurthestPerGood(system.Judgements).Select(judgement =>
        {
            var waits = judgement.Check == TradeRouteCheck.Lucrative && waiting.Contains(judgement.Route.TradeSymbol);
            return new TradingAutomationGoodNotTradedState
            {
                SystemSymbol = systemSymbol,
                TradeSymbol = judgement.Route.TradeSymbol,
                Reason = waits ? Waiting : ReasonOf(judgement.Check),
                ShipSymbol = judgement.Ship.Symbol,
                BuyWaypointSymbol = judgement.Route.BuyWaypointSymbol,
                SellWaypointSymbol = judgement.Route.SellWaypointSymbol,
                Why = judgement.Check != TradeRouteCheck.Lucrative
                    ? judgement.Why(system.Map)
                    : waits
                        ? $"{judgement.Why(system.Map)} It waits for a free trader: the free traders took routes that rank higher."
                        : string.Create(CultureInfo.InvariantCulture, $"{judgement.Why(system.Map)} The {MaxPendingRoutes} waiting routes listed rank higher."),
                JudgedAt = now,
            };
        });

    /// <summary>The goods the routes carry.</summary>
    private static HashSet<string> GoodsOf(IEnumerable<TradingAutomationOpportunityState> routes)
        => routes
            .Select(route => route.TradeSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The check a good's route failed, as the state names it (D76).</summary>
    private static string ReasonOf(TradeRouteCheck check) => check switch
    {
        TradeRouteCheck.BuyMarketOutOfReach => "buy_market_out_of_reach",
        TradeRouteCheck.SellMarketOutOfReach => "sell_market_out_of_reach",
        TradeRouteCheck.NoRoom => "no_room",
        TradeRouteCheck.TooFewCredits => "too_few_credits",
        TradeRouteCheck.NotLucrative => "not_lucrative",
        _ => "below_the_listed_routes",
    };

    /// <summary>Whether two lists of routes say the same, apart from when they were seen.</summary>
    private static bool SameRoutes(
        IReadOnlyList<TradingAutomationOpportunityState> before,
        IReadOnlyList<TradingAutomationOpportunityState> after)
        => JsonSerializer.Serialize(before.Select(Undated), CompareOptions) == JsonSerializer.Serialize(after.Select(Undated), CompareOptions);

    /// <summary>Whether two lists of goods not traded say the same, apart from when they were found.</summary>
    private static bool SameGoods(
        IReadOnlyList<TradingAutomationGoodNotTradedState> before,
        IReadOnlyList<TradingAutomationGoodNotTradedState> after)
        => before.Select(good => good with { JudgedAt = default }).SequenceEqual(after.Select(good => good with { JudgedAt = default }));

    private static TradingAutomationOpportunityState Undated(TradingAutomationOpportunityState opportunity)
        => opportunity with { FirstObservedAt = default, LastObservedAt = default };

    /// <summary>A route a trader holds through its goal.</summary>
    private sealed record HeldRoute(string ShipSymbol, TradeBetweenMarketsGoal Goal)
    {
        public string Key => TradeRoutePlanner.RouteKey(Goal.TradeSymbol, Goal.BuyWaypointSymbol, Goal.SellWaypointSymbol);
    }

    /// <summary>A system whose free traders' routes were checked at this pass, and the map they were checked on (D76).</summary>
    private sealed record JudgedSystem(TradeMarketMap Map, List<TradeRouteJudgement> Judgements);

    /// <summary>A lucrative route no trader holds, and the free traders that could have taken it.</summary>
    private sealed record PendingRoute(TradeRoute Best, IReadOnlyList<string> CandidateShipSymbols)
    {
        public PendingRoute WithCandidate(string shipSymbol, TradeRoute route)
            => new(
                TradeRoutePlanner.CompareBestFirst(route, Best) < 0 ? route : Best,
                [.. CandidateShipSymbols, shipSymbol]);
    }
}
