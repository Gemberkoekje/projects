using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipUndockedMineEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IInOrbitCommandAcceptor inOrbitCommands) : IChainOfCommandEventHandler<ShipUndockedEvent>
{
    public int Priority => 200;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipUndockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        if (string.IsNullOrWhiteSpace(assignment.OriginWaypoint))
        {
            var noTargetIdle = new ShipIdleEvent(
                @event.ShipSymbol,
                "Mine assignment has no target waypoint.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(noTargetIdle);
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship?.WaypointSymbol?.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase) == true)
        {
            var alreadyThereIdle = new ShipIdleEvent(
                @event.ShipSymbol,
                "Mine ship is already at target waypoint.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(alreadyThereIdle);
        }

        await inOrbitCommands.NavigateAsync(@event.ShipSymbol, assignment.OriginWaypoint, cancellationToken);

        var now = TimeProvider.System.GetUtcNow();
        var movingEvent = new ShipMovingEvent(
            @event.ShipSymbol,
            @event.WaypointSymbol,
            assignment.OriginWaypoint,
            now,
            now.AddSeconds(1),
            0,
            @event.CorrelationId,
            @event.EventId,
            now);

        return ChainOfCommandHandlerResult.Handled(movingEvent);
    }
}
