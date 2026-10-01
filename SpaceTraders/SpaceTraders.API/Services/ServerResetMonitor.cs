using SpaceTraders.Application;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.API.Services;

/// <summary>
/// Stops the host when the SpaceTraders server is reset during a run (B6). Kubernetes restarts the
/// pod, and agent bootstrap then registers a new agent with the account token. Before that it
/// switches automation off, so nothing else is tried with the old token while the host stops.
/// </summary>
/// <remarks>
/// Reports during startup are ignored: agent bootstrap tries old tokens on purpose and expects
/// this error, and any other startup step that fails stops the host anyway.
/// </remarks>
public sealed class ServerResetMonitor(
    StartupInitializationState startupState,
    IHostApplicationLifetime applicationLifetime,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<ServerResetMonitor> logger) : IServerResetMonitor
{
    private int _reported;

    /// <inheritdoc />
    public async Task ReportAsync(string detail, CancellationToken cancellationToken)
    {
        if (!startupState.IsCompleted || Interlocked.Exchange(ref _reported, 1) == 1)
        {
            return;
        }

        logger.LogCritical(
            "{EventKind:l}: the SpaceTraders server was reset ({Detail}). Switching automation off and stopping the host; after the restart a new agent is registered.",
            JournalEvents.ResetDetected,
            detail);

        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            await settings.SetAsync(AutomationSwitches.EnabledSetting, "false", CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not switch automation off after the server reset; stopping the host anyway.");
        }

        applicationLifetime.StopApplication();
    }
}
