using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Periodically finds waypoints with stale or missing market/shipyard data
/// and assigns idle ships to scout them.
///
/// Every <see cref="PollInterval"/>:
///   1. Check if automation is enabled.
///   2. Find waypoints in active systems that are missing/stale market data (priority)
///      or missing/stale shipyard data (secondary).
///   3. For each target waypoint: find an idle ship and assign a Scout mission.
/// </summary>
public sealed class ScoutService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<ScoutService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a short time on startup to let sync commands complete first.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

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
                logger.LogError(ex, "Error in ScoutService tick.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var ships = scope.ServiceProvider.GetRequiredService<IShipRepository>();
        var assignments = scope.ServiceProvider.GetRequiredService<IShipAssignmentRepository>();
        var waypoints = scope.ServiceProvider.GetRequiredService<IWaypointRepository>();
        var markets = scope.ServiceProvider.GetRequiredService<IMarketRepository>();
        var shipyards = scope.ServiceProvider.GetRequiredService<IShipyardRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var automationEnabled = await settings.GetAsync<bool>("Automation.Enabled", cancellationToken);
        if (!automationEnabled) return;

        var marketRefreshIntervalMinutes = await settings.GetAsync<int>("Scout.MarketRefreshIntervalMinutes", cancellationToken);
        var shipyardRefreshIntervalMinutes = await settings.GetAsync<int>("Scout.ShipyardRefreshIntervalMinutes", cancellationToken);

        var marketStaleness = TimeSpan.FromMinutes(marketRefreshIntervalMinutes > 0 ? marketRefreshIntervalMinutes : 10);
        var shipyardStaleness = TimeSpan.FromMinutes(shipyardRefreshIntervalMinutes > 0 ? shipyardRefreshIntervalMinutes : 30);

        var allShips = await ships.GetAllAsync(cancellationToken);
        if (allShips.Count == 0) return;

        // Determine which systems our ships are in
        var activeSystems = allShips
            .Where(s => s.SystemSymbol is not null)
            .Select(s => s.SystemSymbol!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var system in activeSystems)
        {
            var target = await FindNextScoutTargetAsync(
                system,
                waypoints,
                markets,
                shipyards,
                marketStaleness,
                shipyardStaleness,
                cancellationToken);

            if (target is null)
            {
                logger.LogDebug("No scout targets in system {System}.", system);
                continue;
            }

            // Find an idle ship in this system to send to scout
            var idleShip = await FindIdleShipInSystemAsync(system, allShips, assignments, cancellationToken);
            if (idleShip is null)
            {
                logger.LogDebug("No idle ship available in {System} for scouting.", system);
                continue;
            }

            logger.LogInformation(
                "ScoutService: assigning ship {Ship} to scout {Waypoint} in {System}.",
                idleShip.Symbol,
                target.Symbol,
                system);

            await bus.SendAsync(new AssignShipCommand(
                idleShip.Symbol,
                "Scout",
                OriginWaypoint: target.Symbol));
        }
    }

    private static async Task<WaypointCacheModel?> FindNextScoutTargetAsync(
        string systemSymbol,
        IWaypointRepository waypoints,
        IMarketRepository markets,
        IShipyardRepository shipyards,
        TimeSpan marketStaleness,
        TimeSpan shipyardStaleness,
        CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var systemWaypoints = await waypoints.GetBySystemAsync(systemSymbol, cancellationToken);

        var marketCandidates = new List<(WaypointCacheModel Waypoint, DateTimeOffset? LastObserved)>();
        var shipyardCandidates = new List<(WaypointCacheModel Waypoint, DateTimeOffset? LastObserved)>();

        foreach (var waypoint in systemWaypoints)
        {
            if (waypoint.HasMarket)
            {
                var lastMarketObserved = await markets.GetLastObservedAtAsync(waypoint.Symbol, cancellationToken);
                if (!lastMarketObserved.HasValue || now - lastMarketObserved.Value >= marketStaleness)
                {
                    marketCandidates.Add((waypoint, lastMarketObserved));
                }
            }

            if (waypoint.HasShipyard)
            {
                var lastShipyardObserved = await shipyards.GetLastObservedAtAsync(waypoint.Symbol, cancellationToken);
                if (!lastShipyardObserved.HasValue || now - lastShipyardObserved.Value >= shipyardStaleness)
                {
                    shipyardCandidates.Add((waypoint, lastShipyardObserved));
                }
            }
        }

        var marketTarget = marketCandidates
            .OrderBy(c => c.LastObserved ?? DateTimeOffset.MinValue)
            .Select(c => c.Waypoint)
            .FirstOrDefault();

        if (marketTarget is not null)
        {
            return marketTarget;
        }

        return shipyardCandidates
            .OrderBy(c => c.LastObserved ?? DateTimeOffset.MinValue)
            .Select(c => c.Waypoint)
            .FirstOrDefault();
    }

    private static async Task<ShipModel?> FindIdleShipInSystemAsync(
        string systemSymbol,
        IReadOnlyList<ShipModel> allShips,
        IShipAssignmentRepository assignments,
        CancellationToken cancellationToken)
    {
        var systemShips = allShips
            .Where(s => s.SystemSymbol?.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase) == true
                        && !s.IsInTransit)
            .ToList();

        foreach (var ship in systemShips)
        {
            var assignment = await assignments.FindAsync(ship.Symbol, cancellationToken);
            // A ship is idle if it has no assignment, its last assignment is completed,
            // or it currently has an explicit Idle assignment.
            if (assignment is null ||
                assignment.CompletedAt.HasValue ||
                assignment.AssignmentType.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            {
                return ship;
            }
        }

        return null;
    }
}
