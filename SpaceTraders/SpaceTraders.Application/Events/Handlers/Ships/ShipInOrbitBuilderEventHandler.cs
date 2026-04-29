using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles Builder-role ships in orbit.
///
/// Decision tree:
///   1. Construction complete → reassign Idle.
///   2. At construction site with cargo → supply materials.
///   3. At construction site without cargo → navigate back to origin to reload.
///   4. Has cargo but not at site → navigate to the construction site.
///   5. No cargo and not at origin → navigate to origin so the docked handler can buy cargo.
///   6. At origin with no cargo → dock so the docked handler can buy cargo.
/// </summary>
public sealed class ShipInOrbitBuilderEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IConstructionRepository constructions,
    IInOrbitCommandAcceptor inOrbitCommands,
    IMessageBus bus,
    ILogger<ShipInOrbitBuilderEventHandler> logger) : IChainOfCommandEventHandler<ShipInOrbitEvent>
{
    public int Priority => 55;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        // ConstructionSuppliedEvent re-enters this chain — handle it here so the loop continues
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Builder", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        if (string.IsNullOrWhiteSpace(assignment.DestWaypoint) || string.IsNullOrWhiteSpace(assignment.CargoSymbol))
        {
            logger.LogWarning("{Handler}: ship {Ship} Builder assignment missing DestWaypoint or CargoSymbol; reassigning Idle.", nameof(ShipInOrbitBuilderEventHandler), @event.ShipSymbol);
            await bus.InvokeAsync(new AssignShipCommand(
                @event.ShipSymbol,
                "Idle",
                SystemSymbol: @event.SystemSymbol,
                WaypointSymbol: @event.WaypointSymbol,
                CorrelationId: @event.CorrelationId,
                CausationId: @event.EventId), cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        var constructionSystem = ExtractSystemSymbol(assignment.DestWaypoint);
        var site = await constructions.FindAsync(assignment.DestWaypoint, cancellationToken);

        if (site is not null && site.IsComplete)
        {
            logger.LogInformation("{Handler}: construction site {Waypoint} complete; reassigning ship {Ship} to Idle.", nameof(ShipInOrbitBuilderEventHandler), assignment.DestWaypoint, @event.ShipSymbol);
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
        if (ship is null || !string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var currentWaypoint = ship.WaypointSymbol ?? @event.WaypointSymbol;

        var cargoUnits = ship.CargoInventory?
            .FirstOrDefault(c => c.Symbol.Equals(assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        var atConstructionSite = currentWaypoint.Equals(assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase);
        var atOrigin = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint) &&
                       currentWaypoint.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase);

        if (atConstructionSite && cargoUnits > 0)
        {
            var targetUnits = assignment.RequiredUnits > 0 ? assignment.RequiredUnits : cargoUnits;
            logger.LogInformation("{Handler}: ship {Ship} at construction site {Waypoint}; supplying {Units}x {Symbol}.", nameof(ShipInOrbitBuilderEventHandler), @event.ShipSymbol, currentWaypoint, cargoUnits, assignment.CargoSymbol);
            await inOrbitCommands.SupplyConstructionAsync(
                @event.ShipSymbol,
                constructionSystem,
                assignment.DestWaypoint,
                assignment.CargoSymbol,
                Math.Min(cargoUnits, targetUnits),
                @event.CorrelationId,
                @event.EventId,
                cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        if (atConstructionSite && cargoUnits == 0)
        {
            // Nothing to supply — head back to origin to reload
            var origin = assignment.OriginWaypoint ?? @event.WaypointSymbol;
            logger.LogInformation("{Handler}: ship {Ship} at construction site but no cargo; navigating back to origin {Origin}.", nameof(ShipInOrbitBuilderEventHandler), @event.ShipSymbol, origin);
            await inOrbitCommands.NavigateAsync(@event.ShipSymbol, origin, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        if (cargoUnits > 0)
        {
            // Cargo loaded — head to construction site
            logger.LogInformation("{Handler}: ship {Ship} has {Units}x {Symbol}; navigating to construction site {Dest}.", nameof(ShipInOrbitBuilderEventHandler), @event.ShipSymbol, cargoUnits, assignment.CargoSymbol, assignment.DestWaypoint);
            await inOrbitCommands.NavigateAsync(@event.ShipSymbol, assignment.DestWaypoint, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        // No cargo — dock at origin so the docked handler can buy
        if (atOrigin)
        {
            logger.LogInformation("{Handler}: ship {Ship} completed supply run; docking at origin {Origin} and returning to Idle.", nameof(ShipInOrbitBuilderEventHandler), @event.ShipSymbol, currentWaypoint);
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
            await bus.InvokeAsync(new AssignShipCommand(
                @event.ShipSymbol,
                "Idle",
                SystemSymbol: @event.SystemSymbol,
                WaypointSymbol: currentWaypoint,
                CorrelationId: @event.CorrelationId,
                CausationId: @event.EventId), cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        var reloadWaypoint = assignment.OriginWaypoint ?? @event.WaypointSymbol;
        logger.LogInformation("{Handler}: ship {Ship} has no cargo and is not at origin; navigating to {Origin}.", nameof(ShipInOrbitBuilderEventHandler), @event.ShipSymbol, reloadWaypoint);
        await inOrbitCommands.NavigateAsync(@event.ShipSymbol, reloadWaypoint, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}
