using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Background service that runs every 10 minutes and requests cache-aware
/// marketplace and shipyard refreshes for every waypoint currently occupied
/// by an owned ship.
/// Only executes when this instance holds the leader lease.
/// </summary>
public sealed class ShipRefreshWorkerService(
    IServiceScopeFactory serviceScopeFactory,
    ILeaderElection leaderElection,
    ILogger<ShipRefreshWorkerService> logger) : BackgroundService
{
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ShipRefreshWorkerService tick.");
            }

            await Task.Delay(RefreshInterval, stoppingToken);
        }
    }

    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!leaderElection.IsLeader)
        {
            logger.LogTrace("ShipRefreshWorkerService: not the leader; skipping tick.");
            return;
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var ships = scope.ServiceProvider.GetRequiredService<IShipRepository>();
        var marketRefresh = scope.ServiceProvider.GetRequiredService<IMarketRefreshService>();
        var shipyardRefresh = scope.ServiceProvider.GetRequiredService<IShipyardRefreshService>();

        var allShips = await ships.GetAllAsync(cancellationToken);

        var waypoints = allShips
            .Select(s => s.WaypointSymbol)
            .Where(w => !string.IsNullOrEmpty(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation(
            "ShipRefreshWorkerService: refreshing market and shipyard data for {Count} waypoint(s).",
            waypoints.Count);

        foreach (var waypoint in waypoints)
        {
            await marketRefresh.RefreshIfApplicableAsync(waypoint!, cancellationToken);
            await shipyardRefresh.RefreshIfApplicableAsync(waypoint!, cancellationToken);
        }
    }
}
