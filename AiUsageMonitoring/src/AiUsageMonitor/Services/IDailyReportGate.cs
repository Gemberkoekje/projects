using System.Threading;
using System.Threading.Tasks;

namespace AiUsageMonitor.Services;

/// <summary>
/// Manages the daily report gate logic to prevent sending multiple reports per day.
/// </summary>
public interface IDailyReportGate
{
    /// <summary>
    /// Determines whether a daily report should be sent today.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a report should be sent, false if one was already sent today.</returns>
    Task<bool> ShouldSendDailyReportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a daily report was sent for today.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordDailyReportSentAsync(CancellationToken cancellationToken = default);
}
