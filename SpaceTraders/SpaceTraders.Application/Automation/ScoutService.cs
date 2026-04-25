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
///   2. Find waypoints in active systems that are unscouted or stale.
///   3. For each stale waypoint: find an idle ship and assign a Scout mission.
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
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var automationEnabled = await settings.GetAsync<bool>("Automation.Enabled", cancellationToken);
        if (!automationEnabled) return;

        var refreshIntervalMinutes = await settings.GetAsync<int>("Scout.MarketRefreshIntervalMinutes", cancellationToken);
        var staleness = TimeSpan.FromMinutes(refreshIntervalMinutes > 0 ? refreshIntervalMinutes : 10);

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
            var staleWaypoints = await waypoints.GetUnscoutedOrStaleAsync(system, staleness, cancellationToken);
            if (staleWaypoints.Count == 0)
            {
                logger.LogDebug("No stale waypoints in system {System}.", system);
                continue;
            }

            foreach (var waypoint in staleWaypoints)
            {
                // Find an idle ship in this system to send to scout
                var idleShip = await FindIdleShipInSystemAsync(system, allShips, assignments, cancellationToken);
                if (idleShip is null)
                {
                    logger.LogDebug("No idle ship available in {System} for scouting.", system);
                    break;
                }

                logger.LogInformation(
                    "ScoutService: assigning ship {Ship} to scout {Waypoint} in {System}.",
                    idleShip.Symbol, waypoint.Symbol, system);

                await bus.SendAsync(new AssignShipCommand(
                    idleShip.Symbol,
                    "Scout",
                    OriginWaypoint: waypoint.Symbol));

                // Only assign one scout per system per tick; re-evaluate next tick
                break;
            }
        }
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
            // A ship is idle if it has no assignment or its last assignment is completed
            if (assignment is null || assignment.CompletedAt.HasValue)
                return ship;
        }

        return null;
    }
}
