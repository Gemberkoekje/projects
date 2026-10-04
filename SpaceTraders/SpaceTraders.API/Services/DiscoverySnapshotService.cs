using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.API.Services;

/// <summary>
/// Every minute, looks for a ship type a cached shipyard lists, or a good a cached market trades, that no snapshot of the
/// run held yet, and takes a snapshot when there is one (slice 2.15, D73: "whenever a new discovery is made. So a shipyard
/// with a new ship type or a market with a new good type"). The snapshot says what was new and where, and the journal
/// has a <c>Discovered</c> line.
/// </summary>
/// <remarks>
/// It reads the cache rather than following each of its writers (the arrivals, the market watch, the exploring command
/// ship, startup sync, purchases), so whatever stores a shipyard or market is covered, and discoveries made within the same
/// minute share one snapshot. It starts from what the startup snapshot held; when that failed, from what the cache lists
/// at its first look.
/// </remarks>
public sealed class DiscoverySnapshotService(
    GameStateSnapshots snapshots,
    ILogger<DiscoverySnapshotService> logger) : BackgroundService
{
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>Looks once: takes a snapshot when the cache lists a ship type or good no snapshot held.</summary>
    /// <param name="cancellationToken">Stops the look.</param>
    /// <returns>The snapshot taken, or null.</returns>
    internal async Task<StartupSnapshot?> CheckAsync(CancellationToken cancellationToken)
    {
        var listed = await snapshots.ReadKnownAsync(cancellationToken);
        if (snapshots.Known is not { } known)
        {
            snapshots.Remember(listed);
            return null;
        }

        return listed.NotIn(known).Count == 0
            ? null
            : await snapshots.TakeAsync(StartupSnapshot.DiscoveryReason, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
                await CheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to look for discoveries; trying again in a minute.");
            }
        }
    }
}
