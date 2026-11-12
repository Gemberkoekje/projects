using Microsoft.Extensions.Hosting;

namespace FrontPage.WebApp;

/// <summary>Refreshes <see cref="ClusterStatus"/> at start-up and then at a fixed interval.</summary>
internal sealed class ClusterStatusRefresher(ClusterStatus status, TimeSpan interval, TimeProvider time) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval, time);
        do
        {
            // A failed read is logged and leaves the previous summary in place; the next tick retries.
            await status.RefreshAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
