using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipIdleDockedEventHandler(
    IShipRepository ships,
    IShipAssignmentPlanner planner,
    IShipAssignmentRepository assignments,
    IMessageBus bus) : IChainOfCommandEventHandler<ShipIdleDockedEvent>
{
    public int Priority => 400;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipIdleDockedEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            var missingShipIdle = new ShipIdleEvent(
                @event.ShipSymbol,
                "Idle docked handler could not load ship state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(missingShipIdle);
        }

        if (!string.Equals(ship.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
        {
            var stateMismatch = new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipIdleDockedEventHandler),
                "DOCKED",
                ship.Status ?? "UNKNOWN",
                "Idle docked handler received ship outside docked state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(stateMismatch);
        }

        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || assignment.CompletedAt.HasValue || assignment.AssignmentType.Equals("Idle", StringComparison.OrdinalIgnoreCase))
        {
            var nextAssignment = await planner.PlanAsync(ship, cancellationToken);
            await bus.SendAsync(nextAssignment);
        }

        await bus.SendAsync(new OrbitShipCommand(@event.ShipSymbol));

        var now = TimeProvider.System.GetUtcNow();
        var undockedEvent = new ShipUndockedEvent(
            @event.ShipSymbol,
            ship.SystemSymbol ?? @event.SystemSymbol,
            ship.WaypointSymbol ?? @event.WaypointSymbol,
            @event.CorrelationId,
            @event.EventId,
            now);

        return ChainOfCommandHandlerResult.Handled(undockedEvent);
    }
}
