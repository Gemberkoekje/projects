using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Background service that deletes activity log entries older than the
/// <c>ActivityLog.RetentionDays</c> setting (default: 30 days).
/// Runs once at startup, then every 24 hours.
/// </summary>
public sealed class ActivityLogPruningService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<ActivityLogPruningService> logger) : BackgroundService
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(24);
    private const string RetentionSettingKey = "ActivityLog.RetentionDays";
    private const int DefaultRetentionDays = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small startup delay so DB is fully initialised before the first prune.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Activity log pruning failed.");
            }

            await Task.Delay(PruneInterval, stoppingToken);
        }
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var activityLog = scope.ServiceProvider.GetRequiredService<IActivityLogRepository>();

        var retentionDays = await settings.GetAsync<int>(RetentionSettingKey, cancellationToken);
        if (retentionDays <= 0) retentionDays = DefaultRetentionDays;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var deleted = await activityLog.PruneAsync(cutoff, cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation(
                "Pruned {Count} activity log entries older than {Cutoff:O} ({Days}-day retention).",
                deleted, cutoff, retentionDays);
        }
    }
}
