using AiUsageMonitor.Events;
using Marten;
using Microsoft.Extensions.Logging;
using Qowaiv;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AiUsageMonitor.Services;

/// <summary>
/// Implements the daily report gate using Marten event sourcing.
/// Ensures only one daily report is sent per calendar day (in local timezone).
/// </summary>
public sealed class DailyReportGate(IDocumentStore documentStore, ILogger<DailyReportGate> logger) : IDailyReportGate
{
    public async Task<bool> ShouldSendDailyReportAsync(CancellationToken cancellationToken = default)
    {
        var today = TimeZoneInfo.ConvertTime(Clock.UtcNow(), TimeZoneInfo.Local).Date;
        var todayDateOnly = DateOnly.FromDateTime(today);

        logger.LogInformation("Checking if daily report was already sent for {Date}", todayDateOnly);

        await using var session = documentStore.LightweightSession();

        // Query the event stream for DailyReportSent events and find the most recent one
        var mostRecentReport = await session.Events
            .QueryRawEventDataOnly<DailyReportSent>()
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (mostRecentReport != null && mostRecentReport.ReportDate == todayDateOnly)
        {
            logger.LogInformation("Daily report already sent for {Date} at {Time}", todayDateOnly, mostRecentReport.Timestamp);
            return false;
        }

        logger.LogInformation("Daily report not yet sent for {Date}, proceeding", todayDateOnly);
        return true;
    }

    public async Task RecordDailyReportSentAsync(CancellationToken cancellationToken = default)
    {
        var today = TimeZoneInfo.ConvertTime(Clock.UtcNow(), TimeZoneInfo.Local).Date;
        var todayDateOnly = DateOnly.FromDateTime(today);
        var now = Clock.UtcNow();

        logger.LogInformation("Recording daily report sent event for {Date}", todayDateOnly);

        var dailyReportEvent = new DailyReportSent
        {
            Timestamp = now,
            ReportDate = todayDateOnly,
        };

        await using var session = documentStore.LightweightSession();
        session.Events.Append("DailyReport", dailyReportEvent);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Daily report event recorded successfully");
    }
}
