using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Health;
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
    /// <summary>One pass of the plan: gives every free siphoner a trip, and buys a drone when one would serve a market short of a gas.</summary>
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
///   <item>otherwise it takes the best of <see cref="SiphonPlanner.SiphonTargets"/>: the market shortest of a gas
///   first (D28), SCARCE, then LIMITED, and once none is short, the lowest supply there is. One siphoner per sell
///   market and gas. There are no surveys: a siphon takes none;</item>
///   <item>when no siphoner was free, it buys a <c>SHIP_SIPHON_DRONE</c> when the drone's first trip, by the same
///   ranking, would serve a market short of its gas (D22, D28, D32): one a pass, so the next pass counts its trip,
///   up to <c>Siphon.MaxDrones</c> and within the credit reserve, at the shipyard that sells it for the least in
///   a system where our ships are. Gas contracts stay unsupported (D2, D31), so no contract takes the drones.</item>
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
    ILogger<SiphonAutomationService> logger) : ISiphonAutomationService
{
    /// <summary>The setting that holds the most siphon drones to keep (D32).</summary>
    public const string MaxDronesSetting = "Siphon.MaxDrones";

    /// <summary>The cap when the setting gives none (D32).</summary>
    internal const int DefaultMaxDrones = 10;

    private const string SiphonDroneShipType = "SHIP_SIPHON_DRONE";

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var board = await FleetRoleBoard.ReadAsync(settings, plans, cancellationToken);
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
            if (goal is SiphonAndSellGoal trip && trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                var key = MiningPlanner.OpportunityKey(trip.SellWaypointSymbol, trip.TradeSymbol);
                heldKeys.Add(key);
                heldBy[key] = ship.Symbol;
            }
            else if (board.IsSiphoner(ship) && FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                free.Add(ship);
            }
        }

        // Before the first drone there is no siphoner: the openings of every system where our ships are show
        // what a drone would be bought for.
        var freeAtStart = free.Count > 0;
        var opportunities = new List<MiningAutomationOpportunityState>();
        foreach (var systemSymbol in Systems(fleet))
        {
            var map = (await tradeContexts.ReadAsync(systemSymbol, cancellationToken)).Map;
            var candidates = free.Where(ship => string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase)).ToList();
            var withTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var siphoner in candidates)
            {
                if (await GiveTripAsync(map, siphoner, heldKeys, heldBy, cancellationToken))
                {
                    withTrip.Add(siphoner.Symbol);
                }
            }

            foreach (var opportunity in SiphonPlanner.LowSupplyOpportunities(map))
            {
                var held = heldBy.TryGetValue(opportunity.Key, out var holder);
                var able = candidates
                    .Where(siphoner => !withTrip.Contains(siphoner.Symbol) && MiningPlanner.CanReach(map, siphoner, opportunity.GasGiantSymbol))
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

        if (!freeAtStart)
        {
            await BuyDroneAsync(fleet, board, heldKeys, cancellationToken);
        }

        await SaveStateAsync(opportunities, cancellationToken);
    }

    /// <summary>The systems where our ships are, by symbol.</summary>
    private static IReadOnlyList<string> Systems(IReadOnlyList<ShipModel> fleet)
        => [.. fleet
            .Select(ship => ship.SystemSymbol)
            .OfType<string>()
            .Where(system => system.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Gives a free siphoner its next trip: selling goods it holds, else the best siphon target. A full hold only
    /// sells, even where the sale doesn't pay for its fuel: a siphon trip would turn to selling at once, and end
    /// without its gas aboard, on every tick.
    /// </summary>
    /// <returns>False when there is nothing it can siphon and sell.</returns>
    private async Task<bool> GiveTripAsync(
        TradeMarketMap map,
        ShipModel siphoner,
        HashSet<string> heldKeys,
        Dictionary<string, string> heldBy,
        CancellationToken cancellationToken)
    {
        var holdIsFull = siphoner.CargoCapacity > 0 && siphoner.CargoCurrent >= siphoner.CargoCapacity;
        if (TryFindHeldCargoSale(map, siphoner, holdIsFull, out var cargo, out var sale))
        {
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

        var targets = SiphonPlanner.SiphonTargets(map, siphoner, heldKeys);
        if (targets.Count == 0)
        {
            logger.LogDebug("Siphon plan: nothing to siphon that ship {ShipSymbol} can reach and sell.", siphoner.Symbol);
            return false;
        }

        var target = targets[0];
        heldKeys.Add(target.Key);
        heldBy[target.Key] = siphoner.Symbol;
        await StartAsync(siphoner, new SiphonAndSellGoal
        {
            TradeSymbol = target.Gas,
            SourceWaypointSymbol = target.GasGiantSymbol,
            SellWaypointSymbol = target.SellWaypointSymbol,
        }, target.LowSupply ? "low_supply" : "lowest_supply", cancellationToken);
        return true;
    }

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
    /// it siphons (D33), so this sells the gases its trip didn't, one a trip.
    /// </summary>
    private static bool TryFindHeldCargoSale(TradeMarketMap map, ShipModel siphoner, bool mustSell, out CargoItemModel cargo, out TradeSale sale)
    {
        cargo = new CargoItemModel(string.Empty, 0);
        sale = new TradeSale(string.Empty, 0, 0, 0);
        var found = false;
        foreach (var item in (siphoner.CargoInventory ?? []).Where(item => item.Units > 0))
        {
            if (TradeRoutePlanner.TryFindBestSale(map, siphoner, item.Symbol, item.Units, out var candidate)
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
    /// Buys a siphon drone when every siphoner works and the drone's first trip, by the siphoners' own ranking
    /// (<see cref="SiphonPlanner.SiphonTargets"/>, the trips under way held), would serve a market short of its gas
    /// (D22, D28, D32). One a pass: the next pass counts its trip. Up to <c>Siphon.MaxDrones</c> and within the
    /// credit reserve.
    /// </summary>
    private async Task BuyDroneAsync(IReadOnlyList<ShipModel> fleet, FleetRoleBoard board, IReadOnlySet<string> heldKeys, CancellationToken cancellationToken)
    {
        var maxDrones = await settings.ThresholdAsync(MaxDronesSetting, DefaultMaxDrones, cancellationToken);
        var drones = fleet.Count(FleetRoles.IsSiphoner);
        if (drones >= maxDrones)
        {
            logger.LogDebug("Siphon plan: siphon drone cap reached ({Current}/{Max}); purchase skipped.", drones, maxDrones);
            return;
        }

        var shipyardList = await shipyards.GetAllAsync(cancellationToken);
        foreach (var systemSymbol in Systems(fleet))
        {
            var shipyard = shipyardList
                .Where(candidate => candidate.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase)
                    && candidate.Ships.Any(ship => ship.Type.Equals(SiphonDroneShipType, StringComparison.OrdinalIgnoreCase) && ship.PurchasePrice > 0))
                .OrderBy(candidate => candidate.Ships.First(ship => ship.Type.Equals(SiphonDroneShipType, StringComparison.OrdinalIgnoreCase)).PurchasePrice)
                .ThenBy(candidate => candidate.WaypointSymbol, StringComparer.Ordinal)
                .FirstOrDefault();
            if (shipyard is null)
            {
                logger.LogDebug("Siphon plan: no shipyard in {SystemSymbol} with a known price for {ShipType}.", systemSymbol, SiphonDroneShipType);
                continue;
            }

            var map = (await tradeContexts.ReadAsync(systemSymbol, cancellationToken)).Map;
            var forSale = shipyard.Ships.First(ship => ship.Type.Equals(SiphonDroneShipType, StringComparison.OrdinalIgnoreCase));
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
            var targets = SiphonPlanner.SiphonTargets(map, newDrone, heldKeys);
            if (targets.Count == 0 || !targets[0].LowSupply)
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

            var purchased = await shipPurchases.TryPurchaseAsync(SiphonDroneShipType, shipyard.WaypointSymbol, cancellationToken);
            if (!purchased.IsSuccess)
            {
                logger.LogDebug(
                    "Siphon plan: siphon drone purchase denied at {Shipyard} — {Reason}.",
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
        var existing = await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.SiphonAutomation, cancellationToken);
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
            PlanTypes.SiphonAutomation,
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
