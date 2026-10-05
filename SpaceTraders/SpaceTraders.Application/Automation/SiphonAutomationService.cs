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

        // How many siphoners work on each pair: a drone that shares one takes the pair with the fewest (D77).
        var siphonersOn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // The siphoners' trips: a free siphoner takes a scarce gas that has none near its market first (D48, D53).
        var covered = new List<CoveringTrip>();
        var free = new List<ShipModel>();
        foreach (var ship in fleet)
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (goal is SiphonAndSellGoal trip && trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                var key = MiningPlanner.OpportunityKey(trip.SellWaypointSymbol, trip.TradeSymbol);
                heldKeys.Add(key);
                heldBy[key] = ship.Symbol;
                siphonersOn[key] = siphonersOn.GetValueOrDefault(key) + 1;
                covered.Add(new CoveringTrip(trip.TradeSymbol, trip.SellWaypointSymbol, ship.FuelCapacity));
            }
            else if (board.IsSiphoner(ship) && FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                free.Add(ship);
            }
        }

        // Before the first drone there is no siphoner: the openings of every system where our ships are show
        // what a drone would be bought for.
        var freeAtStart = free.Count > 0;
        var gaveTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var opportunities = new List<MiningAutomationOpportunityState>();
        foreach (var systemSymbol in systems)
        {
            var map = (await tradeContexts.ReadAsync(systemSymbol, cancellationToken)).Map;
            var candidates = free.Where(ship => string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase)).ToList();
            var withTrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var siphoner in candidates)
            {
                if (await GiveTripAsync(map, siphoner, heldKeys, heldBy, siphonersOn, covered, cancellationToken))
                {
                    withTrip.Add(siphoner.Symbol);
                    gaveTrip.Add(siphoner.Symbol);
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

        await BuyDroneAsync(fleet, systems, board, heldKeys, covered, freeAtStart, cancellationToken);
        await SaveStateAsync(opportunities, cancellationToken);
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
        var maxDrones = await settings.ThresholdAsync(MaxDronesSetting, DefaultMaxDrones, cancellationToken);
        var drones = fleet.Count(FleetRoles.IsSiphoner);
        if (drones >= maxDrones)
        {
            logger.LogDebug("Siphon plan: siphon drone cap reached ({Current}/{Max}); purchase skipped.", drones, maxDrones);
            return PurchaseNeed.None;
        }

        var shipyardList = await shipyards.GetAllAsync(cancellationToken);
        foreach (var systemSymbol in systems)
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
            var need = new PurchaseNeed(PurchaseTier.Coverage, SiphonDroneShipType, shipyard.WaypointSymbol, forSale.PurchasePrice);

            var siphonDrones = fleet.Count(ship => FleetRoles.IsSiphoner(ship) && string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase));
            if (siphonDrones < SiphonPlanner.ScarceGases(map, newDrone).Count)
            {
                return need;
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

            return need with { Tier = PurchaseTier.Alternating };
        }

        return PurchaseNeed.None;
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
