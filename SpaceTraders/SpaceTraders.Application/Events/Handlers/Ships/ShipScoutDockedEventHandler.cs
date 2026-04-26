using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipScoutDockedEventHandler(
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
            var missingShipMismatch = new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipScoutDockedEventHandler),
                "DOCKED",
                "UNKNOWN",
                "Scout docked handler could not load ship state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(missingShipMismatch);
        }

        if (!string.Equals(ship.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
        {
            var stateMismatch = new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipScoutDockedEventHandler),
                "DOCKED",
                ship.Status ?? "UNKNOWN",
                "Scout docked handler received ship outside docked state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(stateMismatch);
        }

        var waypoint = ship.WaypointSymbol ?? @event.WaypointSymbol;
        await waypointVisit.MarkVisitedAsync(waypoint, cancellationToken);
        await markets.RefreshIfApplicableAsync(waypoint, cancellationToken);
        await shipyards.RefreshIfApplicableAsync(waypoint, cancellationToken);

        await dockedCommands.OrbitAsync(@event.ShipSymbol, cancellationToken);

        var now = TimeProvider.System.GetUtcNow();
        var undockedEvent = new ShipUndockedEvent(
            @event.ShipSymbol,
            ship.SystemSymbol ?? @event.SystemSymbol,
            waypoint,
            @event.CorrelationId,
            @event.EventId,
            now);

        return ChainOfCommandHandlerResult.Handled(undockedEvent);
    }
}
