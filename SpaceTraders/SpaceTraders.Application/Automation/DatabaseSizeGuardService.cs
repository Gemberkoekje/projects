using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Keeps the bot from filling the Postgres volume it shares with every other app (D8). Every 5
/// minutes it reads the database size and exports it as <c>spacetraders_db_size_bytes</c>. Above
/// <c>Database.SoftLimitMegabytes</c> it logs a warning; above <c>Database.HardLimitMegabytes</c>
/// it switches automation off. Each limit is an anomaly while the database is above it: the
/// journal says so when it is raised and when it clears (<c>AnomalyRaised</c>, <c>AnomalyCleared</c>),
/// and <c>spacetraders_anomaly_active{rule="DbSizeSoftLimit"|"DbSizeHardLimit",subject="database"}</c>
/// is 1 meanwhile.
/// </summary>
public sealed class DatabaseSizeGuardService(
    IServiceScopeFactory serviceScopeFactory,
    IAutomationMetrics metrics,
    ILogger<DatabaseSizeGuardService> logger) : BackgroundService
{
    public const string SoftLimitSetting = "Database.SoftLimitMegabytes";
    public const string HardLimitSetting = "Database.HardLimitMegabytes";
    public const int DefaultSoftLimitMegabytes = 1024;
    public const int DefaultHardLimitMegabytes = 3072;

    private const string SoftLimitRule = "DbSizeSoftLimit";
    private const string HardLimitRule = "DbSizeHardLimit";
    private const string AnomalySubject = "database";

    internal static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    private const long Megabyte = 1024 * 1024;

    private bool _aboveSoftLimit;
    private bool _aboveHardLimit;

    /// <summary>
    /// Checks once before the rest of startup goes on: a database over the hard limit has
    /// automation off before the tick starts.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await CheckSafelyAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    internal async Task CheckAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var bytes = await scope.ServiceProvider.GetRequiredService<IDatabaseSize>().GetBytesAsync(cancellationToken);
        metrics.DatabaseSize(bytes);

        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var softLimit = await LimitAsync(settings, SoftLimitSetting, DefaultSoftLimitMegabytes, cancellationToken);
        var hardLimit = await LimitAsync(settings, HardLimitSetting, DefaultHardLimitMegabytes, cancellationToken);
        var megabytes = bytes / Megabyte;

        var aboveSoftLimit = bytes > softLimit * Megabyte;
        var aboveHardLimit = bytes > hardLimit * Megabyte;
        metrics.Anomaly(SoftLimitRule, AnomalySubject, aboveSoftLimit);
        metrics.Anomaly(HardLimitRule, AnomalySubject, aboveHardLimit);

        if (aboveSoftLimit && !_aboveSoftLimit)
        {
            logger.LogWarning(
                "{EventKind}: {Rule} on {Subject}: the database is {SizeMegabytes} MB, above the soft limit of {LimitMegabytes} MB ({Setting}).",
                JournalEvents.AnomalyRaised,
                SoftLimitRule,
                AnomalySubject,
                megabytes,
                softLimit,
                SoftLimitSetting);
        }

        if (aboveHardLimit)
        {
            var automationWasOn = await settings.IsAutomationEnabledAsync(cancellationToken);
            if (automationWasOn)
            {
                await settings.SetAsync(AutomationSwitches.EnabledSetting, "false", cancellationToken);
            }

            if (!_aboveHardLimit)
            {
                logger.LogError(
                    "{EventKind}: {Rule} on {Subject}: the database is {SizeMegabytes} MB, above the hard limit of {LimitMegabytes} MB ({Setting}). Automation is off until someone switches it back on.",
                    JournalEvents.AnomalyRaised,
                    HardLimitRule,
                    AnomalySubject,
                    megabytes,
                    hardLimit,
                    HardLimitSetting);
            }
            else if (automationWasOn)
            {
                logger.LogError(
                    "The database is still {SizeMegabytes} MB, above the hard limit of {LimitMegabytes} MB ({Setting}); automation is switched off again.",
                    megabytes,
                    hardLimit,
                    HardLimitSetting);
            }
        }

        if (!aboveHardLimit && _aboveHardLimit)
        {
            LogCleared(HardLimitRule, megabytes);
        }

        if (!aboveSoftLimit && _aboveSoftLimit)
        {
            LogCleared(SoftLimitRule, megabytes);
        }

        _aboveSoftLimit = aboveSoftLimit;
        _aboveHardLimit = aboveHardLimit;
    }

    private void LogCleared(string rule, long megabytes)
        => logger.LogInformation(
            "{EventKind}: {Rule} on {Subject}: the database is {SizeMegabytes} MB, under that limit again.",
            JournalEvents.AnomalyCleared,
            rule,
            AnomalySubject,
            megabytes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(CheckInterval, stoppingToken);
            await CheckSafelyAsync(stoppingToken);
        }
    }

    private static async Task<long> LimitAsync(ISettingsRepository settings, string key, int defaultMegabytes, CancellationToken cancellationToken)
    {
        var megabytes = await settings.GetAsync<int>(key, cancellationToken);
        return megabytes > 0 ? megabytes : defaultMegabytes;
    }

    private async Task CheckSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CheckAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Checking the database size failed; the next check is in {Interval}.", CheckInterval);
        }
    }
}
