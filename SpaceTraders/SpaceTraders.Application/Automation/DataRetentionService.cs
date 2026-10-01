using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Prunes every table that grows, by the policy <see cref="IDataRetention"/> has for it: once
/// when started, then every 24 hours. It only needs the database, so the startup chain starts it
/// right after initialising the database, before any step that could fail. Each table is pruned
/// in its own scope; one that fails is logged and the others carry on.
/// </summary>
public sealed class DataRetentionService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<DataRetentionService> logger) : BackgroundService
{
    internal static readonly TimeSpan PruneInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
        var now = TimeProvider.System.GetUtcNow();
        IReadOnlyList<string> tables;
        await using (var scope = serviceScopeFactory.CreateAsyncScope())
        {
            tables = scope.ServiceProvider.GetRequiredService<IDataRetention>().PrunedTables;
        }

        foreach (var table in tables)
        {
            await PruneAsync(table, now, cancellationToken);
        }
    }

    private async Task PruneAsync(string table, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var deleted = await scope.ServiceProvider.GetRequiredService<IDataRetention>().PruneAsync(table, now, cancellationToken);
            if (deleted > 0)
            {
                logger.LogInformation("Pruned {Rows} row(s) from {Table}.", deleted, table);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Pruning {Table} failed; the other tables carry on.", table);
        }
    }
}
