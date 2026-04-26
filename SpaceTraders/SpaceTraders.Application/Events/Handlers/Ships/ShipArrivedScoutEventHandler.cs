using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipArrivedScoutEventHandler(
    IShipAssignmentRepository assignments,
    IWaypointVisitService waypointVisit,
    IMarketRefreshService markets,
    IShipyardRefreshService shipyards) : IChainOfCommandEventHandler<ShipArrivedEvent>
{
    public int Priority => 100;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipArrivedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Scout", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        await waypointVisit.MarkVisitedAsync(@event.WaypointSymbol, cancellationToken);
        await markets.RefreshIfApplicableAsync(@event.WaypointSymbol, cancellationToken);
        await shipyards.RefreshIfApplicableAsync(@event.WaypointSymbol, cancellationToken);

        var idleEvent = new ShipIdleEvent(
            @event.ShipSymbol,
            "Scout arrival handled; cache refresh requested.",
            @event.CorrelationId,
            @event.EventId,
            TimeProvider.System.GetUtcNow());

        return ChainOfCommandHandlerResult.Handled(idleEvent);
    }
}
