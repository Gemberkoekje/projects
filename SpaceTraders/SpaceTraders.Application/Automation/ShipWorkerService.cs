using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Keeps automation alive and periodically checks whether automation is enabled.
/// Assignment progression is event-driven through Wolverine handlers.
/// </summary>
public sealed class ShipWorkerService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<ShipWorkerService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ShipWorkerService tick.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();

        var automationEnabled = await settings.GetAsync<bool>("Automation.Enabled", cancellationToken);
        if (!automationEnabled)
        {
            logger.LogTrace("ShipWorkerService: automation disabled; skipping tick.");
            return;
        }

        logger.LogTrace("ShipWorkerService: automation enabled; assignment progression is event-driven.");
    }
}
