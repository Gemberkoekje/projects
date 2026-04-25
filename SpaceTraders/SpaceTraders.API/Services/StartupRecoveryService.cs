using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
///  - Ship.ArrivesAt has elapsed  → ship has already arrived; publish ShipArrivedAtWaypointEvent.
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var now = DateTimeOffset.UtcNow;

        var incompleteAssignments = await db.ShipAssignments
            .AsNoTracking()
            .Where(a => a.CompletedAt == null)
            .ToListAsync(cancellationToken);

        if (incompleteAssignments.Count == 0)
        {
            logger.LogInformation("StartupRecovery: no in-flight assignments found.");
            return;
        }

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

                logger.LogInformation(
                    "StartupRecovery: Ship {Symbol} was in transit and has now arrived at {Waypoint}. Publishing ShipArrivedAtWaypointEvent.",
                    assignment.ShipSymbol, arrivedWaypoint);

                await bus.PublishAsync(new ShipArrivedAtWaypointEvent(assignment.ShipSymbol, new WaypointSymbol(arrivedWaypoint)));
            }
            else if (ship.ArrivesAt.HasValue)
            {
                logger.LogInformation(
                    "StartupRecovery: Ship {Symbol} is still in transit (arrives at {ArrivesAt}); GameLoopService will handle arrival.",
                    assignment.ShipSymbol, ship.ArrivesAt);
            }
            else
            {
                logger.LogInformation(
                    "StartupRecovery: Ship {Symbol} resuming at step {Step} of {Type} assignment.",
                    assignment.ShipSymbol, assignment.StepIndex, assignment.Type);
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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
