using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles scouting-role ships that are docked. Refreshes market and shipyard data at the current waypoint,
/// then orbits to continue the scouting cycle.
/// </summary>
public sealed class ShipDockedScoutEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IFleetMaintenancePlanner maintenance,
    IWaypointVisitService waypointVisit,
    IMarketRefreshService markets,
    IShipyardRefreshService shipyards,
    IDockedCommandAcceptor dockedCommands) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 300;
    private const string MarketProbeAssignmentType = "MarketProbe";

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null ||
            (!assignment.AssignmentType.Equals("Scout", StringComparison.OrdinalIgnoreCase) &&
             !assignment.AssignmentType.Equals(MarketProbeAssignmentType, StringComparison.OrdinalIgnoreCase)))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var maintenanceDecision = await maintenance.DecideAsync(ship, assignment.AssignmentType, cancellationToken);
        if (maintenanceDecision.ShouldScrap)
        {
            await dockedCommands.ScrapAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        if (maintenanceDecision.ShouldRepair)
        {
            await dockedCommands.RepairAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        var waypoint = ship.WaypointSymbol ?? @event.WaypointSymbol;
        await waypointVisit.MarkVisitedAsync(waypoint, cancellationToken);
        await markets.RefreshIfApplicableAsync(waypoint, cancellationToken);
        await shipyards.RefreshIfApplicableAsync(waypoint, cancellationToken);

        if (assignment.AssignmentType.Equals(MarketProbeAssignmentType, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(assignment.OriginWaypoint) &&
            waypoint.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Handled();
        }

        await dockedCommands.OrbitAsync(@event.ShipSymbol, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }
}
