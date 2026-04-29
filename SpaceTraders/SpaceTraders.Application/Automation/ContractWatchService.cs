using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Background service that monitors contract deadlines and publishes warning events.
/// Every 5 minutes: loads all accepted, unfulfilled contracts and fires
/// <see cref="ContractDeadlineApproachingEvent"/> at 24 h and 6 h thresholds.
/// Warning state is tracked in memory (acceptable: at most one duplicate per restart).
/// </summary>
public sealed class ContractWatchService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<ContractWatchService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Warning24H = TimeSpan.FromHours(24);
    private static readonly TimeSpan Warning6H = TimeSpan.FromHours(6);

    private readonly HashSet<(string ContractId, string WarningKey)> _sentWarnings = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckContractsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ContractWatchService tick.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    internal Task InvokeCheckContractsForTestAsync(CancellationToken cancellationToken)
        => CheckContractsAsync(cancellationToken);

    private async Task CheckContractsAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var contracts = scope.ServiceProvider.GetRequiredService<IContractRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var activeContracts = await contracts.GetActiveAsync(cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        foreach (var contract in activeContracts)
        {
            if (!contract.IsAccepted || contract.IsFulfilled) continue;

            var deadline = contract.TermsDeadline ?? contract.Expiration;
            if (deadline is null) continue;

            var remaining = deadline.Value - now;

            if (remaining <= TimeSpan.Zero)
            {
                logger.LogWarning("Contract {ContractId} has expired (deadline: {Deadline}).", contract.Id, deadline);
                continue;
            }

            if (remaining <= Warning24H && _sentWarnings.Add((contract.Id, "24h")))
            {
                logger.LogInformation("Contract {ContractId} deadline approaching: {Remaining} remaining.", contract.Id, remaining);
                await bus.PublishAsync(new ContractDeadlineApproachingEvent(contract.Id, remaining));
            }

            if (remaining <= Warning6H && _sentWarnings.Add((contract.Id, "6h")))
            {
                logger.LogWarning("Contract {ContractId} deadline critical: {Remaining} remaining.", contract.Id, remaining);
                await bus.PublishAsync(new ContractDeadlineApproachingEvent(contract.Id, remaining));
            }
        }
    }
}
