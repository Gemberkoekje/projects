using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Background service that runs nightly data retention jobs:
/// <list type="bullet">
///   <item>Prunes <c>MarketPriceSample</c> rows older than 7 days, keeping hourly aggregates for 90 days.</item>
///   <item>Prunes <c>AgentCreditsSample</c> rows older than 7 days, keeping one row per hour for 90 days.</item>
///   <item>Prunes <c>LedgerEntry</c> rows older than 30 days.</item>
///   <item>Prunes <c>ShipTaskRecord</c> rows older than 30 days.</item>
/// </list>
/// Runs once at startup (after a short delay), then every 24 hours.
/// </summary>
public sealed class DataRetentionService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<DataRetentionService> logger) : BackgroundService
{
    internal static readonly TimeSpan PruneInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);

    internal const int MarketSampleRawRetentionDays = 7;
    internal const int MarketSampleAggregateRetentionDays = 90;
    internal const int CreditSampleRawRetentionDays = 7;
    internal const int CreditSampleAggregateRetentionDays = 90;
    internal const int LedgerEntryRetentionDays = 30;
    internal const int ShipTaskRecordRetentionDays = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Data retention pruning failed.");
            }

            await Task.Delay(PruneInterval, stoppingToken);
        }
    }

    internal async Task PruneAllAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var now = TimeProvider.System.GetUtcNow();

        await PruneMarketPriceSamplesAsync(scope, now, cancellationToken);
        await PruneAgentCreditsSamplesAsync(scope, now, cancellationToken);
        await PruneLedgerEntriesAsync(scope, now, cancellationToken);
        await PruneShipTaskRecordsAsync(scope, now, cancellationToken);
    }

    private async Task PruneMarketPriceSamplesAsync(IServiceScope scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var repo = scope.ServiceProvider.GetRequiredService<IMarketPriceSampleRepository>();
        var rawCutoff = now.AddDays(-MarketSampleRawRetentionDays);
        var aggregateCutoff = now.AddDays(-MarketSampleAggregateRetentionDays);

        var deleted = await repo.PruneAsync(rawCutoff, aggregateCutoff, cancellationToken);
        if (deleted > 0)
        {
            logger.LogInformation(
                "Pruned {Count} market price sample rows (raw >{RawDays}d; aggregates >{AggDays}d).",
                deleted, MarketSampleRawRetentionDays, MarketSampleAggregateRetentionDays);
        }
    }

    private async Task PruneAgentCreditsSamplesAsync(IServiceScope scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var repo = scope.ServiceProvider.GetRequiredService<IAgentCreditsSampleRepository>();
        var rawCutoff = now.AddDays(-CreditSampleRawRetentionDays);
        var aggregateCutoff = now.AddDays(-CreditSampleAggregateRetentionDays);

        var deleted = await repo.PruneAsync(rawCutoff, aggregateCutoff, cancellationToken);
        if (deleted > 0)
        {
            logger.LogInformation(
                "Pruned {Count} agent credits sample rows (raw >{RawDays}d; aggregates >{AggDays}d).",
                deleted, CreditSampleRawRetentionDays, CreditSampleAggregateRetentionDays);
        }
    }

    private async Task PruneLedgerEntriesAsync(IServiceScope scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var repo = scope.ServiceProvider.GetRequiredService<ILedgerRepository>();
        var cutoff = now.AddDays(-LedgerEntryRetentionDays);

        var deleted = await repo.PruneAsync(cutoff, cancellationToken);
        if (deleted > 0)
        {
            logger.LogInformation(
                "Pruned {Count} ledger entries older than {Days} days.",
                deleted, LedgerEntryRetentionDays);
        }
    }

    private async Task PruneShipTaskRecordsAsync(IServiceScope scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var repo = scope.ServiceProvider.GetRequiredService<IShipTaskRecordRepository>();
        var cutoff = now.AddDays(-ShipTaskRecordRetentionDays);

        var deleted = await repo.PruneAsync(cutoff, cancellationToken);
        if (deleted > 0)
        {
            logger.LogInformation(
                "Pruned {Count} ship task records older than {Days} days.",
                deleted, ShipTaskRecordRetentionDays);
        }
    }
}
