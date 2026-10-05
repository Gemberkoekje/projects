using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    ILogger<MiningAutomationService> logger) : IMiningAutomationService
{
    private const string MiningDroneShipType = "SHIP_MINING_DRONE";
    private const string MaxMiningDronesSettingKey = "Mining.MaxDrones";
    private const int DefaultMaxMiningDrones = 20;

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var board = await FleetRoleBoard.ReadAsync(settings, plans, cancellationToken);
        var fleet = await ships.GetAllAsync(cancellationToken);
        var active = await assignments.GetAllActiveAsync(cancellationToken);
        var withAssignment = active
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Business stays where our ships work: not where the command ship explores (asked on 2026-10-04).
        var explorers = BusinessSystems.Explorers(active);
        var systems = BusinessSystems.Of(fleet, explorers);

        var heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heldBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // How many miners work on each pair: a drone that shares one takes the pair with the fewest (D77).
        var minersOn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // The miners' trips: a free miner takes a scarce ore that has none near its market first (D48, D53).
        var covered = new List<CoveringTrip>();
        var free = new List<ShipModel>();
        foreach (var ship in fleet)
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (goal is MineAndSellGoal trip && trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                var key = MiningPlanner.OpportunityKey(trip.SellWaypointSymbol, trip.TradeSymbol);
                heldKeys.Add(key);
                heldBy[key] = ship.Symbol;
                minersOn[key] = minersOn.GetValueOrDefault(key) + 1;
                covered.Add(new CoveringTrip(trip.TradeSymbol, trip.SellWaypointSymbol, ship.FuelCapacity));
            }
            else if (board.IsMiner(ship) && FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                free.Add(ship);
            }
        }

        var freeAtStart = free.Count > 0;
        var gaveTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var opportunities = new List<MiningAutomationOpportunityState>();
        foreach (var system in fleet
            .Where(ship => board.IsMiner(ship) && !explorers.Contains(ship.Symbol) && !string.IsNullOrWhiteSpace(ship.SystemSymbol))
            .GroupBy(ship => ship.SystemSymbol!, StringComparer.OrdinalIgnoreCase))
        {
            var context = await miningContexts.ReadAsync(system.Key, cancellationToken);
            var candidates = free.Where(ship => string.Equals(ship.SystemSymbol, system.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            var withTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var miner in candidates)
            {
                if (await GiveTripAsync(context, miner, heldKeys, heldBy, minersOn, covered, cancellationToken))
                {
                    withTrip.Add(miner.Symbol);
                    gaveTrip.Add(miner.Symbol);
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

        await BuyDroneAsync(fleet, systems, board, heldKeys, covered, freeAtStart, cancellationToken);
        await SaveStateAsync(opportunities, cancellationToken);
    }

    /// <summary>
    /// Gives a free miner its next trip: selling ore it holds, else the best mining target, a scarce ore no miner works on
    /// near its market first (D48, D53). The reason says <c>uncovered</c> when that came before D28's choice. A full hold
    /// only sells, even where the sale doesn't pay for its fuel: a trip keeps the other ores a market buys within one tank
    /// (D71), so its hold can fill with them, and a mining trip would turn to selling at once and end without its ore
    /// aboard, on every tick. A drone whose every pair below ABUNDANT has a miner shares one (D77, reason <c>shared</c>):
    /// it is passed over to the trading plan only when nothing below ABUNDANT is left that it can mine and sell.
    /// </summary>
    /// <returns>False when there is nothing it can mine and sell.</returns>
    private async Task<bool> GiveTripAsync(
        MiningContext context,
        ShipModel miner,
        HashSet<string> heldKeys,
        Dictionary<string, string> heldBy,
        Dictionary<string, int> minersOn,
        List<CoveringTrip> covered,
        CancellationToken cancellationToken)
    {
        var holdIsFull = miner.CargoCapacity > 0 && miner.CargoCurrent >= miner.CargoCapacity;
        if (TradeRoutePlanner.TryFindBestCargoSale(context.Map, miner, holdIsFull, out var ore, out var sale))
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

        var ranked = MiningPlanner.MiningTargets(context, miner, heldKeys);
        if (ranked.Count == 0)
        {
            // D77: "I'd like the miners to only mine, even if there is more profit in trading. They can mine until every
            // mineral is ABUNDANT." The command ship keeps D38: it takes what pays it most, so it shares nothing.
            if (FleetRoles.IsMiningDrone(miner) && MiningPlanner.SharedTargets(context, miner, minersOn).FirstOrDefault() is { } shared)
            {
                minersOn[shared.Key] = minersOn.GetValueOrDefault(shared.Key) + 1;
                covered.Add(new CoveringTrip(shared.Ore, shared.SellWaypointSymbol, miner.FuelCapacity));
                await StartAsync(miner, TripTo(shared), "shared", cancellationToken);
                return true;
            }

            logger.LogDebug("Mining plan: nothing to mine that ship {ShipSymbol} can reach and sell.", miner.Symbol);
            return false;
        }

        var target = MiningPlanner.UncoveredFirst(context.Map, miner, ranked, covered)[0];
        var reason = target != ranked[0] ? "uncovered" : target.Surveyed ? "surveyed" : target.LowSupply ? "low_supply" : "lowest_supply";
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

    /// <summary>Whether the contract plan mines now: it takes every free miner (D23), a bought drone included.</summary>
    private async Task<bool> ContractTakesMinersAsync(CancellationToken cancellationToken)
        => await settings.IsPlanEnabledAsync(AutomationPlan.Contract, cancellationToken)
            && await contractPlans.GetAsync(cancellationToken) is { Status: ContractMineralPlanStatus.Active } contract
            && contract.UnitsFulfilled < contract.UnitsRequired;

    /// <summary>
    /// Says what the plan would buy (<see cref="DroneNeedAsync"/>), and buys it when the order ships are bought in lets it
    /// (D43, <see cref="IPurchaseOrder"/>), within the credit reserve. One a pass: the next pass counts the new drone.
    /// </summary>
    private async Task BuyDroneAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyList<string> systems,
        FleetRoleBoard board,
        IReadOnlySet<string> heldKeys,
        IReadOnlyCollection<CoveringTrip> covered,
        bool freeAtStart,
        CancellationToken cancellationToken)
    {
        var need = await DroneNeedAsync(fleet, systems, board, heldKeys, covered, freeAtStart, cancellationToken);
        if (!await purchaseOrder.ReportAsync(AutomationPlan.Mining, need, cancellationToken))
        {
            return;
        }

        var purchased = await shipPurchases.TryPurchaseAsync(need.ShipType, need.ShipyardWaypointSymbol, cancellationToken);
        if (!purchased.IsSuccess)
        {
            logger.LogDebug(
                "Mining plan: mining drone purchase denied at {Shipyard} — {Reason}.",
                need.ShipyardWaypointSymbol,
                purchased.FailureReason ?? "Purchase failed.");
        }
    }

    /// <summary>
    /// The mining drone the plan would buy, at the shipyard of a system where our ships are that sells it for the least,
    /// up to <c>Mining.MaxDrones</c>, and none while the contract takes the miners (D23):
    /// <list type="bullet">
    ///   <item>a drone for a scarce ore (<see cref="PurchaseTier.Coverage"/>, D48), while the system has fewer mining drones
    ///   than SCARCE or LIMITED ores a new drone could serve, each counted once per area (<see cref="MiningPlanner.ScarceOres"/>,
    ///   D53): one drone per scarce mineral and area, which the role board keeps mining. It doesn't ask the board what pays
    ///   most;</item>
    ///   <item>otherwise, when every miner works, a drone whose first trip by the miners' own ranking (the trips under way
    ///   held) would serve a market short of its ore (<see cref="PurchaseTier.Alternating"/>, D22, D28), which, with the
    ///   role board on, the board would have mine (<see cref="IRoleAdvisor"/>).</item>
    /// </list>
    /// </summary>
    private async Task<PurchaseNeed> DroneNeedAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyList<string> systems,
        FleetRoleBoard board,
        IReadOnlySet<string> heldKeys,
        IReadOnlyCollection<CoveringTrip> covered,
        bool freeAtStart,
        CancellationToken cancellationToken)
    {
        if (await ContractTakesMinersAsync(cancellationToken))
        {
            return PurchaseNeed.None;
        }

        var maxDrones = await settings.GetAsync<int>(MaxMiningDronesSettingKey, cancellationToken);
        if (maxDrones <= 0)
        {
            maxDrones = DefaultMaxMiningDrones;
        }

        var drones = fleet.Count(ship => ship.IsMiningCapable);
        if (drones >= maxDrones)
        {
            logger.LogDebug("Mining plan: mining drone cap reached ({Current}/{Max}); purchase skipped.", drones, maxDrones);
            return PurchaseNeed.None;
        }

        var shipyardList = await shipyards.GetAllAsync(cancellationToken);
        foreach (var systemSymbol in systems)
        {
            var shipyard = shipyardList
                .Where(candidate => candidate.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase)
                    && candidate.Ships.Any(ship => ship.Type.Equals(MiningDroneShipType, StringComparison.OrdinalIgnoreCase) && ship.PurchasePrice > 0))
                .OrderBy(candidate => candidate.Ships.First(ship => ship.Type.Equals(MiningDroneShipType, StringComparison.OrdinalIgnoreCase)).PurchasePrice)
                .ThenBy(candidate => candidate.WaypointSymbol, StringComparer.Ordinal)
                .FirstOrDefault();
            if (shipyard is null)
            {
                logger.LogDebug("Mining plan: no shipyard in {SystemSymbol} with a known price for {ShipType}.", systemSymbol, MiningDroneShipType);
                continue;
            }

            var context = await miningContexts.ReadAsync(systemSymbol, cancellationToken);
            var forSale = shipyard.Ships.First(ship => ship.Type.Equals(MiningDroneShipType, StringComparison.OrdinalIgnoreCase));
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

            var miningDrones = fleet.Count(ship => FleetRoles.IsMiningDrone(ship) && string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase));
            if (miningDrones < MiningPlanner.ScarceOres(context, newDrone).Count)
            {
                return need;
            }

            if (freeAtStart)
            {
                continue;
            }

            var targets = MiningPlanner.MiningTargets(context, newDrone, heldKeys, covered);
            if (targets.Count == 0 || !targets[0].LowSupply)
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

            return need with { Tier = PurchaseTier.Alternating };
        }

        return PurchaseNeed.None;
    }

    /// <summary>Records the openings. Only a change is written: the tick runs every 5 seconds.</summary>
    private async Task SaveStateAsync(IReadOnlyList<MiningAutomationOpportunityState> opportunities, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var existing = await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, cancellationToken);
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

        if (existing is not null && Same(existing.Opportunities, dated))
        {
            return;
        }

        await plans.UpsertAsync(
            PlanTypes.MiningAutomation,
            new MiningAutomationPlanState
            {
                PlanId = existing?.PlanId ?? Guid.NewGuid(),
                Opportunities = dated,
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
}
