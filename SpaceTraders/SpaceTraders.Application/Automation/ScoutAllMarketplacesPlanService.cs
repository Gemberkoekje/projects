using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

public interface IScoutAllMarketplacesPlanService
{
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances the active scout plan to the next route waypoint after a scout assignment completes.
    /// Creates the next assignment when another waypoint remains, or marks the plan complete when
    /// all waypoints have been visited.
    /// </summary>
    Task AdvanceAsync(string shipSymbol, CancellationToken cancellationToken = default);
}

public sealed class ScoutAllMarketplacesPlanService(
    IScoutPlanRepository scoutPlans,
    IScoutShipSelectionService shipSelection,
    IScoutMarketplaceDiscoveryService marketplaceDiscovery,
    IMarketplaceRoutePlanner routePlanner,
    IShipAssignmentRepository assignments,
    IShipGoalRepository goals,
    ILogger<ScoutAllMarketplacesPlanService> logger) : IScoutAllMarketplacesPlanService
{
    private const string ScoutAssignmentType = "Scout";

    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var existingPlan = await scoutPlans.GetAsync(cancellationToken);
        if (existingPlan is not null)
        {
            await ResumeIfAssignmentMissingAsync(existingPlan, cancellationToken);
            return;
        }

        var scoutShip = await shipSelection.SelectAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(scoutShip.WaypointSymbol))
        {
            throw new InvalidOperationException(
                $"Scout plan bootstrap failed for ship {scoutShip.Symbol}: no current waypoint symbol is available.");
        }

        var marketplaces = await marketplaceDiscovery.DiscoverAsync(scoutShip, cancellationToken);
        if (marketplaces.Count == 0)
        {
            logger.LogWarning(
                "Scout plan bootstrap skipped: no marketplace waypoints discovered for ship {ShipSymbol} in system {SystemSymbol}.",
                scoutShip.Symbol,
                scoutShip.SystemSymbol ?? string.Empty);
            return;
        }

        var route = await routePlanner.BuildRouteAsync(scoutShip.WaypointSymbol, marketplaces, cancellationToken);
        if (route.Count == 0)
        {
            logger.LogWarning(
                "Scout plan bootstrap skipped: route planner returned no route for ship {ShipSymbol}.",
                scoutShip.Symbol);
            return;
        }

        var now = TimeProvider.System.GetUtcNow();
        var plan = new ScoutAllMarketplacesPlanState
        {
            PlanId = Guid.NewGuid(),
            ShipSymbol = scoutShip.Symbol,
            StartWaypointSymbol = scoutShip.WaypointSymbol,
            RouteWaypointSymbols = route,
            CurrentRouteIndex = 0,
            Status = ScoutPlanStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await scoutPlans.UpsertAsync(plan, cancellationToken);

        var activeAssignment = await assignments.FindAsync(scoutShip.Symbol, cancellationToken);
        if (activeAssignment is not null && !activeAssignment.CompletedAt.HasValue)
        {
            logger.LogInformation(
                "Scout plan bootstrapped for ship {ShipSymbol} with {WaypointCount} route waypoints; active assignment already exists, so no new assignment created.",
                scoutShip.Symbol,
                route.Count);
            return;
        }

        var firstAssignment = ShipAssignmentDto.CreateScout(
            shipSymbol: scoutShip.Symbol,
            destinationWaypoint: route[0],
            stepIndex: plan.CurrentRouteIndex,
            assignedAt: now);

        await assignments.UpsertAsync(firstAssignment, cancellationToken);
        await SetScoutGoalAsync(firstAssignment, cancellationToken);

        logger.LogInformation(
            "Scout plan bootstrapped for ship {ShipSymbol} with {WaypointCount} route waypoints; first assignment targets {Destination}.",
            scoutShip.Symbol,
            route.Count,
            route[0]);
    }

    public async Task AdvanceAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var plan = await scoutPlans.GetAsync(cancellationToken);
        if (plan is null)
        {
            logger.LogWarning(
                "Scout plan advance requested for ship {ShipSymbol} but no active plan found.",
                shipSymbol);
            return;
        }

        if (!string.Equals(plan.ShipSymbol, shipSymbol, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "Scout plan advance requested for ship {ShipSymbol} but plan belongs to ship {PlanShip}; ignoring.",
                shipSymbol,
                plan.ShipSymbol);
            return;
        }

        if (plan.Status != ScoutPlanStatus.Active)
        {
            logger.LogInformation(
                "Scout plan advance requested for ship {ShipSymbol} but plan status is {Status}; ignoring.",
                shipSymbol,
                plan.Status);
            return;
        }

        var nextIndex = plan.CurrentRouteIndex + 1;
        var now = TimeProvider.System.GetUtcNow();

        var currentAssignment = await assignments.FindAsync(shipSymbol, cancellationToken);

        // Guard: if a non-completed assignment already exists for the next step, this advance has
        // already been processed. Bail out to prevent double-advance or duplicate assignments.
        if (currentAssignment is not null
            && !currentAssignment.CompletedAt.HasValue
            && currentAssignment.StepIndex == nextIndex)
        {
            logger.LogDebug(
                "Scout plan advance skipped for ship {ShipSymbol}: assignment for step {NextIndex} is already active.",
                shipSymbol,
                nextIndex);
            return;
        }

        // Mark the current assignment as completed before creating the next one.
        if (currentAssignment is not null && !currentAssignment.CompletedAt.HasValue)
        {
            await assignments.UpsertAsync(currentAssignment with { CompletedAt = now }, cancellationToken);
        }

        if (nextIndex >= plan.RouteWaypointSymbols.Count)
        {
            var completed = plan with { Status = ScoutPlanStatus.Completed, UpdatedAt = now };
            await scoutPlans.UpsertAsync(completed, cancellationToken);

            logger.LogInformation(
                "Scout plan completed for ship {ShipSymbol}: all {WaypointCount} waypoints visited.",
                shipSymbol,
                plan.RouteWaypointSymbols.Count);
            return;
        }

        var advanced = plan with { CurrentRouteIndex = nextIndex, UpdatedAt = now };
        await scoutPlans.UpsertAsync(advanced, cancellationToken);

        var nextWaypoint = plan.RouteWaypointSymbols[nextIndex];
        logger.LogInformation(
            "Sending scout ship {ShipSymbol} to next waypoint {Destination} (step {Index}/{Total}).",
            shipSymbol,
            nextWaypoint,
            nextIndex + 1,
            plan.RouteWaypointSymbols.Count);

        var nextAssignment = ShipAssignmentDto.CreateScout(
            shipSymbol: shipSymbol,
            destinationWaypoint: nextWaypoint,
            stepIndex: nextIndex,
            assignedAt: now);

        await assignments.UpsertAsync(nextAssignment, cancellationToken);
        await SetScoutGoalAsync(nextAssignment, cancellationToken);

        logger.LogInformation(
            "Scout plan advanced for ship {ShipSymbol}: next assignment [{Index}/{Total}] targets {Destination}.",
            shipSymbol,
            nextIndex + 1,
            plan.RouteWaypointSymbols.Count,
            nextWaypoint);
    }

    /// <summary>
    /// Called on every bootstrap tick when a plan record already exists.
    /// If the plan is active and the ship has no active assignment for the current route index,
    /// re-creates the assignment so the ship resumes correctly after a process restart or crash.
    /// </summary>
    private async Task ResumeIfAssignmentMissingAsync(
        ScoutAllMarketplacesPlanState plan,
        CancellationToken cancellationToken)
    {
        if (plan.Status != ScoutPlanStatus.Active)
        {
            return;
        }

        var currentAssignment = await assignments.FindAsync(plan.ShipSymbol, cancellationToken);

        // An active assignment for the correct step is already present; nothing to do.
        if (currentAssignment is not null
            && !currentAssignment.CompletedAt.HasValue
            && currentAssignment.StepIndex == plan.CurrentRouteIndex)
        {
            return;
        }

        var expectedWaypoint = plan.RouteWaypointSymbols[plan.CurrentRouteIndex];
        var now = TimeProvider.System.GetUtcNow();

        var resumedAssignment = ShipAssignmentDto.CreateScout(
            shipSymbol: plan.ShipSymbol,
            destinationWaypoint: expectedWaypoint,
            stepIndex: plan.CurrentRouteIndex,
            assignedAt: now);

        await assignments.UpsertAsync(resumedAssignment, cancellationToken);
        await SetScoutGoalAsync(resumedAssignment, cancellationToken);

        logger.LogWarning(
            "Scout plan resumed for ship {ShipSymbol}: re-created missing assignment for step {Index} targeting {Destination}.",
            plan.ShipSymbol,
            plan.CurrentRouteIndex,
            expectedWaypoint);
    }

    private async Task SetScoutGoalAsync(
        ShipAssignmentDto assignment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assignment.DestWaypoint))
        {
            throw new InvalidOperationException(
                $"Scout assignment for ship {assignment.ShipSymbol} has no destination waypoint.");
        }

        await goals.SetActiveGoalAsync(
            assignment.ShipSymbol,
            new ScoutWaypointGoal { TargetWaypointSymbol = assignment.DestWaypoint },
            cancellationToken);
    }
}
