using AiUsageMonitor.Events;
using AiUsageMonitor.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AiUsageMonitor.Services;

public interface IEmailService
{
    /// <summary>
    /// Sends a spike/projection alert email for a single provider.
    /// </summary>
    Task SendAlertAsync(
        IReadOnlyList<AlertFired> alerts,
        BurndownReport report,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the daily burndown report email.
    /// </summary>
    Task SendDailyReportAsync(
        BurndownReport copilotReport,
        CancellationToken cancellationToken = default);
}
