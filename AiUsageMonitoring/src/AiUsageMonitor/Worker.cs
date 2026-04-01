using AiUsageMonitor.Clients;
using AiUsageMonitor.Services;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AiUsageMonitor;

public sealed class Worker(
    ILogger<Worker> logger,
    ICopilotUsageClient copilot,
    IBurndownCalculator burndownCalculator,
    ISpikeDetector spikeDetector,
    IDailyReportGate dailyReportGate,
    IEmailService emailService,
    IConfiguration configuration,
    IDocumentStore documentStore,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation("AiUsageMonitor worker starting");

            // Fetch usage snapshot
            var copilotSnapshot = await copilot.GetLatestUsageAsync(stoppingToken).ConfigureAwait(false);

            // Persist snapshot as event in Marten
            await using var session = documentStore.LightweightSession();
            session.Events.Append(copilotSnapshot.Provider, copilotSnapshot);
            await session.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

            // Calculate burndown report
            var copilotBudget = configuration.GetValue<long>("CoPilot:Budget");

            var copilotBurndown = await burndownCalculator.CalculateBurndownAsync(
                copilotSnapshot.Provider,
                copilotBudget,
                stoppingToken).ConfigureAwait(false);

            logger.LogInformation(
                "CoPilot burndown: {PercentUsed:F1}% used, {DaysRemaining} days remaining",
                copilotBurndown.PercentUsed,
                copilotBurndown.DaysRemaining);

            // Detect spikes and persist any fired alerts
            var copilotSpikes = await spikeDetector.DetectAndRecordAsync(copilotBurndown, stoppingToken).ConfigureAwait(false);

            if (copilotSpikes.HasAlerts)
            {
                logger.LogWarning(
                    "CoPilot: {Count} alert(s) fired — {Reasons}",
                    copilotSpikes.FiredAlerts.Count,
                    string.Join(", ", copilotSpikes.FiredAlerts.Select(a => a.Reason)));

                await emailService.SendAlertAsync(copilotSpikes.FiredAlerts, copilotBurndown, stoppingToken).ConfigureAwait(false);
            }

            // Check daily gate — send report if not already sent today
            if (await dailyReportGate.ShouldSendDailyReportAsync(stoppingToken).ConfigureAwait(false))
            {
                logger.LogInformation("Preparing to send daily report");
                await emailService.SendDailyReportAsync(copilotBurndown, stoppingToken).ConfigureAwait(false);
                await dailyReportGate.RecordDailyReportSentAsync(stoppingToken).ConfigureAwait(false);
            }

            logger.LogInformation("Execution cycle complete");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Worker was cancelled before completing the cycle");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error during execution cycle");
        }
        finally
        {
            // Signal the host to shut down so the process exits cleanly after one cycle
            lifetime.StopApplication();
        }
    }
}
