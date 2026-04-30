using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles scouting-role ships in orbit. Refreshes market and shipyard data at the current waypoint
/// regardless of the triggering event, then docks if the ship is at its target or navigates to the target.
/// </summary>
public sealed class ShipInOrbitScoutEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IInOrbitCommandAcceptor inOrbitCommands,
    IWaypointVisitService waypointVisit,
    IMarketRefreshService markets,
    IShipyardRefreshService shipyards,
    IMessageBus bus,
    ILogger<ShipInOrbitScoutEventHandler> logger) : IChainOfCommandEventHandler<ShipInOrbitEvent>
{
    private const string MarketProbeAssignmentType = "MarketProbe";

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null ||
            (!assignment.AssignmentType.Equals("Scout", StringComparison.OrdinalIgnoreCase) &&
             !assignment.AssignmentType.Equals(MarketProbeAssignmentType, StringComparison.OrdinalIgnoreCase)))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        if (string.IsNullOrWhiteSpace(assignment.OriginWaypoint))
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

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is not null && !string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var currentWaypoint = ship?.WaypointSymbol ?? @event.WaypointSymbol;

        // Refresh data at the current waypoint regardless of what triggered this event
        await waypointVisit.MarkVisitedAsync(currentWaypoint, cancellationToken);
        await markets.RefreshIfApplicableAsync(currentWaypoint, cancellationToken);
        await shipyards.RefreshIfApplicableAsync(currentWaypoint, cancellationToken);

        // At scout target: chart for exploration metadata then dock so the docked handler can complete the scouting cycle
        if (string.Equals(currentWaypoint, assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("{Handler}: ship {Ship} at target {Waypoint}; charting and docking.", nameof(ShipInOrbitScoutEventHandler), @event.ShipSymbol, currentWaypoint);

            if (!assignment.AssignmentType.Equals(MarketProbeAssignmentType, StringComparison.OrdinalIgnoreCase))
            {
                await bus.InvokeAsync(new CreateChartCommand(@event.ShipSymbol), cancellationToken);
            }

            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        // Not at target: navigate towards it
        logger.LogInformation("{Handler}: ship {Ship} navigating to scout target {Waypoint}.", nameof(ShipInOrbitScoutEventHandler), @event.ShipSymbol, assignment.OriginWaypoint);
        if (ship is not null && ship.FuelCapacity <= 0 && !string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(@event.ShipSymbol, "DRIFT"), cancellationToken);
        }

        await inOrbitCommands.NavigateAsync(@event.ShipSymbol, assignment.OriginWaypoint, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }
}
