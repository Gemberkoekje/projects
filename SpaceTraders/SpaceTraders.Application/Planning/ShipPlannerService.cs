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

        var planner = planners.FirstOrDefault(p => p.CanPlan(ship, assignment));
        if (planner is null)
        {
            return ShipPlannerDecision.None(shipSymbol, $"No planner registered for assignment '{assignment.AssignmentType}'.");
        }

        var context = await BuildContextAsync(ship, assignment, cancellationToken);
        var decision = planner.Plan(ship, assignment, context);

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

        return new ShipPlannerContext
        {
            ActiveSurveyCount = activeSurveys,
            RecommendedFlightMode = recommendedFlightMode,
        };
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
        }
    }
}
