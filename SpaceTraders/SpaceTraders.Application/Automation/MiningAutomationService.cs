using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>The mining plan (PLAN.md slice 6.4).</summary>
public interface IMiningAutomationService
{
    /// <summary>One pass of the plan: gives every free miner a trip, and buys a drone for a scarce ore or a market short of one.</summary>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The mining plan (PLAN.md slice 6.4). Each tick it gives every free miner one trip
/// (<see cref="MineAndSellGoal"/>); the contract plan has taken the miners it wants first (D23):
/// <list type="bullet">
///   <item>a miner is a ship with a mining laser, a hold and a tank that doesn't survey (D20,
///   <see cref="FleetRoles"/>);</item>
///   <item>a miner that holds ore a market buys sells it first, one good a trip, where it fetches most after fuel: the
///   ore left over from a contract, and the other ores a trip keeps (D71); a full hold sells even where that doesn't pay
///   for the fuel;</item>
///   <item>otherwise it takes the best of its mining targets (<see cref="MiningPlanner"/>): a SCARCE or LIMITED ore no
///   miner works on first, the nearest asteroid first (D48), where a trip covers its ore only at the markets its ship
///   reaches in CRUISE from where it sells (D53); then the market shortest of an ore (D28), SCARCE, then LIMITED, and
///   once none is short, the lowest supply there is, but never a market that has the ore ABUNDANT (D77); within a supply
///   level, a surveyed ore first. One miner per sell market and ore. A market out of the miner's CRUISE reach counts
///   after the reachable ones of its supply level: the trip drifts there first (slice 6.10c, D45), and so a drone may be
///   bought for it;</item>
///   <item>a drone whose every pair below ABUNDANT has a miner shares one, the lowest supply and then the fewest miners
///   first (D77): "I'd like the miners to only mine, even if there is more profit in trading. They can mine until every
///   mineral is ABUNDANT." Only with nothing below ABUNDANT left does the trading plan give it a route. The command ship
///   shares nothing: it takes what pays it most (D38);</item>
///   <item>it buys mining drones, one a pass, up to <c>Mining.MaxDrones</c>, within the credit reserve and when the order
///   ships are bought in lets it (D43); not while the contract plan mines, which would take the drone (D23). First a
///   drone for each SCARCE or LIMITED ore a new drone could serve, once per area (D48, D53); then, when no miner was
///   free, a drone whose first trip, by the same ranking, would serve a market short of its ore (D22, D28), in turn with
///   the cargo ships. A drone that would mine for a market that isn't short is not bought: the opening that paid for it
///   would stay open and pay for the next.</item>
///   <item>far asteroids no drone mines on a CRUISE round trip of the market that buys their ores (slice 6.18, D83,
///   <see cref="MiningPlanner.CollectionPoints"/>): a mining drone with no uncovered ore to serve takes a place at one that
///   wants more drones, one per SCARCE or LIMITED ore (D48), drifting to its market first when that is out of its CRUISE
///   reach (D45), and stays parked there (<see cref="MineForShuttleGoal"/>); the shuttle designated for it collects their
///   ore (<see cref="CollectOreGoal"/>) once a drone is parked there. With the drones for scarce minerals (Coverage) it buys
///   a light shuttle for a point where a drone has a place and none is designated yet, and a second when a parked drone
///   waits for one, its hold full, while the first is away selling;</item>
///   <item>while the jump gate needs materials (slice 6.25, D92), a mining drone for each ore its smelters are short of
///   (<see cref="MiningPlanner.GateSmelters"/>: below HIGH, at a smelter a drone from the shipyard can serve), one per ore
///   every <c>Mining.GateMinerIntervalMinutes</c> (30), at the gate's place in the order ships are bought in, after a load
///   that can be bought now. Such a gate miner mines only its ore, for the smelter of it with the lowest supply, and never
///   parks at a collection point, until the gate needs nothing made from its ore; while every smelter of it has the ore
///   ABUNDANT, or is out of its reach, it follows the rules above. Asked on 2026-10-05: "extra miners to be bought for the
///   ores that supply the build gate materials once every half hour (and those miners being dedicated to those ores) until
///   each of the smelters have at least HIGH saturation."</item>
/// </list>
/// Its state lists the low-supply openings, with the miners that could take one (<c>ShipLeftIdle</c>
/// reads them, D13), and is written only when it changes.
/// </summary>
public sealed class MiningAutomationService(
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipAssignmentRepository assignments,
    IContractMineralPlanRepository contractPlans,
    IShipyardRepository shipyards,
    IMiningContextReader miningContexts,
    ISettingsRepository settings,
    IPlanRepository plans,
    IShipPurchaseService shipPurchases,
    IRoleAdvisor roles,
    IPurchaseOrder purchaseOrder,
    PassedOverShips passedOver,
    IAgentRepository agents,
    ILogger<MiningAutomationService> logger) : IMiningAutomationService
{
    /// <summary>
    /// The setting that holds the minutes between the drones bought for one ore of the jump gate's smelters (slice 6.25, D92):
    /// "once every half hour".
    /// </summary>
    public const string GateMinerIntervalSetting = "Mining.GateMinerIntervalMinutes";

    private const string MiningDroneShipType = "SHIP_MINING_DRONE";
    private const string LightShuttleShipType = "SHIP_LIGHT_SHUTTLE";
    private const string Collection = "collection";
    private const string Gate = "gate";

    /// <summary>The minutes between two gate miners for one ore while the setting holds none: half an hour (D92).</summary>
    private const int DefaultGateMinerIntervalMinutes = 30;

    /// <summary>The most shuttles a collection point gets: the first, and a second when drones wait for one (D83).</summary>
    private const int MaxShuttlesPerPoint = 2;
    private const string MaxMiningDronesSettingKey = "Mining.MaxDrones";
    private const int DefaultMaxMiningDrones = 20;

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var board = await FleetRoleBoard.ReadAsync(settings, plans, cancellationToken);

        // B71: the goals before the ships, so a trip that ends meanwhile leaves no miner free with the ore it sold.
        var read = await FleetGoals.ReadAsync(ships, goals, cancellationToken);
        var fleet = read.Fleet;
        var active = await assignments.GetAllActiveAsync(cancellationToken);
        var withAssignment = active
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Business stays home (D60, slice 6.28): drones are bought there only; the command ship works for no plan while it
        // explores (asked on 2026-10-04).
        var explorers = BusinessSystems.Explorers(active);
        var systems = BusinessSystems.Of(await agents.GetAsync(cancellationToken));

        // D83: the shuttles designated for each collection point, by point, as the last pass left them.
        var existing = await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, cancellationToken);
        var inFleet = fleet.Select(ship => ship.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var designated = (existing?.CollectionPoints ?? [])
            .GroupBy(point => PointKey(point.SellWaypointSymbol, point.AsteroidWaypointSymbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(point => point.ShuttleSymbols).Where(inFleet.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        // D92: the drones bought for the jump gate's smelters, as the last pass left them.
        var gateMiners = (existing?.GateMiners ?? [])
            .Where(miner => inFleet.Contains(miner.ShipSymbol))
            .DistinctBy(miner => miner.ShipSymbol, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var shipyardList = await shipyards.GetAllAsync(cancellationToken);

        var heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heldBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // How many miners work on each pair: a drone that shares one takes the pair with the fewest (D77).
        var minersOn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // The miners' trips: a free miner takes a scarce ore that has none near its market first (D48, D53).
        var covered = new List<CoveringTrip>();
        var free = new List<ShipModel>();

        // D83: the drones with a place at a collection point, parked or on their way, and the shuttles' rounds.
        var parked = new List<(ShipModel Ship, MineForShuttleGoal Job)>();
        var rounds = new List<(ShipModel Ship, CollectOreGoal Round)>();
        var freeCollectors = new List<ShipModel>();
        foreach (var ship in fleet)
        {
            var goal = read.GoalOf(ship.Symbol);
            if (goal is MineAndSellGoal trip && trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                var key = MiningPlanner.OpportunityKey(trip.SellWaypointSymbol, trip.TradeSymbol);
                heldKeys.Add(key);
                heldBy[key] = ship.Symbol;
                minersOn[key] = minersOn.GetValueOrDefault(key) + 1;
                covered.Add(new CoveringTrip(trip.TradeSymbol, trip.SellWaypointSymbol, ship.FuelCapacity));
            }
            else if (goal is MineForShuttleGoal job && job.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                var key = MiningPlanner.OpportunityKey(job.SellWaypointSymbol, job.TradeSymbol);
                heldKeys.Add(key);
                heldBy[key] = ship.Symbol;
                minersOn[key] = minersOn.GetValueOrDefault(key) + 1;
                covered.Add(new CoveringTrip(job.TradeSymbol, job.SellWaypointSymbol, ship.FuelCapacity));
                parked.Add((ship, job));
            }
            else if (goal is CollectOreGoal round && round.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                rounds.Add((ship, round));
            }
            else if (board.IsMiner(ship) && FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                free.Add(ship);
            }
            else if (board.IsCollector(ship) && FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                freeCollectors.Add(ship);
            }
        }

        // Each point's drones with a place there, and the ore each mines for.
        var places = parked
            .GroupBy(entry => PointKey(entry.Job.SellWaypointSymbol, entry.Job.AsteroidWaypointSymbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(entry => (entry.Ship.Symbol, entry.Job.TradeSymbol)).ToList(), StringComparer.OrdinalIgnoreCase);
        var pointsBySystem = new Dictionary<string, IReadOnlyList<CollectionPoint>>(StringComparer.OrdinalIgnoreCase);

        var freeAtStart = free.Count > 0;
        var gaveTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var opportunities = new List<MiningAutomationOpportunityState>();
        foreach (var system in fleet
            .Where(ship => board.IsMiner(ship) && !explorers.Contains(ship.Symbol) && !string.IsNullOrWhiteSpace(ship.SystemSymbol))
            .GroupBy(ship => ship.SystemSymbol!, StringComparer.OrdinalIgnoreCase))
        {
            var context = await miningContexts.ReadAsync(system.Key, cancellationToken);
            var points = CollectionPointsIn(context, system.Key, fleet, shipyardList);
            pointsBySystem[system.Key] = points;
            var dedicated = DedicatedIn(context, gateMiners);
            var candidates = free.Where(ship => string.Equals(ship.SystemSymbol, system.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            var withTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var miner in candidates)
            {
                var gateOre = dedicated.GetValueOrDefault(miner.Symbol, string.Empty);
                if (await GiveTripAsync(context, miner, gateOre, heldKeys, heldBy, minersOn, covered, points, places, cancellationToken))
                {
                    withTrip.Add(miner.Symbol);
                    gaveTrip.Add(miner.Symbol);
                }
            }

            // D83: a designated shuttle collects once a drone is parked at its point.
            foreach (var collector in freeCollectors.Where(ship => string.Equals(ship.SystemSymbol, system.Key, StringComparison.OrdinalIgnoreCase)))
            {
                if (points.FirstOrDefault(point => designated.GetValueOrDefault(point.Key, []).Contains(collector.Symbol, StringComparer.OrdinalIgnoreCase)) is { } point
                    && parked.Any(entry => IsParkedAt(entry.Ship, point)))
                {
                    await StartRoundAsync(collector, point, cancellationToken);
                }
            }

            foreach (var opportunity in MiningPlanner.LowSupplyOpportunities(context.Map))
            {
                var held = heldBy.TryGetValue(opportunity.Key, out var holder);
                var able = candidates
                    .Where(miner => !withTrip.Contains(miner.Symbol)
                        && MiningPlanner.CanTake(context.Map, miner, opportunity.AsteroidSymbol, opportunity.SellWaypointSymbol))
                    .Select(miner => miner.Symbol)
                    .Order(StringComparer.Ordinal)
                    .ToList();
                opportunities.Add(new MiningAutomationOpportunityState
                {
                    OpportunityKey = opportunity.Key,
                    TradeSymbol = opportunity.Ore,
                    SellWaypointSymbol = opportunity.SellWaypointSymbol,
                    SourceWaypointSymbol = opportunity.AsteroidSymbol,
                    Status = held ? MarketAutomationOpportunityStatus.Assigned : MarketAutomationOpportunityStatus.Pending,
                    AssignedShipSymbol = held ? holder : null,
                    CandidateShipSymbols = held ? [] : able,
                    FirstObservedAt = default,
                    LastObservedAt = default,
                });
            }
        }

        // B63: the trading plan, later in the tick, gives a route only to a miner this pass had no trip for.
        passedOver.Record(
            AutomationPlan.Mining,
            fleet.Where(board.IsMiner).Select(ship => ship.Symbol),
            free.Where(ship => !gaveTrip.Contains(ship.Symbol)).Select(ship => ship.Symbol));

        await BuyAsync(fleet, systems, board, heldKeys, covered, freeAtStart, new Collecting(pointsBySystem, places, designated, parked, rounds), gateMiners, shipyardList, cancellationToken);
        await SaveStateAsync(existing, opportunities, CollectionPointStates(pointsBySystem, places, designated), gateMiners, cancellationToken);
    }

    /// <summary>
    /// The gate miners that are dedicated now, with their ores (D92): those whose ore a smelter of the system makes a metal
    /// from that goes into a material the jump gate still needs (<see cref="MiningPlanner.GateSmelters"/>). Once the gate needs
    /// nothing made from an ore, or the construction plan is off, its miners are ordinary drones.
    /// </summary>
    private static Dictionary<string, string> DedicatedIn(MiningContext context, IEnumerable<GateMinerState> gateMiners)
    {
        var ores = MiningPlanner.GateSmelters(context.Map).Select(smelter => smelter.Ore).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return gateMiners
            .Where(miner => ores.Contains(miner.TradeSymbol))
            .ToDictionary(miner => miner.ShipSymbol, miner => miner.TradeSymbol, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gives a free miner its next trip: selling ore it holds, at a market that makes something from it where such a sale
    /// pays (D91), else the best mining target, a scarce ore no miner works on near its market first (D48, D53). The reason
    /// says <c>uncovered</c> when that came before D28's choice. A full hold only sells, even where the sale doesn't pay for
    /// its fuel: a trip keeps the other ores a market buys within one tank (D71), so its hold can fill with them, and a mining
    /// trip would turn to selling at once and end without its ore aboard, on every tick. A drone whose every pair below
    /// ABUNDANT has a miner shares one (D77, reason <c>shared</c>): it is passed over to the trading plan only when nothing
    /// below ABUNDANT is left that it can mine and sell. A market that only pays for an ore comes last (D91, reason
    /// <c>wealth</c>): a drone shares a pair whose market makes something from its ore before it mines for one. A gate miner
    /// mines its ore for the jump gate's smelter of it with the lowest supply, after selling what it holds (D92, reason
    /// <c>gate</c>), and parks at no collection point while it is dedicated.
    /// </summary>
    /// <param name="gateOre">The ore the miner is dedicated to for the jump gate's smelters (D92); empty for any other.</param>
    /// <returns>False when there is nothing it can mine and sell.</returns>
    private async Task<bool> GiveTripAsync(
        MiningContext context,
        ShipModel miner,
        string gateOre,
        HashSet<string> heldKeys,
        Dictionary<string, string> heldBy,
        Dictionary<string, int> minersOn,
        List<CoveringTrip> covered,
        IReadOnlyList<CollectionPoint> points,
        Dictionary<string, List<(string Ship, string Ore)>> places,
        CancellationToken cancellationToken)
    {
        // D92: a gate miner stays on its ore; a place at a far asteroid would keep it there for good.
        var dedicated = gateOre.Length > 0;

        // D83: a drone at a collection point's asteroid stays there while the point is open; the shuttle takes its hold.
        if (!dedicated && FleetRoles.IsMiningDrone(miner) && points.FirstOrDefault(point => IsAt(miner, point.AsteroidSymbol)) is { } here)
        {
            await StartParkedAsync(miner, here, drifting: false, heldKeys, heldBy, covered, places, cancellationToken);
            return true;
        }

        var holdIsFull = miner.CargoCapacity > 0 && miner.CargoCurrent >= miner.CargoCapacity;
        if (TradeRoutePlanner.TryFindBestCargoSale(context.Map, miner, holdIsFull, out var ore, out var sale, supplyFirst: true))
        {
            covered.Add(new CoveringTrip(ore.Symbol, sale.WaypointSymbol, miner.FuelCapacity));
            await StartAsync(miner, new MineAndSellGoal
            {
                TradeSymbol = ore.Symbol,
                SourceWaypointSymbol = miner.WaypointSymbol ?? string.Empty,
                SellWaypointSymbol = sale.WaypointSymbol,
                Selling = true,
            }, "held_cargo", cancellationToken);
            return true;
        }

        if (holdIsFull)
        {
            logger.LogDebug("Mining plan: ship {ShipSymbol} has a full hold that no market it can reach buys.", miner.Symbol);
            return false;
        }

        // D92: "those miners being dedicated to those ores", sharing a smelter's pair with any other miner there.
        if (dedicated && MiningPlanner.GateTargets(context, miner, gateOre, minersOn).FirstOrDefault() is { } smelter)
        {
            heldKeys.Add(smelter.Key);
            heldBy[smelter.Key] = miner.Symbol;
            minersOn[smelter.Key] = minersOn.GetValueOrDefault(smelter.Key) + 1;
            covered.Add(new CoveringTrip(smelter.Ore, smelter.SellWaypointSymbol, miner.FuelCapacity));
            await StartAsync(miner, TripTo(smelter), Gate, cancellationToken);
            return true;
        }

        // D91: with no pair left whose market makes something from its ore, a drone shares one before it mines for a market
        // that only pays for the ore.
        var ranked = MiningPlanner.MiningTargets(context, miner, heldKeys);
        if (ranked.Count == 0 || !ranked[0].FeedsProduction)
        {
            if (!dedicated && await TryJoinPointAsync(context, miner, points, heldKeys, heldBy, covered, places, cancellationToken))
            {
                return true;
            }

            // D77: "I'd like the miners to only mine, even if there is more profit in trading. They can mine until every
            // mineral is ABUNDANT." The command ship keeps D38: it takes what pays it most, so it shares nothing.
            if (FleetRoles.IsMiningDrone(miner)
                && MiningPlanner.SharedTargets(context, miner, minersOn).FirstOrDefault() is { } shared
                && (shared.FeedsProduction || ranked.Count == 0))
            {
                minersOn[shared.Key] = minersOn.GetValueOrDefault(shared.Key) + 1;
                covered.Add(new CoveringTrip(shared.Ore, shared.SellWaypointSymbol, miner.FuelCapacity));
                await StartAsync(miner, TripTo(shared), "shared", cancellationToken);
                return true;
            }

            if (ranked.Count == 0)
            {
                logger.LogDebug("Mining plan: nothing to mine that ship {ShipSymbol} can reach and sell.", miner.Symbol);
                return false;
            }
        }

        var target = MiningPlanner.UncoveredFirst(context.Map, miner, ranked, covered)[0];

        // D83: a far asteroid's scarce ores come after the uncovered ones a drone serves on its own (D48, near before far).
        var uncovered = target.FeedsProduction && target.LowSupply && !covered.Any(trip => trip.Covers(context.Map, target.Ore, target.SellWaypointSymbol));
        if (!uncovered && !dedicated && await TryJoinPointAsync(context, miner, points, heldKeys, heldBy, covered, places, cancellationToken))
        {
            return true;
        }

        var reason = !target.FeedsProduction ? "wealth" : target != ranked[0] ? "uncovered" : target.Surveyed ? "surveyed" : target.LowSupply ? "low_supply" : "lowest_supply";
        heldKeys.Add(target.Key);
        heldBy[target.Key] = miner.Symbol;
        minersOn[target.Key] = minersOn.GetValueOrDefault(target.Key) + 1;
        covered.Add(new CoveringTrip(target.Ore, target.SellWaypointSymbol, miner.FuelCapacity));
        await StartAsync(miner, TripTo(target), reason, cancellationToken);
        return true;
    }

    /// <summary>A trip that mines a target's ore at its asteroid and sells it at its market, drifting there first when it is far (D45).</summary>
    private static MineAndSellGoal TripTo(MiningTarget target)
        => new()
        {
            TradeSymbol = target.Ore,
            SourceWaypointSymbol = target.AsteroidSymbol,
            SellWaypointSymbol = target.SellWaypointSymbol,
            Drifting = target.Far,
        };

    private async Task StartAsync(ShipModel miner, MineAndSellGoal trip, string reason, CancellationToken cancellationToken)
    {
        await goals.SetActiveGoalAsync(miner.Symbol, trip, cancellationToken);
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} mines {TradeSymbol} at {WaypointSymbol} and sells it at {SellWaypoint} ({Reason}).",
            JournalEvents.MiningStarted,
            miner.Symbol,
            trip.TradeSymbol,
            trip.SourceWaypointSymbol,
            trip.SellWaypointSymbol,
            reason);
    }

    /// <summary>
    /// A mining drone takes a place at a collection point that wants more drones (D83), one per SCARCE or LIMITED ore (D48),
    /// drifting to its market first when that is out of its CRUISE reach (D45). The command ship takes none (D38).
    /// </summary>
    private async Task<bool> TryJoinPointAsync(
        MiningContext context,
        ShipModel drone,
        IReadOnlyList<CollectionPoint> points,
        HashSet<string> heldKeys,
        Dictionary<string, string> heldBy,
        List<CoveringTrip> covered,
        Dictionary<string, List<(string Ship, string Ore)>> places,
        CancellationToken cancellationToken)
    {
        if (!FleetRoles.IsMiningDrone(drone))
        {
            return false;
        }

        foreach (var point in points.Where(point => point.DronesWanted > (places.TryGetValue(point.Key, out var there) ? there.Count : 0)))
        {
            var reaches = MiningPlanner.CanReach(context.Map, drone, point.SellWaypointSymbol);
            if (reaches || MiningPlanner.CanDriftTo(context.Map, drone, point.SellWaypointSymbol))
            {
                await StartParkedAsync(drone, point, drifting: !reaches, heldKeys, heldBy, covered, places, cancellationToken);
                return true;
            }
        }

        return false;
    }

    /// <summary>Gives a drone its place at a collection point, for the scarce ore no drone there mines for yet (D48).</summary>
    private async Task StartParkedAsync(
        ShipModel drone,
        CollectionPoint point,
        bool drifting,
        HashSet<string> heldKeys,
        Dictionary<string, string> heldBy,
        List<CoveringTrip> covered,
        Dictionary<string, List<(string Ship, string Ore)>> places,
        CancellationToken cancellationToken)
    {
        if (!places.TryGetValue(point.Key, out var there))
        {
            there = [];
            places[point.Key] = there;
        }

        there.RemoveAll(place => place.Ship.Equals(drone.Symbol, StringComparison.OrdinalIgnoreCase));
        var taken = there.Select(place => place.Ore).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ore = point.ScarceOres.FirstOrDefault(scarce => !taken.Contains(scarce))
            ?? point.ScarceOres.FirstOrDefault()
            ?? point.Ores[0];
        there.Add((drone.Symbol, ore));

        var key = MiningPlanner.OpportunityKey(point.SellWaypointSymbol, ore);
        heldKeys.Add(key);
        heldBy[key] = drone.Symbol;
        covered.Add(new CoveringTrip(ore, point.SellWaypointSymbol, drone.FuelCapacity));

        var job = new MineForShuttleGoal
        {
            TradeSymbol = ore,
            AsteroidWaypointSymbol = point.AsteroidSymbol,
            SellWaypointSymbol = point.SellWaypointSymbol,
            Drifting = drifting,
        };
        await goals.SetActiveGoalAsync(drone.Symbol, job, cancellationToken);
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} mines {TradeSymbol} at {WaypointSymbol} and sells it at {SellWaypoint} ({Reason}).",
            JournalEvents.MiningStarted,
            drone.Symbol,
            ore,
            point.AsteroidSymbol,
            point.SellWaypointSymbol,
            Collection);
    }

    /// <summary>Starts a designated shuttle's round of collecting at its point (D83).</summary>
    private async Task StartRoundAsync(ShipModel shuttle, CollectionPoint point, CancellationToken cancellationToken)
    {
        await goals.SetActiveGoalAsync(
            shuttle.Symbol,
            new CollectOreGoal { AsteroidWaypointSymbol = point.AsteroidSymbol, SellWaypointSymbol = point.SellWaypointSymbol },
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
    /// The system's collection points (D83, <see cref="MiningPlanner.CollectionPoints"/>), judged with the tanks of a mining
    /// drone and a light shuttle as the cheapest shipyard there sells them; none while either isn't sold there.
    /// </summary>
    private static IReadOnlyList<CollectionPoint> CollectionPointsIn(
        MiningContext context,
        string systemSymbol,
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyList<ShipyardWaypointDto> shipyardList)
    {
        var drone = CheapestListing(shipyardList, systemSymbol, MiningDroneShipType);
        var shuttle = CheapestListing(shipyardList, systemSymbol, LightShuttleShipType);
        if (drone is null || shuttle is null)
        {
            return [];
        }

        var droneTank = fleet.FirstOrDefault(FleetRoles.IsMiningDrone)?.FuelCapacity ?? drone.Value.Ship.FuelCapacity;
        return MiningPlanner.CollectionPoints(
            context.Map,
            new ShipModel("NEW-DRONE", systemSymbol, drone.Value.Shipyard.WaypointSymbol, "DOCKED", "CRUISE", droneTank, droneTank, CargoCapacity: drone.Value.Ship.CargoCapacity, ShipType: MiningDroneShipType),
            new ShipModel("NEW-SHUTTLE", systemSymbol, shuttle.Value.Shipyard.WaypointSymbol, "DOCKED", "CRUISE", shuttle.Value.Ship.FuelCapacity, shuttle.Value.Ship.FuelCapacity, CargoCapacity: shuttle.Value.Ship.CargoCapacity, ShipType: LightShuttleShipType));
    }

    /// <summary>
    /// The cheapest shipyard of a system that sells a ship type with a known price and tank, and its listing; never one that has
    /// it SCARCE (D121).
    /// </summary>
    private static (ShipyardWaypointDto Shipyard, ShipyardShipDto Ship)? CheapestListing(IReadOnlyList<ShipyardWaypointDto> shipyardList, string systemSymbol, string shipType)
        => shipyardList
            .Where(shipyard => shipyard.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))
            .SelectMany(shipyard => shipyard.Ships
                .Where(ship => ship.Type.Equals(shipType, StringComparison.OrdinalIgnoreCase) && ship.PurchasePrice > 0 && ship.FuelCapacity > 0 && !ScarceShips.IsScarce(ship))
                .Select(ship => ((ShipyardWaypointDto Shipyard, ShipyardShipDto Ship)?)(shipyard, ship)))
            .OrderBy(listing => listing!.Value.Ship.PurchasePrice)
            .ThenBy(listing => listing!.Value.Shipyard.WaypointSymbol, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>Whether a drone with a place at the point is parked at its asteroid: there, and not in flight.</summary>
    private static bool IsParkedAt(ShipModel drone, CollectionPoint point)
        => IsAt(drone, point.AsteroidSymbol) && drone.LocalStatus != ShipLocalStatus.InTransit;

    private static bool IsAt(ShipModel ship, string waypointSymbol)
        => string.Equals(ship.WaypointSymbol, waypointSymbol, StringComparison.OrdinalIgnoreCase);

    /// <summary>A collection point's key: its market and asteroid, as <see cref="CollectionPoint.Key"/> has it.</summary>
    private static string PointKey(string sellWaypointSymbol, string asteroidSymbol)
        => $"{sellWaypointSymbol}|{asteroidSymbol}".ToUpperInvariant();

    /// <summary>The collection points for the plan's state, with the shuttles designated for each and the drones there.</summary>
    private static IReadOnlyList<CollectionPointState> CollectionPointStates(
        IReadOnlyDictionary<string, IReadOnlyList<CollectionPoint>> pointsBySystem,
        IReadOnlyDictionary<string, List<(string Ship, string Ore)>> places,
        IReadOnlyDictionary<string, List<string>> designated)
        => [.. pointsBySystem.Values
            .SelectMany(points => points)
            .OrderBy(point => point.Key, StringComparer.Ordinal)
            .Select(point => new CollectionPointState
            {
                AsteroidWaypointSymbol = point.AsteroidSymbol,
                SellWaypointSymbol = point.SellWaypointSymbol,
                Ores = point.Ores,
                ScarceOres = point.ScarceOres,
                ShuttleSymbols = [.. designated.GetValueOrDefault(point.Key, []).Order(StringComparer.Ordinal)],
                DroneSymbols = [.. (places.TryGetValue(point.Key, out var there) ? there : []).Select(place => place.Ship).Order(StringComparer.Ordinal)],
            })];

    /// <summary>Whether the contract plan mines now: it takes every free miner (D23), a bought drone included.</summary>
    private async Task<bool> ContractTakesMinersAsync(CancellationToken cancellationToken)
        => await settings.IsPlanEnabledAsync(AutomationPlan.Contract, cancellationToken)
            && await contractPlans.GetAsync(cancellationToken) is { Status: ContractMineralPlanStatus.Active } contract
            && contract.UnitsFulfilled < contract.UnitsRequired;

    /// <summary>
    /// Says what the plan would buy (<see cref="NeedAsync"/>), and buys it when the order ships are bought in lets it (D43,
    /// <see cref="IPurchaseOrder"/>), within the credit reserve. One a pass: the next pass counts the new ship. A shuttle
    /// bought for a collection point is designated for it (D83), which the role board reads to keep it collecting. A drone
    /// bought for the jump gate's smelters is noted with its ore (D92): it mines that ore for them, and the next for the ore
    /// waits <c>Mining.GateMinerIntervalMinutes</c>.
    /// </summary>
    private async Task BuyAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyList<string> systems,
        FleetRoleBoard board,
        IReadOnlySet<string> heldKeys,
        IReadOnlyCollection<CoveringTrip> covered,
        bool freeAtStart,
        Collecting collecting,
        List<GateMinerState> gateMiners,
        IReadOnlyList<ShipyardWaypointDto> shipyardList,
        CancellationToken cancellationToken)
    {
        var purchase = await NeedAsync(fleet, systems, board, heldKeys, covered, freeAtStart, collecting, gateMiners, shipyardList, cancellationToken);
        var need = purchase.Need;
        if (!await purchaseOrder.ReportAsync(AutomationPlan.Mining, need, cancellationToken))
        {
            return;
        }

        var purchased = await shipPurchases.TryPurchaseAsync(need.ShipType, need.ShipyardWaypointSymbol, cancellationToken);
        if (!purchased.IsSuccess)
        {
            logger.LogDebug(
                "Mining plan: {ShipType} purchase denied at {Shipyard} — {Reason}.",
                need.ShipType,
                need.ShipyardWaypointSymbol,
                purchased.FailureReason ?? "Purchase failed.");
            return;
        }

        if (purchase.ShuttlePoint.Length > 0 && purchased.PurchasedShip is { } shuttle)
        {
            if (!collecting.Designated.TryGetValue(purchase.ShuttlePoint, out var shuttles))
            {
                shuttles = [];
                collecting.Designated[purchase.ShuttlePoint] = shuttles;
            }

            shuttles.Add(shuttle.Symbol);
        }

        if (purchase.GateOre.Length > 0 && purchased.PurchasedShip is { } drone)
        {
            gateMiners.Add(new GateMinerState { ShipSymbol = drone.Symbol, TradeSymbol = purchase.GateOre, BoughtAt = TimeProvider.System.GetUtcNow() });
            logger.LogInformation(
                "Mining plan: ship {ShipSymbol} was bought for the jump gate's smelters, and mines only {TradeSymbol} for them while the gate needs a material made from it (D92).",
                drone.Symbol,
                purchase.GateOre);
        }
    }

    /// <summary>
    /// A light shuttle for a collection point (D83), with the drones for scarce minerals (<see cref="PurchaseTier.Coverage"/>):
    /// for a point where a drone has a place and no shuttle is designated yet; else a second for a point where a drone
    /// parked at the asteroid waits with its hold full while its shuttle is away selling. None while no shipyard of the
    /// system sells one with a known price.
    /// </summary>
    private static Purchase ShuttleNeed(string systemSymbol, Collecting collecting, IReadOnlyList<ShipyardWaypointDto> shipyardList)
    {
        if (!collecting.PointsBySystem.TryGetValue(systemSymbol, out var points)
            || points.Count == 0
            || CheapestListing(shipyardList, systemSymbol, LightShuttleShipType) is not { } listing)
        {
            return Purchase.Nothing;
        }

        var need = new PurchaseNeed(PurchaseTier.Coverage, LightShuttleShipType, listing.Shipyard.WaypointSymbol, listing.Ship.PurchasePrice);
        foreach (var point in points)
        {
            var shuttles = collecting.Designated.GetValueOrDefault(point.Key, []);
            var hasDrones = collecting.Places.TryGetValue(point.Key, out var there) && there.Count > 0;
            if (hasDrones && shuttles.Count == 0)
            {
                return new Purchase(need, point.Key);
            }

            var away = collecting.Rounds.Where(entry => entry.Round.AsteroidWaypointSymbol.Equals(point.AsteroidSymbol, StringComparison.OrdinalIgnoreCase)).ToList();
            var waits = collecting.Parked.Any(entry => IsParkedAt(entry.Ship, point)
                && entry.Ship.CargoCapacity > 0
                && entry.Ship.CargoCurrent >= entry.Ship.CargoCapacity);
            if (shuttles.Count is > 0 and < MaxShuttlesPerPoint
                && waits
                && away.Any(entry => entry.Round.Selling)
                && !away.Any(entry => !entry.Round.Selling))
            {
                return new Purchase(need, point.Key);
            }
        }

        return Purchase.Nothing;
    }

    /// <summary>
    /// The mining drone the plan would buy, at the shipyard of a system where our ships are that sells it for the least,
    /// up to <c>Mining.MaxDrones</c>, and none while the contract takes the miners (D23):
    /// <list type="bullet">
    ///   <item>a drone for a scarce ore (<see cref="PurchaseTier.Coverage"/>, D48), while the system has fewer mining drones
    ///   than SCARCE or LIMITED ores a new drone could serve, each counted once per area (<see cref="MiningPlanner.ScarceOres"/>,
    ///   D53): one drone per scarce mineral and area, which the role board keeps mining. It doesn't ask the board what pays
    ///   most. A gate miner counts only for its own ore (D92);</item>
    ///   <item>then, while the jump gate needs materials, a drone for an ore its smelters are short of
    ///   (<see cref="MiningPlanner.GateOresShort"/>), one per ore every <c>Mining.GateMinerIntervalMinutes</c>, at the gate's
    ///   place in the order (<see cref="PurchaseTier.Construction"/>, D92), the ore whose smelter has the lowest supply
    ///   first;</item>
    ///   <item>otherwise, when every miner works, a drone whose first trip by the miners' own ranking (the trips under way
    ///   held) would serve a market short of its ore (<see cref="PurchaseTier.Alternating"/>, D22, D28), which, with the
    ///   role board on, the board would have mine (<see cref="IRoleAdvisor"/>).</item>
    /// </list>
    /// </summary>
    private async Task<Purchase> NeedAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyList<string> systems,
        FleetRoleBoard board,
        IReadOnlySet<string> heldKeys,
        IReadOnlyCollection<CoveringTrip> covered,
        bool freeAtStart,
        Collecting collecting,
        IReadOnlyList<GateMinerState> gateMiners,
        IReadOnlyList<ShipyardWaypointDto> shipyardList,
        CancellationToken cancellationToken)
    {
        if (await ContractTakesMinersAsync(cancellationToken))
        {
            return Purchase.Nothing;
        }

        // D83: the shuttles come first: a drone parked at a far asteroid sells nothing without one.
        foreach (var systemSymbol in systems)
        {
            if (ShuttleNeed(systemSymbol, collecting, shipyardList) is { Need.Tier: not PurchaseTier.None } shuttle)
            {
                return shuttle;
            }
        }

        return await DroneNeedAsync(fleet, systems, board, heldKeys, covered, freeAtStart, collecting, gateMiners, shipyardList, cancellationToken);
    }

    private async Task<Purchase> DroneNeedAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyList<string> systems,
        FleetRoleBoard board,
        IReadOnlySet<string> heldKeys,
        IReadOnlyCollection<CoveringTrip> covered,
        bool freeAtStart,
        Collecting collecting,
        IReadOnlyList<GateMinerState> gateMiners,
        IReadOnlyList<ShipyardWaypointDto> shipyardList,
        CancellationToken cancellationToken)
    {
        var maxDrones = await settings.GetAsync<int>(MaxMiningDronesSettingKey, cancellationToken);
        if (maxDrones <= 0)
        {
            maxDrones = DefaultMaxMiningDrones;
        }

        var drones = fleet.Count(ship => ship.IsMiningCapable);
        if (drones >= maxDrones)
        {
            logger.LogDebug("Mining plan: mining drone cap reached ({Current}/{Max}); purchase skipped.", drones, maxDrones);
            return Purchase.Nothing;
        }

        var forSystems = new List<(string System, MiningContext Context, ShipModel NewDrone, PurchaseNeed Need)>();
        foreach (var systemSymbol in systems)
        {
            // D121: never where the shipyard has it SCARCE.
            var listing = shipyardList
                .Where(candidate => candidate.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))
                .SelectMany(candidate => candidate.Ships
                    .Where(ship => ship.Type.Equals(MiningDroneShipType, StringComparison.OrdinalIgnoreCase) && ship.PurchasePrice > 0 && !ScarceShips.IsScarce(ship))
                    .Select(ship => (Shipyard: candidate, Ship: ship)))
                .OrderBy(candidate => candidate.Ship.PurchasePrice)
                .ThenBy(candidate => candidate.Shipyard.WaypointSymbol, StringComparer.Ordinal)
                .FirstOrDefault();
            if (listing.Shipyard is null)
            {
                logger.LogDebug("Mining plan: no shipyard in {SystemSymbol} with a known price for {ShipType} that isn't SCARCE.", systemSymbol, MiningDroneShipType);
                continue;
            }

            var (shipyard, forSale) = listing;
            var context = await miningContexts.ReadAsync(systemSymbol, cancellationToken);
            var newDrone = new ShipModel(
                "NEW-DRONE",
                systemSymbol,
                shipyard.WaypointSymbol,
                "DOCKED",
                "CRUISE",
                forSale.FuelCapacity,
                forSale.FuelCapacity,
                CargoCapacity: forSale.CargoCapacity,
                ShipType: MiningDroneShipType);
            var need = new PurchaseNeed(PurchaseTier.Coverage, MiningDroneShipType, shipyard.WaypointSymbol, forSale.PurchasePrice);
            forSystems.Add((systemSymbol, context, newDrone, need));

            // D83: a far asteroid's SCARCE or LIMITED ores count a drone each too (D48). D92: a gate miner mines only its ore
            // while the gate needs a material made from it, so it counts for that ore's areas alone.
            var dedicated = DedicatedIn(context, gateMiners);
            var dedicatedOres = dedicated.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var miningDrones = fleet.Count(ship => FleetRoles.IsMiningDrone(ship)
                && string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase)
                && !dedicated.ContainsKey(ship.Symbol));
            var scarceOres = MiningPlanner.ScarceOres(context, newDrone).Count(area => !dedicatedOres.Contains(area.Good));
            var pointDrones = collecting.PointsBySystem.GetValueOrDefault(systemSymbol, []).Sum(point => point.DronesWanted);
            if (miningDrones < scarceOres + pointDrones)
            {
                return new Purchase(need);
            }
        }

        // D92: while the jump gate needs materials, a drone for an ore its smelters are short of, one per ore every
        // Mining.GateMinerIntervalMinutes, at the gate's place in the order.
        var interval = await GateMinerIntervalAsync(cancellationToken);
        var now = TimeProvider.System.GetUtcNow();
        foreach (var (_, context, newDrone, need) in forSystems)
        {
            var ore = MiningPlanner.GateOresShort(context, newDrone)
                .FirstOrDefault(shortOre => !gateMiners.Any(miner => miner.TradeSymbol.Equals(shortOre, StringComparison.OrdinalIgnoreCase)
                    && now - miner.BoughtAt < interval));
            if (ore is not null)
            {
                return new Purchase(need with { Tier = PurchaseTier.Construction }, GateOre: ore);
            }
        }

        if (freeAtStart)
        {
            return Purchase.Nothing;
        }

        foreach (var (systemSymbol, context, newDrone, need) in forSystems)
        {
            var targets = MiningPlanner.MiningTargets(context, newDrone, heldKeys, covered);
            if (targets.Count == 0 || !targets[0].FeedsProduction || !targets[0].LowSupply)
            {
                logger.LogDebug(
                    "Mining plan: no drone bought in {SystemSymbol}: its first trip would not serve a market short of its ore ({Trip}).",
                    systemSymbol,
                    targets.Count == 0 ? "nothing it can reach" : $"{targets[0].Ore} for {targets[0].SellWaypointSymbol}, {targets[0].Supply}");
                continue;
            }

            // With the role board on (slice 6.9), a drone that would earn more trading would trade, and the next pass would
            // buy another for the same opening.
            if (board.RolesOn && !await roles.WouldTakeAsync(newDrone, FleetRole.Mine, cancellationToken))
            {
                logger.LogDebug(
                    "Mining plan: no drone bought in {SystemSymbol}: the role board would have it trade, which would pay it more.",
                    systemSymbol);
                continue;
            }

            return new Purchase(need with { Tier = PurchaseTier.Alternating });
        }

        return Purchase.Nothing;
    }

    /// <summary>The minutes between two gate miners for one ore (D92): <c>Mining.GateMinerIntervalMinutes</c>, else half an hour.</summary>
    private async Task<TimeSpan> GateMinerIntervalAsync(CancellationToken cancellationToken)
    {
        var minutes = await settings.GetAsync<int>(GateMinerIntervalSetting, cancellationToken);
        return TimeSpan.FromMinutes(minutes > 0 ? minutes : DefaultGateMinerIntervalMinutes);
    }

    /// <summary>
    /// Records the openings, the collection points with the shuttles designated for each (D83), and the gate's miners (D92).
    /// Only a change is written: the tick runs every 5 seconds.
    /// </summary>
    private async Task SaveStateAsync(
        MiningAutomationPlanState? existing,
        IReadOnlyList<MiningAutomationOpportunityState> opportunities,
        IReadOnlyList<CollectionPointState> collectionPoints,
        IReadOnlyList<GateMinerState> gateMiners,
        CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
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

        List<GateMinerState> miners = [.. gateMiners.OrderBy(miner => miner.ShipSymbol, StringComparer.Ordinal)];
        if (existing is not null
            && Same(existing.Opportunities, dated)
            && JsonSerializer.Serialize(existing.CollectionPoints, CompareOptions) == JsonSerializer.Serialize(collectionPoints, CompareOptions)
            && JsonSerializer.Serialize(existing.GateMiners, CompareOptions) == JsonSerializer.Serialize(miners, CompareOptions))
        {
            return;
        }

        await plans.UpsertAsync(
            PlanTypes.MiningAutomation,
            new MiningAutomationPlanState
            {
                PlanId = existing?.PlanId ?? Guid.NewGuid(),
                Opportunities = dated,
                CollectionPoints = collectionPoints,
                GateMiners = miners,
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
    /// What the plan knows of its collection points at a pass (D83): the points of each system, the drones with a place at
    /// each, the shuttles designated for each, the parked drones' and the shuttles' goals.
    /// </summary>
    private sealed record Collecting(
        IReadOnlyDictionary<string, IReadOnlyList<CollectionPoint>> PointsBySystem,
        IReadOnlyDictionary<string, List<(string Ship, string Ore)>> Places,
        Dictionary<string, List<string>> Designated,
        IReadOnlyList<(ShipModel Ship, MineForShuttleGoal Job)> Parked,
        IReadOnlyList<(ShipModel Ship, CollectOreGoal Round)> Rounds);

    /// <summary>
    /// What the plan would buy this pass: the need it tells the order ships are bought in, the key of the collection point a
    /// shuttle is for (D83), and the ore a drone for the jump gate's smelters is for (D92); each empty when it isn't one.
    /// </summary>
    private sealed record Purchase(PurchaseNeed Need, string ShuttlePoint = "", string GateOre = "")
    {
        /// <summary>Nothing to buy.</summary>
        public static readonly Purchase Nothing = new(PurchaseNeed.None);
    }
}
