using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Siphoning;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>The siphon plan (PLAN.md slice 6.7).</summary>
public interface ISiphonAutomationService
{
    /// <summary>One pass of the plan: gives every free siphoner a trip, and buys a drone for a scarce gas or a market short of one.</summary>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The siphon plan (PLAN.md slice 6.7): the mining plan (<see cref="MiningAutomationService"/>) for gases. Each
/// tick it gives every free siphoner one trip (<see cref="SiphonAndSellGoal"/>):
/// <list type="bullet">
///   <item>a siphoner is a ship with a gas siphon, a hold and a tank, and nothing to mine or survey with
///   (<see cref="FleetRoles.IsSiphoner"/>);</item>
///   <item>a siphoner that holds goods a market buys sells them first, one good a trip, where each fetches most
///   after fuel: a trip keeps every gas it siphons (D33), and sells only its own;</item>
///   <item>otherwise it takes the best of its siphon targets (<see cref="SiphonPlanner"/>): a SCARCE or LIMITED gas no
///   siphoner works on first, the nearest gas giant first (D48), where a trip covers its gas only at the markets its ship
///   reaches in CRUISE from where it sells (D53); then the market shortest of a gas (D28), SCARCE, then LIMITED, and once
///   none is short, the lowest supply there is, but never a market that has the gas ABUNDANT (D77). One siphoner per sell
///   market and gas. There are no surveys: a siphon takes none. A market out of the siphoner's CRUISE reach counts after
///   the reachable ones of its supply level: the trip drifts there first (slice 6.10c, D45), and so a drone may be bought
///   for it;</item>
///   <item>a siphon drone whose every pair below ABUNDANT has a siphoner shares one, the lowest supply and then the fewest
///   siphoners first (D77, for siphon drones as for the miners): only with nothing below ABUNDANT left does the trading plan
///   give it a route. The command ship shares nothing: it takes what pays it most (D38);</item>
///   <item>it buys <c>SHIP_SIPHON_DRONE</c>s, one a pass, up to <c>Siphon.MaxDrones</c>, within the credit reserve and when
///   the order ships are bought in lets it (D43), at the shipyard that sells it for the least in a system where our ships
///   are: first a drone for each SCARCE or LIMITED gas a new drone could serve, once per area (D48, D53); then, when no
///   siphoner was free, a drone whose first trip, by the same ranking, would serve a market short of its gas (D22, D28,
///   D32), in turn with the cargo ships. Gas contracts stay unsupported (D2, D31), so no contract takes the drones.</item>
/// </list>
/// Its state lists the low-supply openings of those systems, with the siphoners that could take one
/// (<c>ShipLeftIdle</c> reads them, D13), and is written only when it changes.
/// </summary>
public sealed class SiphonAutomationService(
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipAssignmentRepository assignments,
    IShipyardRepository shipyards,
    ITradeContextReader tradeContexts,
    ISettingsRepository settings,
    IPlanRepository plans,
    IShipPurchaseService shipPurchases,
    IRoleAdvisor roles,
    IPurchaseOrder purchaseOrder,
    PassedOverShips passedOver,
    IAgentRepository agents,
    ILogger<SiphonAutomationService> logger) : ISiphonAutomationService
{
    /// <summary>The setting that holds the most siphon drones to keep (D32).</summary>
    public const string MaxDronesSetting = "Siphon.MaxDrones";

    /// <summary>The cap when the setting gives none (D32).</summary>
    internal const int DefaultMaxDrones = 10;

    private const string SiphonDroneShipType = "SHIP_SIPHON_DRONE";
    private const string LightShuttleShipType = "SHIP_LIGHT_SHUTTLE";
    private const string Collection = "collection";

    /// <summary>The minutes between two drones for one gas of the jump gate's chain while the setting holds none (D141).</summary>
    private const int DefaultGateSiphonIntervalMinutes = 30;

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var board = await FleetRoleBoard.ReadAsync(settings, plans, cancellationToken);
        var parkedMode = await settings.GetAsync<bool>(MiningAutomationService.ParkedDronesSetting, cancellationToken);

        // B71: the goals before the ships, so a trip that ends meanwhile leaves no siphoner free with the gas it sold.
        var read = await FleetGoals.ReadAsync(ships, goals, cancellationToken);
        var fleet = read.Fleet;
        var active = await assignments.GetAllActiveAsync(cancellationToken);
        var withAssignment = active
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Business stays home (D60, slice 6.28): its openings and drones are there only. The command ship, while it explores,
        // has an open assignment, so it is no free siphoner (asked on 2026-10-04).
        var systems = BusinessSystems.Of(await agents.GetAsync(cancellationToken));

        var heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heldBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // How many siphoners work on each pair: a drone that shares one takes the pair with the fewest (D77).
        var siphonersOn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // The siphoners' trips: a free siphoner takes a scarce gas that has none near its market first (D48, D53). D142: the
        // drones parked at a gas giant, and the shuttles' rounds there.
        var covered = new List<CoveringTrip>();
        var free = new List<ShipModel>();
        var parked = new List<(ShipModel Ship, MineForShuttleGoal Job)>();
        var rounds = new List<(ShipModel Ship, CollectOreGoal Round)>();
        var freeCollectors = new List<ShipModel>();
        foreach (var ship in fleet)
        {
            var goal = read.GoalOf(ship.Symbol);
            if (goal is SiphonAndSellGoal trip && trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                var key = MiningPlanner.OpportunityKey(trip.SellWaypointSymbol, trip.TradeSymbol);
                heldKeys.Add(key);
                heldBy[key] = ship.Symbol;
                siphonersOn[key] = siphonersOn.GetValueOrDefault(key) + 1;
                covered.Add(new CoveringTrip(trip.TradeSymbol, trip.SellWaypointSymbol, ship.FuelCapacity));
            }
            else if (goal is MineForShuttleGoal { Siphon: true } job && job.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                var key = MiningPlanner.OpportunityKey(job.SellWaypointSymbol, job.TradeSymbol);
                heldKeys.Add(key);
                heldBy[key] = ship.Symbol;
                siphonersOn[key] = siphonersOn.GetValueOrDefault(key) + 1;
                parked.Add((ship, job));
            }
            else if (goal is CollectOreGoal { Siphon: true } round && round.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                rounds.Add((ship, round));
            }
            else if (board.IsSiphoner(ship) && FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                free.Add(ship);
            }
            else if (parkedMode && board.IsCollector(ship) && FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                freeCollectors.Add(ship);
            }
        }

        // D142: the gas giants where siphon drones park, the shuttles designated for each, and the drones bought for the jump
        // gate's gases (D141), as the last pass left them.
        var existing = await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.SiphonAutomation, cancellationToken);
        var inFleet = fleet.Select(ship => ship.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var designated = (existing?.CollectionPoints ?? [])
            .GroupBy(point => MiningAutomationService.PointKey(point.SellWaypointSymbol, point.AsteroidWaypointSymbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(point => point.ShuttleSymbols).Where(inFleet.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var gateSiphoners = (existing?.GateMiners ?? []).ToList();
        var shipyardList = await shipyards.GetAllAsync(cancellationToken);
        var maps = new Dictionary<string, TradeMarketMap>(StringComparer.OrdinalIgnoreCase);
        var pointsBySystem = new Dictionary<string, IReadOnlyList<CollectionPoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var systemSymbol in systems)
        {
            var map = (await tradeContexts.ReadAsync(systemSymbol, cancellationToken)).Map;
            maps[systemSymbol] = map;
            pointsBySystem[systemSymbol] = parkedMode ? ParkedPointsIn(map, systemSymbol, fleet, shipyardList) : [];
        }

        if (parkedMode)
        {
            await MiningAutomationService.KeepParkedAtPointsAsync(goals, logger, "Siphon", parked, pointsBySystem, designated, existing?.CollectionPoints ?? [], inFleet, cancellationToken);
        }

        var places = parked
            .GroupBy(entry => MiningAutomationService.PointKey(entry.Job.SellWaypointSymbol, entry.Job.AsteroidWaypointSymbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(entry => (entry.Ship.Symbol, entry.Job.TradeSymbol)).ToList(), StringComparer.OrdinalIgnoreCase);

        // Before the first drone there is no siphoner: the openings of every system where our ships are show
        // what a drone would be bought for.
        var freeAtStart = free.Count > 0;
        var gaveTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var opportunities = new List<MiningAutomationOpportunityState>();
        foreach (var systemSymbol in systems)
        {
            var map = maps[systemSymbol];
            var points = pointsBySystem[systemSymbol];
            var candidates = free.Where(ship => string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase)).ToList();
            var withTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var siphoner in candidates)
            {
                // D142: a siphon drone parks at a gas giant, and the point's shuttle sells; the command ship takes trips (D38).
                if ((parkedMode && FleetRoles.IsSiphoner(siphoner) && points.Count > 0 && await ParkAsync(map, siphoner, points, places, gateSiphoners, heldKeys, heldBy, cancellationToken))
                    || await GiveTripAsync(map, siphoner, heldKeys, heldBy, siphonersOn, covered, cancellationToken))
                {
                    withTrip.Add(siphoner.Symbol);
                    gaveTrip.Add(siphoner.Symbol);
                }
            }

            // D142: a designated shuttle collects once a drone is parked at its gas giant.
            foreach (var collector in freeCollectors.Where(ship => string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase)))
            {
                if (points.FirstOrDefault(point => designated.GetValueOrDefault(point.Key, []).Contains(collector.Symbol, StringComparer.OrdinalIgnoreCase)) is { } point
                    && parked.Any(entry => MiningAutomationService.IsParkedAt(entry.Ship, point)))
                {
                    await StartRoundAsync(collector, point, cancellationToken);
                }
            }

            foreach (var opportunity in SiphonPlanner.LowSupplyOpportunities(map))
            {
                var held = heldBy.TryGetValue(opportunity.Key, out var holder);
                var able = candidates
                    .Where(siphoner => !withTrip.Contains(siphoner.Symbol)
                        && MiningPlanner.CanTake(map, siphoner, opportunity.GasGiantSymbol, opportunity.SellWaypointSymbol))
                    .Select(siphoner => siphoner.Symbol)
                    .Order(StringComparer.Ordinal)
                    .ToList();
                opportunities.Add(new MiningAutomationOpportunityState
                {
                    OpportunityKey = opportunity.Key,
                    TradeSymbol = opportunity.Gas,
                    SellWaypointSymbol = opportunity.SellWaypointSymbol,
                    SourceWaypointSymbol = opportunity.GasGiantSymbol,
                    Status = held ? MarketAutomationOpportunityStatus.Assigned : MarketAutomationOpportunityStatus.Pending,
                    AssignedShipSymbol = held ? holder : null,
                    CandidateShipSymbols = held ? [] : able,
                    FirstObservedAt = default,
                    LastObservedAt = default,
                });
            }
        }

        // B63: the trading plan, later in the tick, gives a route only to a siphoner this pass had no trip for.
        passedOver.Record(
            AutomationPlan.Siphon,
            fleet.Where(board.IsSiphoner).Select(ship => ship.Symbol),
            free.Where(ship => !gaveTrip.Contains(ship.Symbol)).Select(ship => ship.Symbol));

        var collecting = new SiphonCollecting(pointsBySystem, places, designated, parked, rounds, parkedMode, maps);
        await BuyDroneAsync(fleet, systems, board, heldKeys, covered, freeAtStart, gateSiphoners, collecting, shipyardList, cancellationToken);
        await SaveStateAsync(existing, opportunities, gateSiphoners, MiningAutomationService.CollectionPointStates(pointsBySystem, places, designated), cancellationToken);
    }

    /// <summary>
    /// Parks a siphon drone at a gas giant (D142): where it is, when that is a point; a drone bought for a gas of the jump
    /// gate's chain (D141) at a point that yields its gas; another at the nearest point that wants a drone, else where the
    /// fewest are. It
    /// siphons there for the point's shuttle (<see cref="MineForShuttleGoal.Siphon"/>), logging <c>SiphonStarted</c>, reason
    /// <c>collection</c>.
    /// </summary>
    /// <returns>True: it has its place.</returns>
    private async Task<bool> ParkAsync(
        TradeMarketMap map,
        ShipModel drone,
        IReadOnlyList<CollectionPoint> points,
        Dictionary<string, List<(string Ship, string Ore)>> places,
        IReadOnlyList<GateMinerState> gateSiphoners,
        HashSet<string> heldKeys,
        Dictionary<string, string> heldBy,
        CancellationToken cancellationToken)
    {
        var gateGas = gateSiphoners.FirstOrDefault(siphoner => siphoner.ShipSymbol.Equals(drone.Symbol, StringComparison.OrdinalIgnoreCase))?.TradeSymbol ?? string.Empty;
        int DronesAt(CollectionPoint point) => places.TryGetValue(point.Key, out var there) ? there.Count : 0;
        bool Yields(CollectionPoint point) => gateGas.Length > 0 && point.Ores.Contains(gateGas, StringComparer.OrdinalIgnoreCase);
        double Distance(CollectionPoint point) => map.TryGetDistance(MiningPlanner.Position(drone), point.AsteroidSymbol, out var distance) ? distance : double.MaxValue;
        var point = points.FirstOrDefault(candidate => string.Equals(drone.WaypointSymbol, candidate.AsteroidSymbol, StringComparison.OrdinalIgnoreCase))
            ?? points.Where(Yields).OrderBy(DronesAt).ThenBy(Distance).ThenBy(candidate => candidate.Key, StringComparer.Ordinal).FirstOrDefault()
            ?? points.Where(candidate => candidate.DronesWanted > DronesAt(candidate)).OrderBy(Distance).ThenBy(candidate => candidate.Key, StringComparer.Ordinal).FirstOrDefault()
            ?? points.OrderBy(DronesAt).ThenBy(Distance).ThenBy(candidate => candidate.Key, StringComparer.Ordinal).First();

        if (!places.TryGetValue(point.Key, out var placed))
        {
            placed = [];
            places[point.Key] = placed;
        }

        placed.RemoveAll(place => place.Ship.Equals(drone.Symbol, StringComparison.OrdinalIgnoreCase));
        var taken = placed.Select(place => place.Ore).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gas = Yields(point) ? gateGas
            : point.ScarceOres.FirstOrDefault(scarce => !taken.Contains(scarce)) ?? point.ScarceOres.FirstOrDefault() ?? point.Ores[0];
        placed.Add((drone.Symbol, gas));
        var key = MiningPlanner.OpportunityKey(point.SellWaypointSymbol, gas);
        heldKeys.Add(key);
        heldBy[key] = drone.Symbol;

        await goals.SetActiveGoalAsync(
            drone.Symbol,
            new MineForShuttleGoal { TradeSymbol = gas, AsteroidWaypointSymbol = point.AsteroidSymbol, SellWaypointSymbol = point.SellWaypointSymbol, Siphon = true },
            cancellationToken);
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} siphons at {WaypointSymbol} for {TradeSymbol} and sells it at {SellWaypoint} ({Reason}).",
            JournalEvents.SiphonStarted,
            drone.Symbol,
            point.AsteroidSymbol,
            gas,
            point.SellWaypointSymbol,
            Collection);
        return true;
    }

    /// <summary>Starts a designated shuttle's round of collecting at its gas giant (D142).</summary>
    private async Task StartRoundAsync(ShipModel shuttle, CollectionPoint point, CancellationToken cancellationToken)
    {
        await goals.SetActiveGoalAsync(
            shuttle.Symbol,
            new CollectOreGoal { AsteroidWaypointSymbol = point.AsteroidSymbol, SellWaypointSymbol = point.SellWaypointSymbol, Siphon = true },
            cancellationToken);
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} collects at {WaypointSymbol} and sells at {SellWaypoint} ({Reason}).",
            JournalEvents.CollectionStarted,
            shuttle.Symbol,
            point.AsteroidSymbol,
            point.SellWaypointSymbol,
            Collection);
    }

    /// <summary>
    /// The system's gas giants where siphon drones park (D142, <see cref="SiphonPlanner.ParkedPoints"/>), judged with the tanks
    /// of a siphon drone of the fleet (else as the cheapest shipyard sells one) and a light shuttle as the cheapest shipyard sells
    /// it; none while either isn't sold there. While the jump gate needs materials, only those whose gases go into them
    /// (<see cref="MiningAutomationService.GateSitesOnly"/>).
    /// </summary>
    private static IReadOnlyList<CollectionPoint> ParkedPointsIn(TradeMarketMap map, string systemSymbol, IReadOnlyList<ShipModel> fleet, IReadOnlyList<ShipyardWaypointDto> shipyardList)
    {
        var drone = MiningAutomationService.CheapestListing(shipyardList, systemSymbol, SiphonDroneShipType);
        var shuttle = MiningAutomationService.CheapestListing(shipyardList, systemSymbol, LightShuttleShipType);
        if (drone is null || shuttle is null)
        {
            return [];
        }

        var tank = fleet.FirstOrDefault(FleetRoles.IsSiphoner)?.FuelCapacity ?? drone.Value.Ship.FuelCapacity;
        var points = SiphonPlanner.ParkedPoints(
            map,
            new ShipModel("NEW-DRONE", systemSymbol, drone.Value.Shipyard.WaypointSymbol, "DOCKED", "CRUISE", tank, tank, CargoCapacity: drone.Value.Ship.CargoCapacity, ShipType: SiphonDroneShipType),
            new ShipModel("NEW-SHUTTLE", systemSymbol, shuttle.Value.Shipyard.WaypointSymbol, "DOCKED", "CRUISE", shuttle.Value.Ship.FuelCapacity, shuttle.Value.Ship.FuelCapacity, CargoCapacity: shuttle.Value.Ship.CargoCapacity, ShipType: LightShuttleShipType));
        return MiningAutomationService.GateSitesOnly(map, points);
    }

    /// <summary>
    /// Gives a free siphoner its next trip: selling goods it holds, else the best siphon target, a scarce gas no siphoner
    /// works on near its market first (D48, D53); the reason says <c>uncovered</c> when that came before D28's choice. A
    /// full hold only sells, even where the sale doesn't pay for its fuel: a siphon trip would turn to selling at once, and
    /// end without its gas aboard, on every tick. A siphon drone whose every pair below ABUNDANT has a siphoner shares one
    /// (D77, reason <c>shared</c>): it is passed over to the trading plan only when nothing below ABUNDANT is left that it
    /// can siphon and sell.
    /// </summary>
    /// <returns>False when there is nothing it can siphon and sell.</returns>
    private async Task<bool> GiveTripAsync(
        TradeMarketMap map,
        ShipModel siphoner,
        HashSet<string> heldKeys,
        Dictionary<string, string> heldBy,
        Dictionary<string, int> siphonersOn,
        List<CoveringTrip> covered,
        CancellationToken cancellationToken)
    {
        var holdIsFull = siphoner.CargoCapacity > 0 && siphoner.CargoCurrent >= siphoner.CargoCapacity;
        if (TryFindHeldCargoSale(map, siphoner, holdIsFull, out var cargo, out var sale))
        {
            covered.Add(new CoveringTrip(cargo.Symbol, sale.WaypointSymbol, siphoner.FuelCapacity));
            await StartAsync(siphoner, new SiphonAndSellGoal
            {
                TradeSymbol = cargo.Symbol,
                SourceWaypointSymbol = siphoner.WaypointSymbol ?? string.Empty,
                SellWaypointSymbol = sale.WaypointSymbol,
                Selling = true,
            }, "held_cargo", cancellationToken);
            return true;
        }

        if (holdIsFull)
        {
            logger.LogDebug("Siphon plan: ship {ShipSymbol} has a full hold that no market it can reach buys.", siphoner.Symbol);
            return false;
        }

        // D91: with no pair left whose market makes something from its gas, a siphon drone shares one before it siphons for a
        // market that only pays for the gas.
        var ranked = SiphonPlanner.SiphonTargets(map, siphoner, heldKeys);
        if (ranked.Count == 0 || !ranked[0].FeedsProduction)
        {
            // D77, for siphon drones too: they siphon until every gas is ABUNDANT. The command ship keeps D38: it takes what
            // pays it most, so it shares nothing.
            if (FleetRoles.IsSiphoner(siphoner)
                && SiphonPlanner.SharedTargets(map, siphoner, siphonersOn).FirstOrDefault() is { } shared
                && (shared.FeedsProduction || ranked.Count == 0))
            {
                siphonersOn[shared.Key] = siphonersOn.GetValueOrDefault(shared.Key) + 1;
                covered.Add(new CoveringTrip(shared.Gas, shared.SellWaypointSymbol, siphoner.FuelCapacity));
                await StartAsync(siphoner, TripTo(shared), "shared", cancellationToken);
                return true;
            }

            if (ranked.Count == 0)
            {
                logger.LogDebug("Siphon plan: nothing to siphon that ship {ShipSymbol} can reach and sell.", siphoner.Symbol);
                return false;
            }
        }

        var target = SiphonPlanner.UncoveredFirst(map, siphoner, ranked, covered)[0];
        var reason = !target.FeedsProduction ? "wealth" : target != ranked[0] ? "uncovered" : target.LowSupply ? "low_supply" : "lowest_supply";
        heldKeys.Add(target.Key);
        heldBy[target.Key] = siphoner.Symbol;
        siphonersOn[target.Key] = siphonersOn.GetValueOrDefault(target.Key) + 1;
        covered.Add(new CoveringTrip(target.Gas, target.SellWaypointSymbol, siphoner.FuelCapacity));
        await StartAsync(siphoner, TripTo(target), reason, cancellationToken);
        return true;
    }

    /// <summary>A trip that siphons a target's gas at its gas giant and sells it at its market, drifting there first when it is far (D45).</summary>
    private static SiphonAndSellGoal TripTo(SiphonTarget target)
        => new()
        {
            TradeSymbol = target.Gas,
            SourceWaypointSymbol = target.GasGiantSymbol,
            SellWaypointSymbol = target.SellWaypointSymbol,
            Drifting = target.Far,
        };

    private async Task StartAsync(ShipModel siphoner, SiphonAndSellGoal trip, string reason, CancellationToken cancellationToken)
    {
        await goals.SetActiveGoalAsync(siphoner.Symbol, trip, cancellationToken);
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} siphons at {WaypointSymbol} for {TradeSymbol} and sells it at {SellWaypoint} ({Reason}).",
            JournalEvents.SiphonStarted,
            siphoner.Symbol,
            trip.SourceWaypointSymbol,
            trip.TradeSymbol,
            trip.SellWaypointSymbol,
            reason);
    }

    /// <summary>
    /// For a siphoner that holds goods: the good that fetches most where it sells best, after the fuel to get
    /// there, when that is anything at all, or whatever it is when the hold must be emptied. A trip keeps every gas
    /// it siphons (D33), so this sells the gases its trip didn't, one a trip, each at a market that makes something from it
    /// where such a sale pays (D91).
    /// </summary>
    private static bool TryFindHeldCargoSale(TradeMarketMap map, ShipModel siphoner, bool mustSell, out CargoItemModel cargo, out TradeSale sale)
    {
        cargo = new CargoItemModel(string.Empty, 0);
        sale = new TradeSale(string.Empty, 0, 0, 0);
        var found = false;
        foreach (var item in (siphoner.CargoInventory ?? []).Where(item => item.Units > 0))
        {
            if (TradeRoutePlanner.TryFindBestSale(map, siphoner, item.Symbol, item.Units, out var candidate, supplyFirst: true)
                && (candidate.NetRevenue > 0 || mustSell)
                && (!found || candidate.NetRevenue > sale.NetRevenue))
            {
                cargo = item;
                sale = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Says what the plan would buy (<see cref="DroneNeedAsync"/>), and buys it when the order ships are bought in lets it
    /// (D43, <see cref="IPurchaseOrder"/>), within the credit reserve. One a pass: the next pass counts the new drone. A drone
    /// bought for the jump gate's gases is noted with its gas (D141): the next for the gas waits
    /// <c>Mining.GateMinerIntervalMinutes</c>, as the gate's miners do.
    /// </summary>
    private async Task BuyDroneAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyList<string> systems,
        FleetRoleBoard board,
        IReadOnlySet<string> heldKeys,
        IReadOnlyCollection<CoveringTrip> covered,
        bool freeAtStart,
        List<GateMinerState> gateSiphoners,
        SiphonCollecting collecting,
        IReadOnlyList<ShipyardWaypointDto> shipyardList,
        CancellationToken cancellationToken)
    {
        // D142: the shuttles come first: a drone parked at a gas giant sells nothing without one.
        var (need, shuttlePoint) = ShuttleNeed(systems, collecting, shipyardList);
        var gateGas = string.Empty;
        if (need.Tier == PurchaseTier.None)
        {
            (need, gateGas) = await DroneNeedAsync(fleet, systems, board, heldKeys, covered, freeAtStart, gateSiphoners, collecting, cancellationToken);
        }

        if (!await purchaseOrder.ReportAsync(AutomationPlan.Siphon, need, cancellationToken))
        {
            return;
        }

        var purchased = await shipPurchases.TryPurchaseAsync(need.ShipType, need.ShipyardWaypointSymbol, cancellationToken);
        if (!purchased.IsSuccess)
        {
            logger.LogDebug(
                "Siphon plan: siphon drone purchase denied at {Shipyard} — {Reason}.",
                need.ShipyardWaypointSymbol,
                purchased.FailureReason ?? "Purchase failed.");
            return;
        }

        if (shuttlePoint.Length > 0 && purchased.PurchasedShip is { } shuttle)
        {
            if (!collecting.Designated.TryGetValue(shuttlePoint, out var shuttles))
            {
                shuttles = [];
                collecting.Designated[shuttlePoint] = shuttles;
            }

            shuttles.Add(shuttle.Symbol);
            logger.LogInformation(
                "Siphon plan: ship {ShipSymbol} was bought to collect what the drones parked at {PointKey} siphon (D142).",
                shuttle.Symbol,
                shuttlePoint);
        }

        if (gateGas.Length > 0 && purchased.PurchasedShip is { } drone)
        {
            gateSiphoners.Add(new GateMinerState { ShipSymbol = drone.Symbol, TradeSymbol = gateGas, BoughtAt = TimeProvider.System.GetUtcNow() });
            logger.LogInformation(
                "Siphon plan: ship {ShipSymbol} was bought for {TradeSymbol}, which the jump gate's chain is short of (D141).",
                drone.Symbol,
                gateGas);
        }
    }

    /// <summary>
    /// The siphon drone the plan would buy, at the shipyard of a system where our ships are that sells it for the least,
    /// up to <c>Siphon.MaxDrones</c> (D32):
    /// <list type="bullet">
    ///   <item>a drone for a scarce gas (<see cref="PurchaseTier.Coverage"/>, D48), while the system has fewer siphon drones
    ///   than SCARCE or LIMITED gases a new drone could serve, each counted once per area (<see cref="SiphonPlanner.ScarceGases"/>,
    ///   D53): one drone per scarce mineral and area, which the role board keeps siphoning. It doesn't ask the board what
    ///   pays most;</item>
    ///   <item>otherwise, when every siphoner works, a drone whose first trip by the siphoners' own ranking (the trips under
    ///   way held) would serve a market short of its gas (<see cref="PurchaseTier.Alternating"/>, D22, D28, D32), which,
    ///   with the role board on, the board would have siphon (<see cref="IRoleAdvisor"/>).</item>
    /// </list>
    /// While the home jump gate needs materials (slice 6.45, D130: "Gate chain only, until done"), only a drone for a gas its
    /// chain is short of (slice 6.52, D141, "Like the gate's miners"): a gas a market making a good of the chain from it has
    /// below HIGH (<see cref="SiphonPlanner.GateGasesShort"/>), one per gas every <c>Mining.GateMinerIntervalMinutes</c>, at the
    /// gate's place in the order (<see cref="PurchaseTier.Construction"/>), after a load the markets sell now, as the gate's
    /// miners (D92). D130 read that no gas goes into the gate's materials; in X1-VP96 on 2026-11-08 LIQUID_HYDROGEN and
    /// LIQUID_NITROGEN made the EXPLOSIVES the asteroid base's ores are made from.
    /// </summary>
    /// <returns>The need, and the gas of the gate's chain it is for; empty for any other.</returns>
    private async Task<(PurchaseNeed Need, string GateGas)> DroneNeedAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyList<string> systems,
        FleetRoleBoard board,
        IReadOnlySet<string> heldKeys,
        IReadOnlyCollection<CoveringTrip> covered,
        bool freeAtStart,
        IReadOnlyList<GateMinerState> gateSiphoners,
        SiphonCollecting collecting,
        CancellationToken cancellationToken)
    {
        var maxDrones = await settings.ThresholdAsync(MaxDronesSetting, DefaultMaxDrones, cancellationToken);
        var drones = fleet.Count(FleetRoles.IsSiphoner);
        if (drones >= maxDrones)
        {
            logger.LogDebug("Siphon plan: siphon drone cap reached ({Current}/{Max}); purchase skipped.", drones, maxDrones);
            return (PurchaseNeed.None, string.Empty);
        }

        var interval = await GateSiphonIntervalAsync(cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        var shipyardList = await shipyards.GetAllAsync(cancellationToken);
        foreach (var systemSymbol in systems)
        {
            // D121: never where the shipyard has it SCARCE.
            var listing = shipyardList
                .Where(candidate => candidate.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))
                .SelectMany(candidate => candidate.Ships
                    .Where(ship => ship.Type.Equals(SiphonDroneShipType, StringComparison.OrdinalIgnoreCase) && ship.PurchasePrice > 0 && !ScarceShips.IsScarce(ship))
                    .Select(ship => (Shipyard: candidate, Ship: ship)))
                .OrderBy(candidate => candidate.Ship.PurchasePrice)
                .ThenBy(candidate => candidate.Shipyard.WaypointSymbol, StringComparer.Ordinal)
                .FirstOrDefault();
            if (listing.Shipyard is null)
            {
                logger.LogDebug("Siphon plan: no shipyard in {SystemSymbol} with a known price for {ShipType} that isn't SCARCE.", systemSymbol, SiphonDroneShipType);
                continue;
            }

            var (shipyard, forSale) = listing;
            var map = (await tradeContexts.ReadAsync(systemSymbol, cancellationToken)).Map;
            var newDrone = new ShipModel(
                "NEW-DRONE",
                systemSymbol,
                shipyard.WaypointSymbol,
                "DOCKED",
                "CRUISE",
                forSale.FuelCapacity,
                forSale.FuelCapacity,
                CargoCapacity: forSale.CargoCapacity,
                ShipType: SiphonDroneShipType);
            var need = new PurchaseNeed(PurchaseTier.Coverage, SiphonDroneShipType, shipyard.WaypointSymbol, forSale.PurchasePrice);

            // D130, D141: while the jump gate needs materials, only a drone for a gas its chain is short of, one per gas every
            // interval, at the gate's place in the order.
            var points = collecting.PointsBySystem.GetValueOrDefault(systemSymbol, []);
            if (map.ConstructionMaterials.Count > 0)
            {
                // D142: with the drones parked, a gas is served where a point yields it: its shuttle takes it to the market.
                var shortGases = collecting.ParkedMode ? SiphonPlanner.GateGasesShortAtPoints(map, points) : SiphonPlanner.GateGasesShort(map, newDrone);
                var gas = shortGases
                    .FirstOrDefault(shortGas => !gateSiphoners.Any(siphoner => siphoner.TradeSymbol.Equals(shortGas, StringComparison.OrdinalIgnoreCase)
                        && now - siphoner.BoughtAt < interval));
                if (gas is not null)
                {
                    return (need with { Tier = PurchaseTier.Construction }, gas);
                }

                logger.LogDebug(
                    "Siphon plan: no siphon drone bought in {SystemSymbol} while the jump gate needs materials: no gas of its chain is short there, or one was bought for it lately (D130, D141).",
                    systemSymbol);
                continue;
            }

            var siphonDrones = fleet.Count(ship => FleetRoles.IsSiphoner(ship) && string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase));
            if (siphonDrones < (collecting.ParkedMode ? points.Sum(point => point.DronesWanted) : SiphonPlanner.ScarceGases(map, newDrone).Count))
            {
                return (need, string.Empty);
            }

            if (freeAtStart)
            {
                continue;
            }

            var targets = SiphonPlanner.SiphonTargets(map, newDrone, heldKeys, covered);
            if (targets.Count == 0 || !targets[0].FeedsProduction || !targets[0].LowSupply)
            {
                logger.LogDebug(
                    "Siphon plan: no drone bought in {SystemSymbol}: its first trip would not serve a market short of its gas ({Trip}).",
                    systemSymbol,
                    targets.Count == 0 ? "nothing it can reach" : $"{targets[0].Gas} for {targets[0].SellWaypointSymbol}, {targets[0].Supply}");
                continue;
            }

            // With the role board on (slice 6.9), a drone that would earn more trading would trade, and the next pass would
            // buy another for the same opening.
            if (board.RolesOn && !await roles.WouldTakeAsync(newDrone, FleetRole.Siphon, cancellationToken))
            {
                logger.LogDebug(
                    "Siphon plan: no drone bought in {SystemSymbol}: the role board would have it trade, which would pay it more.",
                    systemSymbol);
                continue;
            }

            return (need with { Tier = PurchaseTier.Alternating }, string.Empty);
        }

        return (PurchaseNeed.None, string.Empty);
    }

    /// <summary>
    /// A light shuttle for a gas giant where siphon drones park (D142, <see cref="MiningAutomationService.ShuttleNeedAt"/>): at
    /// the gate's place in the order while the jump gate needs materials, as its miners' shuttles; with the drones for scarce
    /// gases (<see cref="PurchaseTier.Coverage"/>) once it is done.
    /// </summary>
    private (PurchaseNeed Need, string PointKey) ShuttleNeed(IReadOnlyList<string> systems, SiphonCollecting collecting, IReadOnlyList<ShipyardWaypointDto> shipyardList)
    {
        if (!collecting.ParkedMode)
        {
            return (PurchaseNeed.None, string.Empty);
        }

        foreach (var systemSymbol in systems)
        {
            var points = collecting.PointsBySystem.GetValueOrDefault(systemSymbol, []);
            if (points.Count == 0 || MiningAutomationService.CheapestListing(shipyardList, systemSymbol, LightShuttleShipType) is not { } listing)
            {
                continue;
            }

            var map = collecting.MapsBySystem[systemSymbol];
            var tier = map.ConstructionMaterials.Count > 0 ? PurchaseTier.Construction : PurchaseTier.Coverage;
            var (need, pointKey) = MiningAutomationService.ShuttleNeedAt(
                points,
                collecting.Designated,
                collecting.Places,
                collecting.Parked,
                collecting.Rounds,
                new PurchaseNeed(tier, LightShuttleShipType, listing.Shipyard.WaypointSymbol, listing.Ship.PurchasePrice),
                map);
            if (need.Tier != PurchaseTier.None)
            {
                logger.LogDebug("Siphon plan: a {ShipType} for the drones parked at {PointKey} (D142).", LightShuttleShipType, pointKey);
                return (need, pointKey);
            }
        }

        return (PurchaseNeed.None, string.Empty);
    }

    /// <summary>
    /// The time between two drones for one gas of the jump gate's chain (D141), as between two gate miners for one ore (D92):
    /// <c>Mining.GateMinerIntervalMinutes</c>, else half an hour.
    /// </summary>
    private async Task<TimeSpan> GateSiphonIntervalAsync(CancellationToken cancellationToken)
    {
        var minutes = await settings.GetAsync<int>(MiningAutomationService.GateMinerIntervalSetting, cancellationToken);
        return TimeSpan.FromMinutes(minutes > 0 ? minutes : DefaultGateSiphonIntervalMinutes);
    }

    /// <summary>
    /// Records the openings, and the drones bought for the jump gate's gases (D141), those of the last day. Only a change is
    /// written: the tick runs every 5 seconds.
    /// </summary>
    private async Task SaveStateAsync(
        MiningAutomationPlanState? existing,
        IReadOnlyList<MiningAutomationOpportunityState> opportunities,
        IReadOnlyList<GateMinerState> gateSiphoners,
        IReadOnlyList<CollectionPointState> collectionPoints,
        CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        List<GateMinerState> recentGateSiphoners = [.. gateSiphoners.Where(siphoner => now - siphoner.BoughtAt < TimeSpan.FromDays(1))];
        var firstSeen = (existing?.Opportunities ?? [])
            .GroupBy(opportunity => opportunity.OpportunityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().FirstObservedAt, StringComparer.OrdinalIgnoreCase);
        List<MiningAutomationOpportunityState> dated =
        [
            .. opportunities
                .OrderBy(opportunity => opportunity.SellWaypointSymbol, StringComparer.Ordinal)
                .ThenBy(opportunity => opportunity.TradeSymbol, StringComparer.Ordinal)
                .Select(opportunity => opportunity with
                {
                    FirstObservedAt = firstSeen.GetValueOrDefault(opportunity.OpportunityKey, now),
                    LastObservedAt = now,
                }),
        ];

        if (existing is not null
            && Same(existing.Opportunities, dated)
            && JsonSerializer.Serialize(existing.GateMiners, CompareOptions) == JsonSerializer.Serialize(recentGateSiphoners, CompareOptions)
            && JsonSerializer.Serialize(existing.CollectionPoints, CompareOptions) == JsonSerializer.Serialize(collectionPoints, CompareOptions))
        {
            return;
        }

        await plans.UpsertAsync(
            PlanTypes.SiphonAutomation,
            new MiningAutomationPlanState
            {
                PlanId = existing?.PlanId ?? Guid.NewGuid(),
                Opportunities = dated,
                GateMiners = recentGateSiphoners,
                CollectionPoints = collectionPoints,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now,
            },
            cancellationToken);
    }

    /// <summary>Whether two lists of openings say the same, apart from when they were seen.</summary>
    private static bool Same(IReadOnlyList<MiningAutomationOpportunityState> before, IReadOnlyList<MiningAutomationOpportunityState> after)
        => JsonSerializer.Serialize(before.Select(Undated), CompareOptions) == JsonSerializer.Serialize(after.Select(Undated), CompareOptions);

    private static MiningAutomationOpportunityState Undated(MiningAutomationOpportunityState opportunity)
        => opportunity with { FirstObservedAt = default, LastObservedAt = default };

    /// <summary>
    /// What the plan knows of its gas giants where drones park at a pass (D142): the points of each system, the drones with a
    /// place at each, the shuttles designated for each, the parked drones' and the shuttles' goals, whether drones park, and the
    /// maps read this pass.
    /// </summary>
    private sealed record SiphonCollecting(
        IReadOnlyDictionary<string, IReadOnlyList<CollectionPoint>> PointsBySystem,
        IReadOnlyDictionary<string, List<(string Ship, string Ore)>> Places,
        Dictionary<string, List<string>> Designated,
        IReadOnlyList<(ShipModel Ship, MineForShuttleGoal Job)> Parked,
        IReadOnlyList<(ShipModel Ship, CollectOreGoal Round)> Rounds,
        bool ParkedMode,
        IReadOnlyDictionary<string, TradeMarketMap> MapsBySystem);
}
