using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.API.Services;

/// <summary>
/// Saves a JSON snapshot of the game state at startup (<see cref="GameStateSnapshots"/>): the agent, its ships (with
/// their goals), its contracts, and every system it has a ship, market or shipyard in, with every market and shipyard
/// cached there.
/// </summary>
/// <remarks>
/// It runs right after startup sync and reads what sync has just cached. It calls no API itself: it
/// used to fetch all of that again, about 11 calls on every start (B35). What it holds is where
/// <see cref="DiscoverySnapshotService"/> starts from.
/// </remarks>
public sealed class StartupSnapshotService(
    GameStateSnapshots snapshots,
    ILogger<StartupSnapshotService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await snapshots.TakeAsync(StartupSnapshot.StartupReason, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "StartupSnapshotService failed; snapshot will not be saved this run.");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
