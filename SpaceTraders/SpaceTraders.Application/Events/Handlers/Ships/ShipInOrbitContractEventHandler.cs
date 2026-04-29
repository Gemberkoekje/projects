using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles contract-role ships while in orbit.
/// </summary>
public sealed class ShipInOrbitContractEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IInOrbitCommandAcceptor inOrbitCommands,
    IMessageBus bus,
    ILogger<ShipInOrbitContractEventHandler> logger) : IChainOfCommandEventHandler<ShipInOrbitEvent>
{
    public int Priority => 65;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Contract", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ChainOfCommandHandlerResult.Handled(new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipInOrbitContractEventHandler),
                "IN_ORBIT",
                "UNKNOWN",
                "Contract in-orbit handler could not load ship state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow()));
        }

        if (!string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var destination = ResolveDestination(assignment, ship);
        if (string.IsNullOrWhiteSpace(destination))
        {
            await bus.InvokeAsync(new AssignShipCommand(
                @event.ShipSymbol,
                "Idle",
                SystemSymbol: @event.SystemSymbol,
                WaypointSymbol: @event.WaypointSymbol,
                CorrelationId: @event.CorrelationId,
                CausationId: @event.EventId), cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        if (ship.WaypointSymbol?.Equals(destination, StringComparison.OrdinalIgnoreCase) == true)
        {
            logger.LogInformation("{Handler}: ship {Ship} docking at {Waypoint} for contract assignment.", nameof(ShipInOrbitContractEventHandler), @event.ShipSymbol, destination);
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        logger.LogInformation("{Handler}: ship {Ship} navigating to {Waypoint} for contract assignment.", nameof(ShipInOrbitContractEventHandler), @event.ShipSymbol, destination);
        await inOrbitCommands.NavigateAsync(@event.ShipSymbol, destination, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }

    private static string ResolveDestination(
        SpaceTraders.Application.DTOs.ShipAssignmentDto assignment,
        SpaceTraders.Application.Ports.ShipModel ship)
    {
        var cargoUnits = ship.CargoInventory?
            .FirstOrDefault(i => i.Symbol.Equals(assignment.CargoSymbol ?? string.Empty, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        if (cargoUnits > 0 && !string.IsNullOrWhiteSpace(assignment.DestWaypoint))
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
