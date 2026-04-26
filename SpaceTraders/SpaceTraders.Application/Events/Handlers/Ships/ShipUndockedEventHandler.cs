using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipUndockedEventHandler(
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    IShipAssignmentPlanner planner) : IChainOfCommandEventHandler<ShipUndockedEvent>
{
    public int Priority => 1000;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipUndockedEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            var missingShipIdle = new ShipIdleEvent(
                @event.ShipSymbol,
                "Ship not found for fallback undocked handling.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(missingShipIdle);
        }

        var assignment = await planner.PlanAsync(ship, cancellationToken);
        var role = MapRole(assignment.AssignmentType);

        if (role == ShipRole.None)
        {
            var nextEvent = new ShipIdleEvent(
                @event.ShipSymbol,
                "No specialized undocked handler matched.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(nextEvent);
        }

        var now = TimeProvider.System.GetUtcNow();
        var assignmentDto = new ShipAssignmentDto(
            @event.ShipSymbol,
            assignment.AssignmentType,
            assignment.OriginWaypoint,
            assignment.DestWaypoint,
            assignment.CargoSymbol,
            assignment.ContractId,
            0,
            now,
            null,
            0);

        await assignments.UpsertAsync(assignmentDto, cancellationToken);

        var roleSetEvent = new ShipRoleSetEvent(
            @event.ShipSymbol,
            role,
            @event.CorrelationId,
            @event.EventId,
            now);

        return ChainOfCommandHandlerResult.Handled(roleSetEvent);
    }

    private static ShipRole MapRole(string assignmentType)
    {
        if (assignmentType.Equals("Scout", StringComparison.OrdinalIgnoreCase))
        {
            return ShipRole.Explorer;
        }

        if (assignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase))
        {
            return ShipRole.Excavator;
        }

        if (assignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase))
        {
            return ShipRole.Hauler;
        }

        return ShipRole.None;
    }
}
