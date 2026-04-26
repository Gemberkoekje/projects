namespace SpaceTraders.API.Services;

/// <summary>
/// Logs all active settings once during API startup.
/// </summary>
public sealed class SettingsStartupLoggingService(
    SettingsSnapshotLogger snapshotLogger,
    ILogger<SettingsStartupLoggingService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await snapshotLogger.LogAsync("startup", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to log settings snapshot at startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
