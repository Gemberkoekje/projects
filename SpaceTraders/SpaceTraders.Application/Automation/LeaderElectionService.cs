using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// PostgreSQL-backed leader election service.
/// Runs as a hosted service and attempts to acquire or renew the <c>"game-loop"</c> leader
/// lease every <see cref="RenewInterval"/>. When <see cref="IsLeader"/> returns
/// <c>true</c>, <see cref="GameLoopService"/> is permitted to run its tick.
///
/// The lease expires after <see cref="LeaseDuration"/> allowing another pod to take over
/// if this instance stops renewing (e.g. pod crash). This satisfies the leader-election
/// requirement for <c>replicas &gt; 1</c>.
/// </summary>
public sealed class LeaderElectionService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<LeaderElectionService> logger) : BackgroundService, ILeaderElection
{
    private const string LeaseKey = "game-loop";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(10);

    private readonly string _holderId = Guid.NewGuid().ToString("N");
    private volatile bool _isLeader;

    /// <inheritdoc/>
    public bool IsLeader => _isLeader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TryAcquireOrRenewAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Leader election tick failed; releasing leadership.");
                _isLeader = false;
            }

            await Task.Delay(RenewInterval, stoppingToken);
        }

        _isLeader = false;
        logger.LogInformation("LeaderElectionService stopped; released leadership.");
    }

    private async Task TryAcquireOrRenewAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var leaseRepo = scope.ServiceProvider.GetRequiredService<ILeaderLeaseRepository>();

        var acquired = await leaseRepo.TryAcquireOrRenewAsync(
            LeaseKey, _holderId, LeaseDuration, cancellationToken);

        if (acquired && !_isLeader)
        {
            _isLeader = true;
            logger.LogInformation("This instance ({HolderId}) acquired the leader lease.", _holderId);
        }
        else if (!acquired && _isLeader)
        {
            _isLeader = false;
            logger.LogWarning("This instance ({HolderId}) lost the leader lease.", _holderId);
        }
    }
}
