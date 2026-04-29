using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles Builder-role ships that are docked at their origin waypoint.
/// Buys the required construction material when cargo is short, then orbits
/// so the in-orbit handler can navigate to the construction site and supply it.
/// When the construction site reports completion the ship is reassigned to Idle.
/// </summary>
public sealed class ShipDockedBuilderEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IConstructionRepository constructions,
    IDockedCommandAcceptor dockedCommands,
    IMessageBus bus,
    ILogger<ShipDockedBuilderEventHandler> logger) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 90;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Builder", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        if (string.IsNullOrWhiteSpace(assignment.DestWaypoint) || string.IsNullOrWhiteSpace(assignment.CargoSymbol))
        {
            logger.LogWarning("{Handler}: ship {Ship} Builder assignment missing DestWaypoint or CargoSymbol; reassigning Idle.", nameof(ShipDockedBuilderEventHandler), @event.ShipSymbol);
            await bus.InvokeAsync(new AssignShipCommand(
                @event.ShipSymbol,
                "Idle",
                SystemSymbol: @event.SystemSymbol,
                WaypointSymbol: @event.WaypointSymbol,
                CorrelationId: @event.CorrelationId,
                CausationId: @event.EventId), cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        // Derive system from the construction waypoint symbol (e.g. X1-AB-CC00 → X1-AB)
        var site = await constructions.FindAsync(assignment.DestWaypoint, cancellationToken);

        if (site is not null && site.IsComplete)
        {
            logger.LogInformation("{Handler}: construction site {Waypoint} is complete; reassigning ship {Ship} to Idle.", nameof(ShipDockedBuilderEventHandler), assignment.DestWaypoint, @event.ShipSymbol);
            await bus.InvokeAsync(new AssignShipCommand(
                @event.ShipSymbol,
                "Idle",
                SystemSymbol: @event.SystemSymbol,
                WaypointSymbol: @event.WaypointSymbol,
                CorrelationId: @event.CorrelationId,
                CausationId: @event.EventId), cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        // Check how many units of the required material are already in cargo
        var cargoUnits = ship.CargoInventory?
            .FirstOrDefault(c => c.Symbol.Equals(assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        // Supply run completed — ship has returned to origin docked; hand back to role planner
        if (assignment.SupplyCompleted)
        {
            logger.LogInformation("{Handler}: ship {Ship} completed supply run; reassigning to Idle.", nameof(ShipDockedBuilderEventHandler), @event.ShipSymbol);
            await bus.InvokeAsync(new AssignShipCommand(
                @event.ShipSymbol,
                "Idle",
                SystemSymbol: @event.SystemSymbol,
                WaypointSymbol: @event.WaypointSymbol,
                CorrelationId: @event.CorrelationId,
                CausationId: @event.EventId), cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        var targetUnits = assignment.RequiredUnits > 0 ? assignment.RequiredUnits : 1;
        var cargoCapacity = ship.CargoCapacity > 0 ? ship.CargoCapacity : targetUnits;
        var unitsToBuy = Math.Min(targetUnits - cargoUnits, cargoCapacity - ship.CargoCurrent);

        if (unitsToBuy > 0)
        {
            logger.LogInformation("{Handler}: ship {Ship} buying {Units}x {Symbol} at {Waypoint}.", nameof(ShipDockedBuilderEventHandler), @event.ShipSymbol, unitsToBuy, assignment.CargoSymbol, @event.WaypointSymbol);
            await dockedCommands.BuyCargoAsync(@event.ShipSymbol, assignment.CargoSymbol, unitsToBuy, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        logger.LogInformation("{Handler}: ship {Ship} has {Units}x {Symbol}; orbiting to depart for construction site {Dest}.", nameof(ShipDockedBuilderEventHandler), @event.ShipSymbol, cargoUnits, assignment.CargoSymbol, assignment.DestWaypoint);
        await dockedCommands.OrbitAsync(@event.ShipSymbol, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }
}
