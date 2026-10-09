using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>The survey plan (PLAN.md slice 6.4).</summary>
public interface ISurveyPlanService
{
    /// <summary>
    /// One pass of the plan: ends the surveys that expired, gives every free surveyor its next survey, and with the role
    /// board on buys a surveyor for a system with mining drones that has none.
    /// </summary>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The survey plan (PLAN.md slice 6.4). Every ship that can survey surveys before anything else (D20); with the role
/// board on (slice 6.9), the ships it gives the survey role (<see cref="FleetRoleBoard"/>):
/// each tick a free surveyor gets one survey to take (<see cref="SurveyWaypointGoal"/>), chosen by
/// <see cref="MiningPlanner.SurveyTargets"/> among the ores with fewer usable surveys than the stock,
/// <see cref="StockPerOreSetting"/> (D27):
/// <list type="bullet">
///   <item>the contract's ore, at the contract's asteroid, while the contract plan mines it;</item>
///   <item>then an ore a market in the system buys, at the asteroid nearest the market that pays most
///   for it, among those the miners can reach; the fewest usable surveys first, then the best paid.</item>
/// </list>
/// With the stock for every ore, a surveyor waits until a survey runs out, or mines and siphons in its spare time
/// (slice 6.8): a survey that needs taking takes it off its spare-time trip at once, with its hold aboard (D37). A
/// surveyor takes the best target no other surveyor works on, or the best one when all are taken, among those it can survey
/// at and fly on from to a market that sells fuel (B58: <see cref="MiningPlanner.CanSurveyAt"/>). The plan also ends
/// the surveys that expired (<see cref="ISurveyKeeper.ExpireAsync"/>), for the survey dashboard.
/// <para>
/// With the role board on, it buys a designated surveyor (D47): a <c>SHIP_SURVEYOR</c> for each system with mining drones
/// and no ship that can only survey, first in the order ships are bought in after the contract's drone (D43). The board
/// gives it the survey role (D38), which frees the command ship for what pays it most.
/// </para>
/// <para>
/// A ship that can only survey works where most mining drones work (D54, <see cref="MiningPlanner.TryFindBusierArea"/>):
/// when an area out of its CRUISE reach has more drones than its own, it drifts there (<see cref="MoveToWaypointGoal"/>)
/// before it surveys again; a tie keeps it where it is. Each area with drones gets a survey ship of its own (D55): an area
/// another survey ship works in or moves to is taken, and of two in one area, the one free first moves to an area with
/// drones that has none. The plan buys one more surveyor for each such area, after the drones per scarce mineral (D43).
/// </para>
/// </summary>
public sealed class SurveyPlanService(
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipAssignmentRepository assignments,
    IContractMineralPlanRepository contractPlans,
    IMiningContextReader miningContexts,
    ISurveyKeeper surveyKeeper,
    IPlanRepository plans,
    ISettingsRepository settings,
    SpareTimeInterruption interruption,
    IShipyardRepository shipyards,
    IShipPurchaseService shipPurchases,
    IPurchaseOrder purchaseOrder,
    ILogger<SurveyPlanService> logger) : ISurveyPlanService
{
    /// <summary>The ship the plan buys to survey (D47): a drone frame with a surveyor, and no hold.</summary>
    public const string SurveyorShipType = "SHIP_SURVEYOR";

    /// <summary>The setting that holds the usable surveys to keep of each ore (D27).</summary>
    public const string StockPerOreSetting = "Survey.StockPerOre";

    /// <summary>The stock when the setting gives none: two, so a miner has a choice (D27).</summary>
    internal const int DefaultStockPerOre = 2;

    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var now = TimeProvider.System.GetUtcNow();
        await surveyKeeper.ExpireAsync(now, cancellationToken);

        var board = await FleetRoleBoard.ReadAsync(settings, plans, cancellationToken, surveyOn: true);
        var fleet = await ships.GetAllAsync(cancellationToken);
        var active = await assignments.GetAllActiveAsync(cancellationToken);
        var withAssignment = active
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Business stays where our ships work: not where the command ship explores (asked on 2026-10-04).
        var explorers = BusinessSystems.Explorers(active);

        var surveying = new Dictionary<string, SurveyWaypointGoal>(StringComparer.OrdinalIgnoreCase);
        var free = new List<ShipModel>();
        var gathering = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Where each ship that can only survey works: where it is, or where it is moving to (D54, D55).
        var surveyShipsAt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ship in fleet.Where(board.IsSurveyor))
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (FleetRoles.CanOnlySurvey(ship))
            {
                surveyShipsAt[ship.Symbol] = goal is MoveToWaypointGoal moving && moving.Status is not GoalStatus.Blocked and not GoalStatus.Completed
                    ? moving.TargetWaypointSymbol
                    : MiningPlanner.Position(ship);
            }

            if (goal is SurveyWaypointGoal survey && survey.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                surveying[ship.Symbol] = survey;
            }
            else if (FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                free.Add(ship);
            }
            else if (SpareTimeInterruption.IsInterruptible(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                // On a spare-time trip that fills its hold: a survey comes first (D37).
                free.Add(ship);
                gathering.Add(ship.Symbol);
            }
        }

        var contract = await contractPlans.GetAsync(cancellationToken);
        var stock = await settings.ThresholdAsync(StockPerOreSetting, DefaultStockPerOre, cancellationToken);

        // D83: the mining plan's collection points, as its last pass left them: their asteroids get surveys, and a survey ship
        // that reaches one parks there, as the drones do.
        var points = ((await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, cancellationToken))?.CollectionPoints ?? [])
            .Select(point => new CollectionPoint(point.AsteroidWaypointSymbol, point.SellWaypointSymbol, point.Ores, point.ScarceOres))
            .ToList();
        var targets = new List<SurveyPlanTarget>();
        var drones = new Dictionary<string, (TradeMarketMap Map, IReadOnlyList<string> Waypoints)>(StringComparer.OrdinalIgnoreCase);
        foreach (var system in fleet
            .Where(ship => board.IsSurveyor(ship) && !explorers.Contains(ship.Symbol) && !string.IsNullOrWhiteSpace(ship.SystemSymbol))
            .GroupBy(ship => ship.SystemSymbol!, StringComparer.OrdinalIgnoreCase))
        {
            var context = await miningContexts.ReadAsync(system.Key, cancellationToken);
            var miners = await MinersAsync(fleet, board, system.Key, cancellationToken);
            var systemPoints = points.Where(point => context.Map.MarketWaypoints.Contains(point.SellWaypointSymbol, StringComparer.OrdinalIgnoreCase)).ToList();
            var systemTargets = MiningPlanner.SurveyTargets(context, ContractOres(contract, system.Key), miners, stock, systemPoints);

            // B58: a surveyor surveys where it can fly on from; at a collection point's asteroid a survey ship stays parked.
            bool CanSurvey(ShipModel surveyor, string asteroid)
                => MiningPlanner.CanSurveyAt(context.Map, surveyor, asteroid)
                    || (FleetRoles.CanOnlySurvey(surveyor)
                        && systemPoints.Any(point => point.AsteroidSymbol.Equals(asteroid, StringComparison.OrdinalIgnoreCase))
                        && MiningPlanner.CanReach(context.Map, surveyor, asteroid));
            var droneWaypoints = await DroneWaypointsAsync(fleet, system.Key, cancellationToken);
            drones[system.Key] = (context.Map, droneWaypoints);

            foreach (var surveyor in free.Where(ship => string.Equals(ship.SystemSymbol, system.Key, StringComparison.OrdinalIgnoreCase)))
            {
                // D54, D55: a ship that can only survey works where most drones mine, each area with drones with one of its own.
                var others = system
                    .Where(ship => !ship.Symbol.Equals(surveyor.Symbol, StringComparison.OrdinalIgnoreCase) && surveyShipsAt.ContainsKey(ship.Symbol))
                    .Select(ship => surveyShipsAt[ship.Symbol])
                    .ToList();
                // B68: a survey ship at a collection point's asteroid stays parked there, as the drones do (D83). From there its own
                // area, as far as its fuel cruises, may hold none of the point's drones, which count in their market's area.
                var parked = surveyor.LocalStatus != ShipLocalStatus.InTransit
                    && systemPoints.Any(point => point.AsteroidSymbol.Equals(surveyor.WaypointSymbol, StringComparison.OrdinalIgnoreCase));
                if (FleetRoles.CanOnlySurvey(surveyor) && !parked && MiningPlanner.TryFindBusierArea(context.Map, surveyor, droneWaypoints, others, out var move))
                {
                    await goals.SetActiveGoalAsync(
                        surveyor.Symbol,
                        new MoveToWaypointGoal { TargetWaypointSymbol = move.MarketSymbol, Drifting = true },
                        cancellationToken);
                    surveyShipsAt[surveyor.Symbol] = move.MarketSymbol;
                    logger.LogInformation(
                        "Survey plan: ship {ShipSymbol} moves to {WaypointSymbol}, where {Drones} mining drones work and no other survey ship, against {OwnDrones} in its own area{Shared:l} (D54, D55).",
                        surveyor.Symbol,
                        move.MarketSymbol,
                        move.Drones,
                        move.OwnDrones,
                        move.Shared ? ", which another survey ship works in" : string.Empty);
                    continue;
                }

                var reachable = systemTargets
                    .Where(target => target.NeedsSurvey && CanSurvey(surveyor, target.AsteroidSymbol))
                    .ToList();
                var beyondStock = reachable.Count == 0 && FleetRoles.CanOnlySurvey(surveyor);
                if (beyondStock)
                {
                    // D52: "A (single role) surveyor which is idle is allowed to keep surveying, starting with whichever ore is
                    // lowest": with every ore it reaches at its stock, the ore with the fewest usable surveys first.
                    reachable = [.. systemTargets
                        .Where(target => CanSurvey(surveyor, target.AsteroidSymbol))
                        .OrderBy(target => target.UsableSurveys)
                        .ThenByDescending(target => target.ForContract)
                        .ThenByDescending(target => target.SellPrice)
                        .ThenBy(target => target.Ore, StringComparer.Ordinal)];
                }

                if (reachable.Count == 0)
                {
                    if (!gathering.Contains(surveyor.Symbol))
                    {
                        logger.LogDebug(
                            "Survey plan: ship {ShipSymbol} waits: every ore it can reach has {Stock} usable surveys.",
                            surveyor.Symbol,
                            stock);
                    }

                    continue;
                }

                var target = reachable.FirstOrDefault(candidate => !surveying.Values.Any(goal => Targets(goal, candidate))) ?? reachable[0];
                var goal = new SurveyWaypointGoal { TargetWaypointSymbol = target.AsteroidSymbol, TargetDepositSymbol = target.Ore };
                if (gathering.Contains(surveyor.Symbol))
                {
                    // The hold stays aboard: surveying needs no room, and the spare-time trip after the survey fills it
                    // on (D37).
                    if (!await interruption.TryReplaceAsync(surveyor.Symbol, goal, "survey", cancellationToken))
                    {
                        continue;
                    }
                }
                else
                {
                    await goals.SetActiveGoalAsync(surveyor.Symbol, goal, cancellationToken);
                }

                surveying[surveyor.Symbol] = goal;
                logger.LogDebug(
                    "Survey plan: ship {ShipSymbol} surveys {WaypointSymbol} for {TradeSymbol}{ForContract}{BeyondStock}.",
                    surveyor.Symbol,
                    target.AsteroidSymbol,
                    target.Ore,
                    target.ForContract ? " (the contract's)" : string.Empty,
                    beyondStock ? " beyond its stock (D52)" : string.Empty);
            }

            // The surveyors that can survey at each asteroid and fly on, or park there (D83): only for those is a target work the
            // plan could give (B55, B58).
            var reachedBy = systemTargets
                .Select(target => target.AsteroidSymbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    asteroid => asteroid,
                    asteroid => (IReadOnlyList<string>)[.. system.Where(surveyor => CanSurvey(surveyor, asteroid)).Select(surveyor => surveyor.Symbol).Order(StringComparer.Ordinal)],
                    StringComparer.OrdinalIgnoreCase);
            targets.AddRange(systemTargets.Select(target => new SurveyPlanTarget
            {
                TradeSymbol = target.Ore,
                WaypointSymbol = target.AsteroidSymbol,
                BuyerWaypointSymbol = target.BuyerSymbol,
                ForContract = target.ForContract,
                HasUsableSurvey = target.HasUsableSurvey,
                UsableSurveys = target.UsableSurveys,
                NeedsSurvey = target.NeedsSurvey,
                SurveyorShipSymbols = [.. surveying.Where(entry => Targets(entry.Value, target)).Select(entry => entry.Key).Order(StringComparer.Ordinal)],
                CandidateShipSymbols = reachedBy[target.AsteroidSymbol],
            }));
        }

        await BuySurveyorAsync(fleet, board, drones, cancellationToken);
        await SaveStateAsync(targets, now, cancellationToken);
    }

    /// <summary>
    /// Says what the plan would buy (<see cref="SurveyorNeedAsync"/>), and buys it when the order ships are bought in lets
    /// it (D43, <see cref="IPurchaseOrder"/>), within the credit reserve. With the role board off the command ship surveys
    /// (D20), and no surveyor is bought.
    /// </summary>
    private async Task BuySurveyorAsync(
        IReadOnlyList<ShipModel> fleet,
        FleetRoleBoard board,
        IReadOnlyDictionary<string, (TradeMarketMap Map, IReadOnlyList<string> Waypoints)> drones,
        CancellationToken cancellationToken)
    {
        var need = board.RolesOn ? await SurveyorNeedAsync(fleet, drones, cancellationToken) : PurchaseNeed.None;
        if (!await purchaseOrder.ReportAsync(AutomationPlan.Survey, need, cancellationToken))
        {
            return;
        }

        var purchased = await shipPurchases.TryPurchaseAsync(need.ShipType, need.ShipyardWaypointSymbol, cancellationToken);
        if (!purchased.IsSuccess)
        {
            logger.LogDebug(
                "Survey plan: surveyor purchase denied at {Shipyard} — {Reason}.",
                need.ShipyardWaypointSymbol,
                purchased.FailureReason ?? "Purchase failed.");
        }
    }

    /// <summary>
    /// A surveyor for the first system, by symbol, with a mining drone that needs one (<see cref="FleetRoles.NeedsSurveyShips"/>:
    /// not an ore hound, which surveys for itself, D120),
    /// at the system's shipyard that sells a <c>SHIP_SURVEYOR</c> for the least; none when no shipyard there is known to sell
    /// one. A system with no ship that can only survey needs its designated surveyor (D47,
    /// <see cref="PurchaseTier.Surveyor"/>); one with fewer of them than areas with mining drones, as they fly between them,
    /// needs one more (D55, <see cref="PurchaseTier.SurveyorPerArea"/>, after the drones per scarce mineral).
    /// </summary>
    private async Task<PurchaseNeed> SurveyorNeedAsync(
        IReadOnlyList<ShipModel> fleet,
        IReadOnlyDictionary<string, (TradeMarketMap Map, IReadOnlyList<string> Waypoints)> drones,
        CancellationToken cancellationToken)
    {
        // Slice 6.39 (D120): the survey ships serve the miners that don't survey for themselves; an ore hound does.
        var shipyardList = await shipyards.GetAllAsync(cancellationToken);
        foreach (var systemSymbol in fleet
            .Where(FleetRoles.NeedsSurveyShips)
            .Select(ship => ship.SystemSymbol)
            .OfType<string>()
            .Where(system => system.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal))
        {
            var surveyShips = fleet
                .Where(ship => string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase) && FleetRoles.CanOnlySurvey(ship))
                .ToList();
            var tier = PurchaseTier.Surveyor;
            if (surveyShips.Count > 0)
            {
                if (!drones.TryGetValue(systemSymbol, out var here)
                    || surveyShips.Count >= MiningPlanner.CountAreas(here.Map, surveyShips.Min(ship => ship.FuelCapacity), here.Waypoints))
                {
                    continue;
                }

                tier = PurchaseTier.SurveyorPerArea;
            }

            // D121: never where the shipyard has it SCARCE.
            var offer = shipyardList
                .Where(shipyard => shipyard.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))
                .SelectMany(shipyard => shipyard.Ships
                    .Where(ship => ship.Type.Equals(SurveyorShipType, StringComparison.OrdinalIgnoreCase) && ship.PurchasePrice > 0 && !ScarceShips.IsScarce(ship))
                    .Select(ship => (Shipyard: shipyard.WaypointSymbol, Price: ship.PurchasePrice)))
                .OrderBy(candidate => candidate.Price)
                .ThenBy(candidate => candidate.Shipyard, StringComparer.Ordinal)
                .ToList();
            if (offer.Count == 0)
            {
                logger.LogDebug("Survey plan: no shipyard in {SystemSymbol} with a known price for {ShipType} that isn't SCARCE.", systemSymbol, SurveyorShipType);
                continue;
            }

            return new PurchaseNeed(tier, SurveyorShipType, offer[0].Shipyard, offer[0].Price);
        }

        return PurchaseNeed.None;
    }

    /// <summary>
    /// The ships in a system that mine with the surveys: those that may mine. A drone still drifting to a market out of its
    /// CRUISE reach (D45) counts once it is there: until then it shows at that market, and surveys for it would expire
    /// during its drift of hours (B54).
    /// </summary>
    private async Task<IReadOnlyList<ShipModel>> MinersAsync(
        IReadOnlyList<ShipModel> fleet,
        FleetRoleBoard board,
        string systemSymbol,
        CancellationToken cancellationToken)
    {
        var miners = new List<ShipModel>();
        foreach (var ship in fleet.Where(ship => board.MinesForContract(ship)
            && string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase)))
        {
            if (await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken) is not MineAndSellGoal { Drifting: true } and not MineForShuttleGoal { Drifting: true })
            {
                miners.Add(ship);
            }
        }

        return miners;
    }

    /// <summary>
    /// Where each mining drone in a system works (D54): the market its trip sells at, a drone still drifting there included;
    /// between trips, where it is. A drone on other work doesn't count, and nor does an ore hound, which surveys for itself
    /// (slice 6.39, D120).
    /// </summary>
    private async Task<IReadOnlyList<string>> DroneWaypointsAsync(IReadOnlyList<ShipModel> fleet, string systemSymbol, CancellationToken cancellationToken)
    {
        var waypoints = new List<string>();
        foreach (var drone in fleet.Where(ship => FleetRoles.NeedsSurveyShips(ship)
            && string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase)))
        {
            var goal = await goals.GetActiveGoalAsync(drone.Symbol, cancellationToken);
            if (goal is MineAndSellGoal trip && trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                waypoints.Add(trip.SellWaypointSymbol);
            }
            else if (goal is MineForShuttleGoal job && job.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                // D83: a drone parked at a far asteroid works from its collection point's market's area.
                waypoints.Add(job.SellWaypointSymbol);
            }
            else if (goal is null || goal.Status is GoalStatus.Blocked or GoalStatus.Completed)
            {
                waypoints.Add(MiningPlanner.Position(drone));
            }
        }

        return waypoints;
    }

    /// <summary>The contract's ore, while the contract plan mines it in this system.</summary>
    private static IReadOnlyList<ContractOre> ContractOres(ContractMineralPlanState? contract, string systemSymbol)
        => contract is { Status: ContractMineralPlanStatus.Active }
            && contract.UnitsFulfilled < contract.UnitsRequired
            && !string.IsNullOrWhiteSpace(contract.SourceWaypoint)
            && contract.SourceWaypoint.StartsWith(systemSymbol + "-", StringComparison.OrdinalIgnoreCase)
                ? [new ContractOre(contract.TradeSymbol, contract.SourceWaypoint, contract.DestinationWaypoint)]
                : [];

    private static bool Targets(SurveyWaypointGoal goal, SurveyTarget target)
        => goal.TargetWaypointSymbol.Equals(target.AsteroidSymbol, StringComparison.OrdinalIgnoreCase)
            && goal.TargetDepositSymbol.Equals(target.Ore, StringComparison.OrdinalIgnoreCase);

    /// <summary>Records the targets. Only a change is written: the tick runs every 5 seconds.</summary>
    private async Task SaveStateAsync(IReadOnlyList<SurveyPlanTarget> targets, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await plans.GetAsync<SurveyPlanState>(PlanTypes.Survey, cancellationToken);
        if (existing is not null
            && JsonSerializer.Serialize(existing.Targets, CompareOptions) == JsonSerializer.Serialize(targets, CompareOptions))
        {
            return;
        }

        await plans.UpsertAsync(
            PlanTypes.Survey,
            new SurveyPlanState
            {
                PlanId = existing?.PlanId ?? Guid.NewGuid(),
                Targets = targets,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now,
            },
            cancellationToken);
    }
}
