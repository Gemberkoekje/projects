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
    IWaypointVisitService waypointVisit,
    IMarketRefreshService markets,
    IShipyardRefreshService shipyards,
    IDockedCommandAcceptor dockedCommands) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 300;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Scout", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var waypoint = ship.WaypointSymbol ?? @event.WaypointSymbol;
        await waypointVisit.MarkVisitedAsync(waypoint, cancellationToken);
        await markets.RefreshIfApplicableAsync(waypoint, cancellationToken);
        await shipyards.RefreshIfApplicableAsync(waypoint, cancellationToken);

        await dockedCommands.OrbitAsync(@event.ShipSymbol, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }
}
