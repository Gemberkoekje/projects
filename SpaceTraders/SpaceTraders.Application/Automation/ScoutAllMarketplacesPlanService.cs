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
    /// <param name="shipSymbol">The ship whose scout goal completed.</param>
    /// <param name="visitedWaypointSymbol">
    /// The waypoint the ship visited. Only a visit of the plan's current waypoint moves the plan on:
    /// a second completion of the same visit, or a goal for an earlier waypoint, changes nothing.
    /// </param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    Task AdvanceAsync(string shipSymbol, string visitedWaypointSymbol, CancellationToken cancellationToken = default);
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
                "{EventKind:l}: {Plan} plan for ship {ShipSymbol} with {WaypointCount} route waypoints; active assignment already exists, so no new assignment created.",
                JournalEvents.PlanStarted,
                AutomationPlan.Scout,
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
            "{EventKind:l}: {Plan} plan for ship {ShipSymbol} with {WaypointCount} route waypoints; first assignment targets {Destination}.",
            JournalEvents.PlanStarted,
            AutomationPlan.Scout,
            scoutShip.Symbol,
            route.Count,
            route[0]);
    }

    public async Task AdvanceAsync(string shipSymbol, string visitedWaypointSymbol, CancellationToken cancellationToken = default)
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
            // The ship's scout goal outlived its plan (a database from before B10 was fixed). Left in
            // place, the ship would complete it again on every tick.
            await goals.ClearActiveGoalAsync(shipSymbol, cancellationToken);
            logger.LogDebug(
                "Scout plan advance requested for ship {ShipSymbol} but plan status is {Status}; cleared its scout goal.",
                shipSymbol,
                plan.Status);
            return;
        }

        // The tick and an arrival can both run the ship's goal step when it docks, and both complete
        // the visit. Only the first may move the plan on: a second advance would skip the next stop
        // (B45).
        var currentWaypoint = plan.RouteWaypointSymbols[plan.CurrentRouteIndex];
        if (!string.Equals(currentWaypoint, visitedWaypointSymbol, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "Scout plan advance skipped for ship {ShipSymbol}: it visited {WaypointSymbol}, but the plan's current stop is {Destination}.",
                shipSymbol,
                visitedWaypointSymbol,
                currentWaypoint);
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

        // The plan is written before the assignment, and the resume check reads them the other way
        // round, so it never sees an assignment that is newer than the plan it compares it with (B45).
        if (nextIndex >= plan.RouteWaypointSymbols.Count)
        {
            var completed = plan with { Status = ScoutPlanStatus.Completed, UpdatedAt = now };
            await scoutPlans.UpsertAsync(completed, cancellationToken);
            await CompleteAssignmentAsync(currentAssignment, now, cancellationToken);

            // The last goal is done too: the ship is free for other work (B10).
            await goals.ClearActiveGoalAsync(shipSymbol, cancellationToken);

            logger.LogInformation(
                "{EventKind:l}: {Plan} plan for ship {ShipSymbol}: all {WaypointCount} waypoints visited.",
                JournalEvents.PlanCompleted,
                AutomationPlan.Scout,
                shipSymbol,
                plan.RouteWaypointSymbols.Count);
            return;
        }

        var advanced = plan with { CurrentRouteIndex = nextIndex, UpdatedAt = now };
        await scoutPlans.UpsertAsync(advanced, cancellationToken);
        await CompleteAssignmentAsync(currentAssignment, now, cancellationToken);

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
        ScoutAllMarketplacesPlanState existingPlan,
        CancellationToken cancellationToken)
    {
        if (existingPlan.Status != ScoutPlanStatus.Active)
        {
            return;
        }

        var currentAssignment = await assignments.FindAsync(existingPlan.ShipSymbol, cancellationToken);

        // Read the plan again, after the assignment: AdvanceAsync writes them the other way round.
        // An arrival can move the plan on while this runs, and a plan read before the assignment
        // could be from before that advance and the assignment from after it. That looked like a
        // missing assignment, and sent the ship back to the stop it had just visited (B45).
        var plan = await scoutPlans.GetAsync(cancellationToken);
        if (plan is null || plan.Status != ScoutPlanStatus.Active)
        {
            return;
        }

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

    private async Task CompleteAssignmentAsync(
        ShipAssignmentDto? assignment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (assignment is not null && !assignment.CompletedAt.HasValue)
        {
            await assignments.UpsertAsync(assignment with { CompletedAt = now }, cancellationToken);
        }
    }

    private async Task SetScoutGoalAsync(
        ShipAssignmentDto scoutAssignment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scoutAssignment.DestWaypoint))
        {
            throw new InvalidOperationException(
                $"Scout assignment for ship {scoutAssignment.ShipSymbol} has no destination waypoint.");
        }

        await goals.SetActiveGoalAsync(
            scoutAssignment.ShipSymbol,
            new ScoutWaypointGoal { TargetWaypointSymbol = scoutAssignment.DestWaypoint },
            cancellationToken);
    }
}
