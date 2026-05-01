using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.Automation;

namespace SpaceTraders.API.Services;

/// <summary>
/// Brings the HTTP host online first, then runs expensive startup initialization in the background.
/// </summary>
public sealed class DeferredStartupHostedService(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime applicationLifetime,
    StartupInitializationState startupState,
    ILogger<DeferredStartupHostedService> logger) : IHostedService
{
    private readonly List<IHostedService> _startedServices = [];
    private readonly Lock _startedServicesLock = new();
    private Task _startupTask = Task.CompletedTask;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        applicationLifetime.ApplicationStarted.Register(() =>
        {
            _startupTask = Task.Run(() => RunDeferredStartupAsync(applicationLifetime.ApplicationStopping), CancellationToken.None);
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_startupTask.IsCompleted)
        {
            await Task.WhenAny(_startupTask, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        }

        List<IHostedService> startedServices;
        lock (_startedServicesLock)
        {
            startedServices = [.. _startedServices];
        }

        for (var index = startedServices.Count - 1; index >= 0; index--)
        {
            await startedServices[index].StopAsync(cancellationToken);
        }
    }

    private async Task RunDeferredStartupAsync(CancellationToken cancellationToken)
    {
        startupState.MarkRunning();
        logger.LogInformation("Deferred startup initialization started after the HTTP host came online.");

        try
        {
            await StartServiceAsync<AgentBootstrapService>(cancellationToken);
            await StartServiceAsync<RunLifecycleService>(cancellationToken);
            await StartServiceAsync<LeaderElectionService>(cancellationToken);
            await StartServiceAsync<StartupSyncService>(cancellationToken);
            await StartServiceAsync<StartupSnapshotService>(cancellationToken);
            await StartServiceAsync<StartupRecoveryService>(cancellationToken);
            await StartServiceAsync<SettingsStartupLoggingService>(cancellationToken);
            await StartServiceAsync<GameLoopService>(cancellationToken);
            await StartServiceAsync<ContractWatchService>(cancellationToken);
            await StartServiceAsync<ResetAndReliabilityMonitorService>(cancellationToken);
            await StartServiceAsync<ActivityLogPruningService>(cancellationToken);
            await StartServiceAsync<DataRetentionService>(cancellationToken);
            await StartServiceAsync<ShipRefreshWorkerService>(cancellationToken);
            await StartServiceAsync<PrometheusMetricsService>(cancellationToken);

            startupState.MarkCompleted();
            logger.LogInformation("Deferred startup initialization completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            startupState.MarkFailed(new OperationCanceledException("Deferred startup initialization was canceled."));
            logger.LogInformation("Deferred startup initialization canceled.");
        }
        catch (Exception exception)
        {
            startupState.MarkFailed(exception);
            logger.LogError(exception, "Deferred startup initialization failed.");
        }
    }

    private async Task StartServiceAsync<TService>(CancellationToken cancellationToken)
        where TService : class, IHostedService
    {
        var service = serviceProvider.GetRequiredService<TService>();
        await service.StartAsync(cancellationToken);

        lock (_startedServicesLock)
        {
            _startedServices.Add(service);
        }
    }
}
