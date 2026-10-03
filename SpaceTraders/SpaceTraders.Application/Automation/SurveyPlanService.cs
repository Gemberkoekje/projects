using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
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
/// surveyor takes the best target no other surveyor works on, or the best one when all are taken. The plan also ends
/// the surveys that expired (<see cref="ISurveyKeeper.ExpireAsync"/>), for the survey dashboard.
/// <para>
/// With the role board on, it buys a designated surveyor (D47): a <c>SHIP_SURVEYOR</c> for each system with mining drones
/// and no ship that can only survey, first in the order ships are bought in after the contract's drone (D43). The board
/// gives it the survey role (D38), which frees the command ship for what pays it most.
/// </para>
/// <para>
/// A ship that can only survey works where most mining drones work (D54, <see cref="MiningPlanner.TryFindBusierArea"/>):
/// when an area out of its CRUISE reach has more drones than its own, it drifts there (<see cref="MoveToWaypointGoal"/>)
/// before it surveys again; a tie keeps it where it is.
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
        var withAssignment = (await assignments.GetAllActiveAsync(cancellationToken))
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var surveying = new Dictionary<string, SurveyWaypointGoal>(StringComparer.OrdinalIgnoreCase);
        var free = new List<ShipModel>();
        var gathering = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ship in fleet.Where(board.IsSurveyor))
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
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
        var targets = new List<SurveyPlanTarget>();
        foreach (var system in fleet
            .Where(ship => board.IsSurveyor(ship) && !string.IsNullOrWhiteSpace(ship.SystemSymbol))
            .GroupBy(ship => ship.SystemSymbol!, StringComparer.OrdinalIgnoreCase))
        {
            var context = await miningContexts.ReadAsync(system.Key, cancellationToken);
            var miners = await MinersAsync(fleet, board, system.Key, cancellationToken);
            var systemTargets = MiningPlanner.SurveyTargets(context, ContractOres(contract, system.Key), miners, stock);
            var freeHere = free.Where(ship => string.Equals(ship.SystemSymbol, system.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            var droneWaypoints = freeHere.Any(FleetRoles.CanOnlySurvey)
                ? await DroneWaypointsAsync(fleet, system.Key, cancellationToken)
                : [];

            foreach (var surveyor in freeHere)
            {
                // D54: a ship that can only survey works where most drones mine, and drifts to an area that has more.
                if (FleetRoles.CanOnlySurvey(surveyor) && MiningPlanner.TryFindBusierArea(context.Map, surveyor, droneWaypoints, out var move))
                {
                    await goals.SetActiveGoalAsync(
                        surveyor.Symbol,
                        new MoveToWaypointGoal { TargetWaypointSymbol = move.MarketSymbol, Drifting = true },
                        cancellationToken);
                    logger.LogInformation(
                        "Survey plan: ship {ShipSymbol} moves to {WaypointSymbol}, where {Drones} mining drones work, against {OwnDrones} in its own area (D54).",
                        surveyor.Symbol,
                        move.MarketSymbol,
                        move.Drones,
                        move.OwnDrones);
                    continue;
                }

                var reachable = systemTargets
                    .Where(target => target.NeedsSurvey && MiningPlanner.CanReach(context.Map, surveyor, target.AsteroidSymbol))
                    .ToList();
                var beyondStock = reachable.Count == 0 && FleetRoles.CanOnlySurvey(surveyor);
                if (beyondStock)
                {
                    // D52: "A (single role) surveyor which is idle is allowed to keep surveying, starting with whichever ore is
                    // lowest": with every ore it reaches at its stock, the ore with the fewest usable surveys first.
                    reachable = [.. systemTargets
                        .Where(target => MiningPlanner.CanReach(context.Map, surveyor, target.AsteroidSymbol))
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

            // The surveyors that can reach each asteroid: only for those is a target work the plan could give (B55).
            var reachedBy = systemTargets
                .Select(target => target.AsteroidSymbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    asteroid => asteroid,
                    asteroid => (IReadOnlyList<string>)[.. system.Where(surveyor => MiningPlanner.CanReach(context.Map, surveyor, asteroid)).Select(surveyor => surveyor.Symbol).Order(StringComparer.Ordinal)],
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

        await BuySurveyorAsync(fleet, board, cancellationToken);
        await SaveStateAsync(targets, now, cancellationToken);
    }

    /// <summary>
    /// Says what the plan would buy (<see cref="SurveyorNeedAsync"/>), and buys it when the order ships are bought in lets
    /// it (D43, <see cref="IPurchaseOrder"/>), within the credit reserve. With the role board off the command ship surveys
    /// (D20), and no surveyor is bought.
    /// </summary>
    private async Task BuySurveyorAsync(IReadOnlyList<ShipModel> fleet, FleetRoleBoard board, CancellationToken cancellationToken)
    {
        var need = board.RolesOn ? await SurveyorNeedAsync(fleet, cancellationToken) : PurchaseNeed.None;
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
    /// A designated surveyor (D47) for the first system, by symbol, with a mining drone (<see cref="FleetRoles.IsMiningDrone"/>)
    /// and no ship that can only survey, at the system's shipyard that sells a <c>SHIP_SURVEYOR</c> for the least; none when
    /// no shipyard there is known to sell one.
    /// </summary>
    private async Task<PurchaseNeed> SurveyorNeedAsync(IReadOnlyList<ShipModel> fleet, CancellationToken cancellationToken)
    {
        var shipyardList = await shipyards.GetAllAsync(cancellationToken);
        foreach (var systemSymbol in fleet
            .Where(FleetRoles.IsMiningDrone)
            .Select(ship => ship.SystemSymbol)
            .OfType<string>()
            .Where(system => system.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal))
        {
            if (fleet.Any(ship => string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase)
                && FleetRoles.CanOnlySurvey(ship)))
            {
                continue;
            }

            var offer = shipyardList
                .Where(shipyard => shipyard.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))
                .SelectMany(shipyard => shipyard.Ships
                    .Where(ship => ship.Type.Equals(SurveyorShipType, StringComparison.OrdinalIgnoreCase) && ship.PurchasePrice > 0)
                    .Select(ship => (Shipyard: shipyard.WaypointSymbol, Price: ship.PurchasePrice)))
                .OrderBy(candidate => candidate.Price)
                .ThenBy(candidate => candidate.Shipyard, StringComparer.Ordinal)
                .ToList();
            if (offer.Count == 0)
            {
                logger.LogDebug("Survey plan: no shipyard in {SystemSymbol} with a known price for {ShipType}.", systemSymbol, SurveyorShipType);
                continue;
            }

            return new PurchaseNeed(PurchaseTier.Surveyor, SurveyorShipType, offer[0].Shipyard, offer[0].Price);
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
            if (await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken) is not MineAndSellGoal { Drifting: true })
            {
                miners.Add(ship);
            }
        }

        return miners;
    }

    /// <summary>
    /// Where each mining drone in a system works (D54): the market its trip sells at, a drone still drifting there included;
    /// between trips, where it is. A drone on other work doesn't count.
    /// </summary>
    private async Task<IReadOnlyList<string>> DroneWaypointsAsync(IReadOnlyList<ShipModel> fleet, string systemSymbol, CancellationToken cancellationToken)
    {
        var waypoints = new List<string>();
        foreach (var drone in fleet.Where(ship => FleetRoles.IsMiningDrone(ship)
            && string.Equals(ship.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase)))
        {
            var goal = await goals.GetActiveGoalAsync(drone.Symbol, cancellationToken);
            if (goal is MineAndSellGoal trip && trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed)
            {
                waypoints.Add(trip.SellWaypointSymbol);
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
