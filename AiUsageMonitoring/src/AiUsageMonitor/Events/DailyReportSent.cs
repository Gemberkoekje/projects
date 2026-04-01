using System;

namespace AiUsageMonitor.Events;

public sealed record DailyReportSent
{
    public DateTime Timestamp { get; init; }
    public DateOnly ReportDate { get; init; }
}
