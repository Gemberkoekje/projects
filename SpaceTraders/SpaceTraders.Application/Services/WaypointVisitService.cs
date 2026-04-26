using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Services;

public sealed class WaypointVisitService(IWaypointRepository waypoints) : IWaypointVisitService
{
    public Task MarkVisitedAsync(string waypointSymbol, CancellationToken cancellationToken)
        => waypoints.MarkVisitedAsync(waypointSymbol, cancellationToken);
}
