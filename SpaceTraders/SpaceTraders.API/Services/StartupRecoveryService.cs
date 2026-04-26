using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using SpaceTraders.Infrastructure.Persistence;
using Wolverine;

namespace SpaceTraders.API.Services;

/// <summary>
/// Runs once at startup (after <see cref="StartupSyncService"/>) to resume
/// in-flight ship assignments that were interrupted by a pod restart.
///
/// For each active (incomplete) assignment:
///  - Ship.ArrivesAt has elapsed  → ship has already arrived; publish ShipEnteredOrbitEvent and ShipArrivedAtWaypointEvent.
///  - Ship.ArrivesAt is in the future → ship is still in transit; GameLoopService will handle arrival.
///  - Ship has no ArrivesAt         → resume at persisted StepIndex; no action needed here.
///
/// Only issues a full API sync when the last sync is older than
/// <c>StartupRecovery:SyncThresholdMinutes</c> (default: 60).
/// </summary>
public sealed class StartupRecoveryService(
    IServiceScopeFactory serviceScopeFactory,
    IConfiguration configuration,
    ILogger<StartupRecoveryService> logger) : IHostedService
{
    private int RecoverySyncThresholdMinutes =>
        configuration.GetValue("StartupRecovery:SyncThresholdMinutes", 60);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var ships = scope.ServiceProvider.GetRequiredService<IShipRepository>();
        var assignments = scope.ServiceProvider.GetRequiredService<IShipAssignmentRepository>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var now = TimeProvider.System.GetUtcNow();
        var recoveredArrivals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var incompleteAssignments = await db.ShipAssignments
            .AsNoTracking()
            .Where(a => a.CompletedAt == null)
            .ToListAsync(cancellationToken);

        if (incompleteAssignments.Count == 0)
        {
            logger.LogInformation("StartupRecovery: no in-flight assignments found.");
        }
        else
        {
            logger.LogInformation("StartupRecovery: found {Count} in-flight assignment(s). Checking for arrived ships.", incompleteAssignments.Count);

            foreach (var assignment in incompleteAssignments)
            {
                var ship = await db.Ships.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Symbol == assignment.ShipSymbol, cancellationToken);

                if (ship is null) continue;

                if (ship.ArrivesAt.HasValue && ship.ArrivesAt.Value <= now)
                {
                    var arrivedWaypoint = ship.DestWaypointSymbol ?? ship.WaypointSymbol;
                    if (arrivedWaypoint is null) continue;

                    var arrivedNav = new SpaceTraders.Application.Ports.NavModel(
                        Status: "IN_ORBIT",
                        SystemSymbol: ship.SystemSymbol ?? string.Empty,
                        WaypointSymbol: arrivedWaypoint,
                        FlightMode: ship.FlightMode ?? "CRUISE",
                        DestWaypointSymbol: null,
                        ArrivesAt: null);

                    await ships.UpdateNavAsync(assignment.ShipSymbol, arrivedNav, null, cancellationToken);
                    recoveredArrivals.Add(assignment.ShipSymbol);

                    logger.LogInformation(
                        "StartupRecovery: Ship {Symbol} was in transit and has now arrived at {Waypoint}. Publishing ShipEnteredOrbitEvent and ShipArrivedAtWaypointEvent.",
                        assignment.ShipSymbol,
                        arrivedWaypoint);

                    var waypoint = new WaypointSymbol(arrivedWaypoint);
                    await bus.PublishAsync(new ShipEnteredOrbitEvent(assignment.ShipSymbol, waypoint));
                    await bus.PublishAsync(new ShipArrivedAtWaypointEvent(assignment.ShipSymbol, waypoint));
                }
                else if (ship.ArrivesAt.HasValue)
                {
                    logger.LogInformation(
                        "StartupRecovery: Ship {Symbol} is still in transit (arrives at {ArrivesAt}); GameLoopService will handle arrival.",
                        assignment.ShipSymbol,
                        ship.ArrivesAt);
                }
                else
                {
                    logger.LogInformation(
                        "StartupRecovery: Ship {Symbol} resuming at step {Step} of {Type} assignment.",
                        assignment.ShipSymbol,
                        assignment.StepIndex,
                        assignment.Type);
                }
            }
        }

        // Issue a full sync only if last data is stale
        var oldestSync = await db.Ships.AsNoTracking()
            .MinAsync(s => (DateTimeOffset?)s.LastSyncedAt, cancellationToken);

        if (oldestSync is null || (now - oldestSync.Value).TotalMinutes >= RecoverySyncThresholdMinutes)
        {
            logger.LogInformation("StartupRecovery: ship data is stale; dispatching SyncAllShipsCommand.");
            await bus.SendAsync(new Application.Sync.SyncAllShipsCommand());
        }

        await EnsureIdleShipsArePublishedAsync(ships, assignments, settings, recoveredArrivals, bus, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureIdleShipsArePublishedAsync(
        IShipRepository ships,
        IShipAssignmentRepository assignments,
        ISettingsRepository settings,
        ISet<string> recoveredArrivals,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var automationEnabled = await settings.GetAsync<bool>("Automation.Enabled", cancellationToken);
        if (!automationEnabled)
        {
            logger.LogInformation("StartupRecovery: automation disabled; skipping idle ship recovery.");
            return;
        }

        var fleet = await ships.GetAllAsync(cancellationToken);

        foreach (var ship in fleet)
        {
            if (ship.IsInTransit) continue;
            if (recoveredArrivals.Contains(ship.Symbol)) continue;

            var currentAssignment = await assignments.FindAsync(ship.Symbol, cancellationToken);
            var hasActiveNonIdleAssignment =
                currentAssignment is not null &&
                !currentAssignment.CompletedAt.HasValue &&
                !currentAssignment.AssignmentType.Equals("Idle", StringComparison.OrdinalIgnoreCase);

            if (hasActiveNonIdleAssignment) continue;

            logger.LogInformation(
                "StartupRecovery: ship {Symbol} is idle/stalled. Publishing ShipBecameIdleEvent.",
                ship.Symbol);

            await bus.PublishAsync(new ShipBecameIdleEvent(ship.Symbol, "Startup recovery detected idle or stalled ship"));
        }
    }
}
