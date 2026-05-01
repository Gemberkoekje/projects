using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using Wolverine;

namespace SpaceTraders.Application.Planning;

/// <summary>
/// Phase 3: ship planner boundary entry point.
/// Loads a ship and its assignment, picks the matching planner, builds the planner context,
/// and translates the resulting <see cref="ShipPlannerDecision"/> into exactly one command
/// (or none). All side effects flow through the existing command acceptors so this boundary
/// can coexist with the chain-of-command handlers during migration.
/// </summary>
public interface IShipPlannerService
{
    /// <summary>
    /// Plan and execute the next command for the supplied ship, if any. Returns the planner
    /// decision so callers can log/inspect the chosen action without re-executing it.
    /// </summary>
    Task<ShipPlannerDecision> PlanAndExecuteAsync(string shipSymbol, CancellationToken cancellationToken);
}

public sealed class ShipPlannerService(
    IEnumerable<IShipPlanner> planners,
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    ISurveyRepository surveys,
    INavigationPlanningService navigationPlanning,
    IConstructionRepository constructions,
    IWaypointRepository waypoints,
    IMarketRepository markets,
    IContractRepository contracts,
    ISettingsRepository settings,
    IFleetMaintenancePlanner maintenance,
    IWaypointVisitService waypointVisit,
    IMarketRefreshService marketRefresh,
    IShipyardRefreshService shipyardRefresh,
    IInOrbitCommandAcceptor inOrbitCommands,
    IDockedCommandAcceptor dockedCommands,
    IMessageBus bus,
    ILogger<ShipPlannerService> logger) : IShipPlannerService
{
    public async Task<ShipPlannerDecision> PlanAndExecuteAsync(string shipSymbol, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(shipSymbol, cancellationToken);
        if (ship is null)
        {
            logger.LogWarning("ShipPlannerService: cannot plan for {Ship}; ship state not found.", shipSymbol);
            return ShipPlannerDecision.None(shipSymbol, "Ship state not found.");
        }

        var assignment = await assignments.FindAsync(shipSymbol, cancellationToken);
        if (assignment is null)
        {
            return ShipPlannerDecision.None(shipSymbol, "Ship has no active assignment.");
        }

        var matchingPlanners = planners.Where(p => p.CanPlan(ship, assignment)).ToArray();
        if (matchingPlanners.Length == 0)
        {
            return ShipPlannerDecision.None(shipSymbol, $"No planner registered for assignment '{assignment.AssignmentType}'.");
        }

        var context = await BuildContextAsync(ship, assignment, cancellationToken);

        // Phase 4b: planners are evaluated in registration order. Cross-cutting planners
        // (fuel recovery, maintenance) are registered first so they can preempt the role
        // planner; if they return None the next matching planner is consulted.
        ShipPlannerDecision decision = ShipPlannerDecision.None(shipSymbol, "No planner produced a command.");
        foreach (var planner in matchingPlanners)
        {
            var candidate = planner.Plan(ship, assignment, context);
            if (candidate.Kind != ShipPlannerCommandKind.None)
            {
                decision = candidate;
                break;
            }

            decision = candidate;
        }

        await ExecuteAsync(ship, assignment, decision, cancellationToken);
        return decision;
    }

    private async Task<ShipPlannerContext> BuildContextAsync(
        ShipModel ship,
        ShipAssignmentDto assignment,
        CancellationToken cancellationToken)
    {
        var activeSurveys = ship.HasSurveyEquipment && !string.IsNullOrWhiteSpace(ship.WaypointSymbol)
            ? (await surveys.GetActiveByWaypointAsync(ship.WaypointSymbol, cancellationToken)).Count
            : 0;

        var navigationTarget = ResolveNavigationTarget(ship, assignment);
        var recommendedFlightMode = string.Empty;
        if (!string.IsNullOrWhiteSpace(navigationTarget) && ship.FuelCapacity > 0)
        {
            var plan = await navigationPlanning.BuildPlanAsync(ship, navigationTarget, cancellationToken);
            if (plan is not null && !string.IsNullOrWhiteSpace(plan.RecommendedFlightMode))
            {
                recommendedFlightMode = plan.RecommendedFlightMode;
            }
        }

        var fuelMarket = string.Empty;
        var currentSellsFuel = false;
        if (!string.IsNullOrWhiteSpace(ship.SystemSymbol))
        {
            (fuelMarket, currentSellsFuel) = await ResolveFuelContextAsync(ship, cancellationToken);
        }

        FleetMaintenanceDecision? maintenanceDecision = null;
        if (ship.LocalStatus == Domain.Enums.ShipLocalStatus.Docked)
        {
            maintenanceDecision = await maintenance.DecideAsync(ship, assignment.AssignmentType, cancellationToken);
        }

        var constructionComplete = false;
        if (assignment.AssignmentType.Equals("Builder", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(assignment.DestWaypoint))
        {
            var site = await constructions.FindAsync(assignment.DestWaypoint, cancellationToken);
            constructionComplete = site is not null && site.IsComplete;
        }

        // Phase 6.5b: load docked context for role planners that now handle docked state.
        IReadOnlyList<ContractDto> activeContracts = [];
        Interfaces.MarketSnapshot? currentMarketSnapshot = null;
        var miningReserveHydrocarbonUnits = 0;
        var miningMinimumSellPrice = 0;
        var miningJettisonLowValueWhenFull = false;

        if (ship.LocalStatus == Domain.Enums.ShipLocalStatus.Docked)
        {
            activeContracts = await contracts.GetActiveAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(ship.WaypointSymbol))
            {
                currentMarketSnapshot = await markets.FindSnapshotByWaypointAsync(ship.WaypointSymbol, cancellationToken);
            }

            if (assignment.AssignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase)
                || assignment.AssignmentType.Equals("Siphon", StringComparison.OrdinalIgnoreCase))
            {
                miningReserveHydrocarbonUnits = await settings.GetAsync<int>("Mining.ReserveHydrocarbonUnits", cancellationToken);
                miningMinimumSellPrice = await settings.GetAsync<int>("Mining.MinimumSellPriceToKeepCargo", cancellationToken);
                miningJettisonLowValueWhenFull = await settings.GetAsync<bool>("Mining.JettisonLowValueWhenFull", cancellationToken);
            }
        }

        return new ShipPlannerContext
        {
            ActiveSurveyCount = activeSurveys,
            RecommendedFlightMode = recommendedFlightMode,
            FuelMarketWaypoint = fuelMarket,
            CurrentWaypointSellsFuel = currentSellsFuel,
            Maintenance = maintenanceDecision,
            ConstructionComplete = constructionComplete,
            ActiveContracts = activeContracts,
            CurrentMarketSnapshot = currentMarketSnapshot,
            MiningReserveHydrocarbonUnits = miningReserveHydrocarbonUnits,
            MiningMinimumSellPrice = miningMinimumSellPrice,
            MiningJettisonLowValueWhenFull = miningJettisonLowValueWhenFull,
        };
    }

    private async Task<(string FuelMarket, bool CurrentSellsFuel)> ResolveFuelContextAsync(
        ShipModel ship,
        CancellationToken cancellationToken)
    {
        var systemWaypoints = await waypoints.GetBySystemAsync(ship.SystemSymbol!, cancellationToken);
        if (systemWaypoints.Count == 0)
        {
            return (string.Empty, false);
        }

        var marketWaypoints = systemWaypoints
            .Where(w => w.HasMarket)
            .Select(w => w.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (marketWaypoints.Count == 0)
        {
            return (string.Empty, false);
        }

        var snapshots = await markets.GetAllSnapshotsAsync(cancellationToken);
        var fuelSellingMarkets = snapshots
            .Where(s => s.SystemSymbol.Equals(ship.SystemSymbol, StringComparison.OrdinalIgnoreCase))
            .Where(s => marketWaypoints.Contains(s.WaypointSymbol))
            .Where(s => s.TradeGoods.Any(g => g.Symbol.Equals("FUEL", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var fuelMarket = fuelSellingMarkets.FirstOrDefault()?.WaypointSymbol ?? string.Empty;
        var currentSellsFuel = !string.IsNullOrWhiteSpace(ship.WaypointSymbol)
            && fuelSellingMarkets.Any(s => s.WaypointSymbol.Equals(ship.WaypointSymbol, StringComparison.OrdinalIgnoreCase));

        return (fuelMarket, currentSellsFuel);
    }

    private static string ResolveNavigationTarget(ShipModel ship, ShipAssignmentDto assignment)
    {
        if (ship.CargoCurrent > 0 && !string.IsNullOrWhiteSpace(assignment.DestWaypoint))
        {
            return assignment.DestWaypoint;
        }

        if (!string.IsNullOrWhiteSpace(assignment.OriginWaypoint))
        {
            return assignment.OriginWaypoint;
        }

        return assignment.DestWaypoint ?? string.Empty;
    }

    private async Task ExecuteAsync(
        ShipModel ship,
        ShipAssignmentDto assignment,
        ShipPlannerDecision decision,
        CancellationToken cancellationToken)
    {
        switch (decision.Kind)
        {
            case ShipPlannerCommandKind.None:
                logger.LogDebug("ShipPlannerService: no action for {Ship}: {Reason}", decision.ShipSymbol, decision.Reason);
                return;

            case ShipPlannerCommandKind.Dock:
                await inOrbitCommands.DockAsync(decision.ShipSymbol, cancellationToken);
                return;

            case ShipPlannerCommandKind.Orbit:
                await dockedCommands.OrbitAsync(decision.ShipSymbol, cancellationToken);
                return;

            case ShipPlannerCommandKind.Navigate:
                await inOrbitCommands.NavigateAsync(decision.ShipSymbol, decision.DestinationWaypoint, cancellationToken);
                return;

            case ShipPlannerCommandKind.Extract:
                await inOrbitCommands.ExtractAsync(decision.ShipSymbol, cancellationToken);
                return;

            case ShipPlannerCommandKind.Survey:
                await inOrbitCommands.SurveyAsync(decision.ShipSymbol, cancellationToken);
                return;

            case ShipPlannerCommandKind.Siphon:
                await inOrbitCommands.SiphonAsync(decision.ShipSymbol, cancellationToken);
                return;

            case ShipPlannerCommandKind.PatchFlightMode:
                await bus.InvokeAsync(new PatchShipNavCommand(decision.ShipSymbol, decision.FlightMode), cancellationToken);
                return;

            case ShipPlannerCommandKind.AssignIdle:
                await bus.InvokeAsync(new AssignShipCommand(
                    decision.ShipSymbol,
                    "Idle",
                    SystemSymbol: ship.SystemSymbol ?? string.Empty,
                    WaypointSymbol: ship.WaypointSymbol ?? string.Empty), cancellationToken);
                return;

            case ShipPlannerCommandKind.SupplyConstruction:
                await inOrbitCommands.SupplyConstructionAsync(
                    decision.ShipSymbol,
                    decision.SystemSymbol,
                    decision.WaypointSymbol,
                    decision.TradeSymbol,
                    decision.Units,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    cancellationToken);
                return;

            case ShipPlannerCommandKind.Refuel:
                await dockedCommands.RefuelAsync(decision.ShipSymbol, fromCargo: false, cancellationToken);
                return;

            case ShipPlannerCommandKind.BuyCargo:
                await dockedCommands.BuyCargoAsync(decision.ShipSymbol, decision.TradeSymbol, decision.Units, cancellationToken);
                return;

            case ShipPlannerCommandKind.Repair:
                await dockedCommands.RepairAsync(decision.ShipSymbol, cancellationToken);
                return;

            case ShipPlannerCommandKind.Scrap:
                await dockedCommands.ScrapAsync(decision.ShipSymbol, cancellationToken);
                return;

            case ShipPlannerCommandKind.SellCargo:
                await dockedCommands.SellCargoAsync(decision.ShipSymbol, decision.TradeSymbol, decision.Units, cancellationToken);
                return;

            case ShipPlannerCommandKind.DeliverContractCargo:
                await dockedCommands.DeliverContractAsync(
                    decision.ContractId,
                    decision.ShipSymbol,
                    decision.TradeSymbol,
                    decision.Units,
                    decision.WaypointSymbol,
                    cancellationToken);
                return;

            case ShipPlannerCommandKind.RefuelFromCargo:
                await dockedCommands.RefuelAsync(decision.ShipSymbol, fromCargo: true, cancellationToken);
                return;

            case ShipPlannerCommandKind.JettisonCargo:
                await bus.InvokeAsync(new JettisonCargoCommand(decision.ShipSymbol, decision.TradeSymbol, decision.Units), cancellationToken);
                return;

            case ShipPlannerCommandKind.RefreshScoutData:
                var waypoint = ship.WaypointSymbol ?? string.Empty;
                await waypointVisit.MarkVisitedAsync(waypoint, cancellationToken);
                await marketRefresh.RefreshIfApplicableAsync(waypoint, cancellationToken);
                await shipyardRefresh.RefreshIfApplicableAsync(waypoint, cancellationToken);
                var isMarketProbeAtTarget = assignment.AssignmentType.Equals("MarketProbe", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
                    && waypoint.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase);
                if (!isMarketProbeAtTarget)
                {
                    await dockedCommands.OrbitAsync(decision.ShipSymbol, cancellationToken);
                }
                return;
        }
    }
}
