using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Application.Services;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Consolidated handler for all trade-role ship in-orbit events.
/// Handles ShipInOrbitEvent (general), ShipUndockedEvent (navigate), and trading workflow.
/// </summary>
public sealed class ShipInOrbitTraderEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IInOrbitCommandAcceptor inOrbitCommands,
    INavigationPlanningService navigationPlanning,
    IMessageBus bus,
    ILogger<ShipInOrbitTraderEventHandler> logger) : IChainOfCommandEventHandler<ShipInOrbitEvent>
{
    public int Priority => 70;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ChainOfCommandHandlerResult.Handled(new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipInOrbitTraderEventHandler),
                "IN_ORBIT",
                "UNKNOWN",
                "Trade in-orbit handler could not load ship state.",
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
            logger.LogInformation("{Handler}: ship {Ship} docking at {Waypoint}.", nameof(ShipInOrbitTraderEventHandler), @event.ShipSymbol, destination);
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        logger.LogInformation("{Handler}: ship {Ship} navigating to {Waypoint}.", nameof(ShipInOrbitTraderEventHandler), @event.ShipSymbol, destination);
        if (ShouldAdjustFlightMode(ship, destination))
        {
            var plan = await navigationPlanning.BuildPlanAsync(ship, destination, cancellationToken);
            if (!string.IsNullOrWhiteSpace(plan.RecommendedFlightMode) &&
                !string.Equals(ship.FlightMode, plan.RecommendedFlightMode, StringComparison.OrdinalIgnoreCase))
            {
                await bus.InvokeAsync(new PatchShipNavCommand(@event.ShipSymbol, plan.RecommendedFlightMode), cancellationToken);
            }
        }

        await inOrbitCommands.NavigateAsync(@event.ShipSymbol, destination, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }

    private static bool ShouldAdjustFlightMode(
        SpaceTraders.Application.Ports.ShipModel ship,
        string destinationWaypoint)
    {
        if (string.IsNullOrWhiteSpace(ship.SystemSymbol) || string.IsNullOrWhiteSpace(destinationWaypoint))
        {
            return false;
        }

        var destinationSystem = ExtractSystemSymbol(destinationWaypoint);
        return ship.SystemSymbol.Equals(destinationSystem, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }

    private static string ResolveDestination(
        SpaceTraders.Application.DTOs.ShipAssignmentDto assignment,
        SpaceTraders.Application.Ports.ShipModel ship)
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
