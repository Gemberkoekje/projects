using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles <see cref="ShipDockedEvent"/> by running the assignment planner and dispatching
/// the resulting <see cref="AssignShipCommand"/>, which writes the new assignment and re-enters
/// the docked chain via <see cref="ShipAssignmentTypeSetEvent"/>.
/// </summary>
public sealed class ShipDockedIdleEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IShipAssignmentPlanner planner,
    IMessageBus bus,
    ILogger<ShipDockedIdleEventHandler> logger) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 500;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Idle", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ChainOfCommandHandlerResult.Handled(new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipDockedIdleEventHandler),
                "DOCKED",
                "UNKNOWN",
                "Idle docked handler could not load ship state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow()));
        }

        var command = await planner.PlanAsync(ship, cancellationToken);

        var commandWithContext = new AssignShipCommand(
            command.ShipSymbol,
            command.AssignmentType,
            command.OriginWaypoint,
            command.DestWaypoint,
            command.CargoSymbol,
            command.ContractId,
            ship.SystemSymbol ?? @event.SystemSymbol,
            ship.WaypointSymbol ?? @event.WaypointSymbol,
            @event.CorrelationId,
            @event.EventId,
            command.RequiredUnits);

        logger.LogInformation(
            "{Handler}: ship {Ship} planned as {AssignmentType}.",
            nameof(ShipDockedIdleEventHandler),
            @event.ShipSymbol,
            command.AssignmentType);

        await bus.InvokeAsync(commandWithContext, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }
}
