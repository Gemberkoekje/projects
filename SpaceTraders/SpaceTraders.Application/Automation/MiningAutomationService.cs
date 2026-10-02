using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>The mining plan (PLAN.md slice 6.4).</summary>
public interface IMiningAutomationService
{
    /// <summary>One pass of the plan: gives every free miner a trip, and buys a drone when one would serve a market short of an ore.</summary>
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
///   <item>a miner that holds ore a market buys sells it first, where it fetches most after fuel: the ore
///   left over from a contract, for one;</item>
///   <item>otherwise it takes the best of <see cref="MiningPlanner.MiningTargets"/>: the market shortest of
///   an ore first (D28), SCARCE, then LIMITED, and once none is short, the lowest supply there is; within a
///   supply level, a surveyed ore first. One miner per sell market and ore;</item>
///   <item>when no miner was free, it buys a drone when the drone's first trip, by the same ranking, would
///   serve a market short of its ore (D22, D28): one a pass, so the next pass counts its trip, up to
///   <c>Mining.MaxDrones</c> and within the credit reserve; not while the contract plan mines, which would
///   take the drone (D23). A drone that would mine for a market that isn't short is not bought: the
///   opening that paid for it would stay open and pay for the next.</item>
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
    ILogger<MiningAutomationService> logger) : IMiningAutomationService
{
    private const string MiningDroneShipType = "SHIP_MINING_DRONE";
    private const string MaxMiningDronesSettingKey = "Mining.MaxDrones";
    private const int DefaultMaxMiningDrones = 20;

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

        var heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heldBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var free = new List<ShipModel>();
        foreach (var ship in fleet)
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (goal is MineAndSellGoal trip && trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                var key = MiningPlanner.OpportunityKey(trip.SellWaypointSymbol, trip.TradeSymbol);
                heldKeys.Add(key);
                heldBy[key] = ship.Symbol;
            }
            else if (FleetRoles.IsMiner(ship, surveyOn) && FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                free.Add(ship);
            }
        }

        var freeAtStart = free.Count > 0;
        var opportunities = new List<MiningAutomationOpportunityState>();
        foreach (var system in fleet
            .Where(ship => FleetRoles.IsMiner(ship, surveyOn) && !string.IsNullOrWhiteSpace(ship.SystemSymbol))
            .GroupBy(ship => ship.SystemSymbol!, StringComparer.OrdinalIgnoreCase))
        {
            var context = await miningContexts.ReadAsync(system.Key, cancellationToken);
            var candidates = free.Where(ship => string.Equals(ship.SystemSymbol, system.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            var withTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var miner in candidates)
            {
                if (await GiveTripAsync(context, miner, heldKeys, heldBy, cancellationToken))
                {
                    withTrip.Add(miner.Symbol);
                }
            }

            foreach (var opportunity in MiningPlanner.LowSupplyOpportunities(context.Map))
            {
                var held = heldBy.TryGetValue(opportunity.Key, out var holder);
                var able = candidates
                    .Where(miner => !withTrip.Contains(miner.Symbol) && MiningPlanner.CanReach(context.Map, miner, opportunity.AsteroidSymbol))
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

        if (!freeAtStart && !await ContractTakesMinersAsync(cancellationToken))
        {
            await BuyDroneAsync(fleet, surveyOn, heldKeys, cancellationToken);
        }

        await SaveStateAsync(opportunities, cancellationToken);
    }

    /// <summary>Gives a free miner its next trip: selling ore it holds, else the best mining target.</summary>
    /// <returns>False when there is nothing it can mine and sell.</returns>
    private async Task<bool> GiveTripAsync(
        MiningContext context,
        ShipModel miner,
        HashSet<string> heldKeys,
        Dictionary<string, string> heldBy,
        CancellationToken cancellationToken)
    {
        if (TryFindHeldOreSale(context.Map, miner, out var ore, out var sale))
        {
            await StartAsync(miner, new MineAndSellGoal
            {
                TradeSymbol = ore.Symbol,
                SourceWaypointSymbol = miner.WaypointSymbol ?? string.Empty,
                SellWaypointSymbol = sale.WaypointSymbol,
                Selling = true,
            }, "held_cargo", cancellationToken);
            return true;
        }

        var targets = MiningPlanner.MiningTargets(context, miner, heldKeys);
        if (targets.Count == 0)
        {
            logger.LogDebug("Mining plan: nothing to mine that ship {ShipSymbol} can reach and sell.", miner.Symbol);
            return false;
        }

        var target = targets[0];
        heldKeys.Add(target.Key);
        heldBy[target.Key] = miner.Symbol;
        await StartAsync(miner, new MineAndSellGoal
        {
            TradeSymbol = target.Ore,
            SourceWaypointSymbol = target.AsteroidSymbol,
            SellWaypointSymbol = target.SellWaypointSymbol,
        }, target.Surveyed ? "surveyed" : target.LowSupply ? "low_supply" : "lowest_supply", cancellationToken);
        return true;
    }

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
    /// For a miner that holds ore: the ore that fetches most where it sells best, after the fuel to get
    /// there, when that is anything at all. Other cargo is jettisoned on the next extraction.
    /// </summary>
    private static bool TryFindHeldOreSale(TradeMarketMap map, ShipModel miner, out CargoItemModel ore, out TradeSale sale)
    {
        ore = new CargoItemModel(string.Empty, 0);
        sale = new TradeSale(string.Empty, 0, 0, 0);
        var found = false;
        foreach (var item in (miner.CargoInventory ?? []).Where(item => item.Units > 0))
        {
            if (TradeRoutePlanner.TryFindBestSale(map, miner, item.Symbol, item.Units, out var candidate)
                && candidate.NetRevenue > 0
                && (!found || candidate.NetRevenue > sale.NetRevenue))
            {
                ore = item;
                sale = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Whether the contract plan mines now: it takes every free miner (D23), a bought drone included.</summary>
    private async Task<bool> ContractTakesMinersAsync(CancellationToken cancellationToken)
        => await settings.IsPlanEnabledAsync(AutomationPlan.Contract, cancellationToken)
            && await contractPlans.GetAsync(cancellationToken) is { Status: ContractMineralPlanStatus.Active } contract
            && contract.UnitsFulfilled < contract.UnitsRequired;

    /// <summary>
    /// Buys a drone when every miner works and the drone's first trip, by the miners' own ranking
    /// (<see cref="MiningPlanner.MiningTargets"/>, the trips under way held), would serve a market short of its
    /// ore (D22, D28). One a pass: the next pass counts its trip. Up to <c>Mining.MaxDrones</c> and within the
    /// credit reserve.
    /// </summary>
    private async Task BuyDroneAsync(
        IReadOnlyList<ShipModel> fleet,
        bool surveyOn,
        IReadOnlySet<string> heldKeys,
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
            return;
        }

        var shipyardList = await shipyards.GetAllAsync(cancellationToken);
        foreach (var systemSymbol in fleet
            .Where(ship => FleetRoles.IsMiner(ship, surveyOn) && !string.IsNullOrWhiteSpace(ship.SystemSymbol))
            .Select(ship => ship.SystemSymbol!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal))
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
                CargoCapacity: forSale.CargoCapacity);
            var targets = MiningPlanner.MiningTargets(context, newDrone, heldKeys);
            if (targets.Count == 0 || !targets[0].LowSupply)
            {
                logger.LogDebug(
                    "Mining plan: no drone bought in {SystemSymbol}: its first trip would not serve a market short of its ore ({Trip}).",
                    systemSymbol,
                    targets.Count == 0 ? "nothing it can reach" : $"{targets[0].Ore} for {targets[0].SellWaypointSymbol}, {targets[0].Supply}");
                continue;
            }

            var purchased = await shipPurchases.TryPurchaseAsync(MiningDroneShipType, shipyard.WaypointSymbol, cancellationToken);
            if (!purchased.IsSuccess)
            {
                logger.LogDebug(
                    "Mining plan: mining drone purchase denied at {Shipyard} — {Reason}.",
                    shipyard.WaypointSymbol,
                    purchased.FailureReason ?? "Purchase failed.");
            }

            return;
        }
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
