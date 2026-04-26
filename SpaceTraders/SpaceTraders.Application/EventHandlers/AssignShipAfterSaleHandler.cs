using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.EventHandlers;

public sealed class ShipAssignmentOrchestratorHandler(
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    IShipAssignmentPlanner planner,
    IMessageBus bus,
    ILogger<ShipAssignmentOrchestratorHandler> logger)
{
    public async Task Handle(ShipBecameIdleEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            logger.LogWarning("Ship {Symbol}: ship data not found; setting to Idle.", @event.ShipSymbol);
            await bus.SendAsync(new AssignShipCommand(@event.ShipSymbol, "Idle"));
            return;
        }

        var currentAssignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (HasActiveNonIdleAssignment(currentAssignment))
        {
            logger.LogInformation(
                "Ship {Symbol} became idle because {Reason}, but still has active {Type} assignment; skipping replanning.",
                @event.ShipSymbol,
                @event.Reason,
                currentAssignment!.AssignmentType);
            return;
        }

        var assignment = await planner.PlanAsync(ship, cancellationToken);

        logger.LogInformation(
            "Ship {Symbol} became idle because {Reason}; assigning {Type}{Details}.",
            @event.ShipSymbol,
            @event.Reason,
            assignment.AssignmentType,
            DescribeAssignment(assignment));

        await bus.SendAsync(assignment);
    }

    public async Task Handle(ShipArrivedAtWaypointEvent @event, CancellationToken cancellationToken)
    {
        var currentAssignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (!HasActiveNonIdleAssignment(currentAssignment))
        {
            logger.LogInformation(
                "Ship {Symbol} arrived at {Waypoint} without active non-idle assignment; publishing ShipBecameIdleEvent.",
                @event.ShipSymbol,
                @event.Waypoint.Value);
            await bus.PublishAsync(new ShipBecameIdleEvent(@event.ShipSymbol, $"Arrived at {@event.Waypoint.Value} without active assignment"));
            return;
        }

        logger.LogInformation(
            "Ship {Symbol} arrived at {Waypoint} for active {Type} assignment; orchestrator exiting early.",
            @event.ShipSymbol,
            @event.Waypoint.Value,
            currentAssignment!.AssignmentType);
    }

    private static bool HasActiveNonIdleAssignment(ShipAssignmentDto? assignment)
        => assignment is not null &&
           !assignment.CompletedAt.HasValue &&
           !assignment.AssignmentType.Equals("Idle", StringComparison.OrdinalIgnoreCase);

    private static string DescribeAssignment(AssignShipCommand assignment)
    {
        if (assignment.AssignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase))
        {
            return $" {assignment.CargoSymbol} {assignment.OriginWaypoint} → {assignment.DestWaypoint}";
        }

        if (assignment.AssignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase))
        {
            return $" {assignment.OriginWaypoint} → {assignment.DestWaypoint}";
        }

        if (assignment.AssignmentType.Equals("Scout", StringComparison.OrdinalIgnoreCase))
        {
            return $" {assignment.OriginWaypoint}";
        }

        return string.Empty;
    }
}

public sealed class ShipAssignmentCompletionHandler(IMessageBus bus)
{
    public async Task Handle(ShipAssignmentCompletedEvent @event, CancellationToken cancellationToken)
    {
        await bus.PublishAsync(
            new ShipBecameIdleEvent(@event.ShipSymbol, $"Completed {@event.CompletedType} assignment"));
    }
}

public sealed class ShipAssignmentProgressHandler(
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    ITradeOpportunityRepository tradeOpportunities,
    ISettingsRepository settings,
    IMarketRepository markets,
    IMessageBus bus,
    ILogger<ShipAssignmentProgressHandler> logger)
{
    public async Task Handle(ShipAssignedEvent @event, CancellationToken cancellationToken)
    {
        if (@event.AssignmentType.Equals("Idle", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Ship {Symbol} assigned to Idle; waiting for orchestrator trigger.", @event.ShipSymbol);
            return;
        }

        await AdvanceActiveAssignmentAsync(@event.ShipSymbol, "assignment", cancellationToken);
    }

    public async Task Handle(ShipArrivedAtWaypointEvent @event, CancellationToken cancellationToken)
    {
        await AdvanceActiveAssignmentAsync(@event.ShipSymbol, $"arrival at {@event.Waypoint.Value}", cancellationToken);
    }

    private async Task AdvanceActiveAssignmentAsync(string shipSymbol, string trigger, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(shipSymbol, cancellationToken);
        if (ship is null)
        {
            logger.LogWarning("Ship {Symbol}: ship data not found while handling {Trigger}.", shipSymbol, trigger);
            return;
        }

        var assignment = await assignments.FindAsync(shipSymbol, cancellationToken);
        if (assignment is null || assignment.CompletedAt.HasValue)
        {
            logger.LogDebug("Ship {Symbol}: no active assignment to advance for {Trigger}.", shipSymbol, trigger);
            return;
        }

        if (assignment.AssignmentType.Equals("Idle", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Ship {Symbol}: idle assignment ignored for {Trigger}.", shipSymbol, trigger);
            return;
        }

        logger.LogInformation(
            "Ship {Symbol}: advancing {Type} assignment at step {Step} due to {Trigger}.",
            shipSymbol,
            assignment.AssignmentType,
            assignment.StepIndex,
            trigger);

        await AdvanceAssignmentAsync(ship, assignment, cancellationToken);
    }

    private async Task AdvanceAssignmentAsync(ShipModel ship, ShipAssignmentDto assignment, CancellationToken cancellationToken)
    {
        if (assignment.AssignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase))
        {
            var machine = new TradeStateMachine(ship, assignment, assignments, tradeOpportunities, settings, markets, bus, logger);
            await machine.AdvanceAsync(cancellationToken);
            return;
        }

        if (assignment.AssignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase))
        {
            var machine = new MineStateMachine(ship, assignment, assignments, bus, logger);
            await machine.AdvanceAsync(cancellationToken);
            return;
        }

        if (assignment.AssignmentType.Equals("Scout", StringComparison.OrdinalIgnoreCase))
        {
            var machine = new ScoutStateMachine(ship, assignment, assignments, bus, logger);
            await machine.AdvanceAsync(cancellationToken);
            return;
        }

        logger.LogDebug("Ship {Symbol}: unsupported assignment type {Type}; skipping.", ship.Symbol, assignment.AssignmentType);
    }
}
