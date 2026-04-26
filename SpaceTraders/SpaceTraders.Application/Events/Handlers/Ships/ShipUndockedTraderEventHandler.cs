using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipUndockedTraderEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IInOrbitCommandAcceptor inOrbitCommands) : IChainOfCommandEventHandler<ShipUndockedEvent>
{
    public int Priority => 150;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipUndockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            var missingShipMismatch = new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipUndockedTraderEventHandler),
                "IN_ORBIT",
                "UNKNOWN",
                "Trade undocked handler could not load ship state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(missingShipMismatch);
        }

        if (!string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            var stateMismatch = new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipUndockedTraderEventHandler),
                "IN_ORBIT",
                ship.Status ?? "UNKNOWN",
                "Trade undocked handler received ship outside orbit state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(stateMismatch);
        }

        var destination = ResolveDestination(assignment, ship);
        if (string.IsNullOrWhiteSpace(destination))
        {
            var noDestinationIdle = new ShipIdleEvent(
                @event.ShipSymbol,
                "Trade assignment has no actionable destination waypoint.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(noDestinationIdle);
        }

        if (ship.WaypointSymbol?.Equals(destination, StringComparison.OrdinalIgnoreCase) == true)
        {
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);

            var nowDocked = TimeProvider.System.GetUtcNow();
            var dockedEvent = new ShipDockedEvent(
                @event.ShipSymbol,
                ship.SystemSymbol ?? @event.SystemSymbol,
                destination,
                @event.CorrelationId,
                @event.EventId,
                nowDocked);

            return ChainOfCommandHandlerResult.Handled(dockedEvent);
        }

        await inOrbitCommands.NavigateAsync(@event.ShipSymbol, destination, cancellationToken);

        var now = TimeProvider.System.GetUtcNow();
        var movingEvent = new ShipMovingEvent(
            @event.ShipSymbol,
            ship.WaypointSymbol ?? @event.WaypointSymbol,
            destination,
            now,
            now.AddSeconds(1),
            0,
            @event.CorrelationId,
            @event.EventId,
            now);

        return ChainOfCommandHandlerResult.Handled(movingEvent);
    }

    private static string ResolveDestination(SpaceTraders.Application.DTOs.ShipAssignmentDto assignment, SpaceTraders.Application.Ports.ShipModel ship)
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
}
