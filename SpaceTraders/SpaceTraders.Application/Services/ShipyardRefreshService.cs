using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.Interfaces.Repositories;
using Wolverine;

namespace SpaceTraders.Application.Services;

public sealed class ShipyardRefreshService(
    IWaypointRepository waypoints,
    IMessageBus bus) : IShipyardRefreshService
{
    public async Task RefreshIfApplicableAsync(string waypointSymbol, CancellationToken cancellationToken)
    {
        var waypoint = await waypoints.FindAsync(waypointSymbol, cancellationToken);
        if (waypoint is null || !waypoint.HasShipyard)
        {
            return;
        }

        await bus.SendAsync(new RefreshShipyardDataCommand(waypoint.SystemSymbol, waypointSymbol));
    }
}
