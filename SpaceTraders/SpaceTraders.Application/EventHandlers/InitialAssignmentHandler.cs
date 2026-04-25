using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.EventHandlers;

public sealed class InitialAssignmentHandler(
    IShipRepository ships,
    IShipAssignmentPlanner planner,
    IMessageBus bus,
    ILogger<InitialAssignmentHandler> logger)
{
    public async Task Handle(NewShipPurchasedEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            logger.LogWarning("New ship {Symbol}: ship data not found; assigning Idle.", @event.ShipSymbol);
            await bus.SendAsync(new Commands.Ships.AssignShipCommand(@event.ShipSymbol, "Idle"));
            return;
        }

        var assignment = await planner.PlanAsync(ship, cancellationToken);

        logger.LogInformation(
            "New ship {Symbol}: assigning {Type}{Details}.",
            @event.ShipSymbol,
            assignment.AssignmentType,
            DescribeAssignment(assignment));

        await bus.SendAsync(assignment);
    }

    private static string DescribeAssignment(Commands.Ships.AssignShipCommand assignment)
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
