using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.Interfaces.Repositories;
using Wolverine;

namespace SpaceTraders.Application.Services;

public sealed class MarketRefreshService(
    IWaypointRepository waypoints,
    IMessageBus bus) : IMarketRefreshService
{
    public async Task RefreshIfApplicableAsync(string waypointSymbol, CancellationToken cancellationToken)
    {
        var waypoint = await waypoints.FindAsync(waypointSymbol, cancellationToken);
        if (waypoint is null || !waypoint.HasMarket)
        {
            return;
        }

        await bus.SendAsync(new RefreshMarketDataCommand(waypoint.SystemSymbol, waypointSymbol));
    }
}
