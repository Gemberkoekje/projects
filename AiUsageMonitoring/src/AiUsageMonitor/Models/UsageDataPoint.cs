using System;

namespace AiUsageMonitor.Models;

public sealed record UsageDataPoint
{
    public DateTime Timestamp { get; init; }
    public long TokensDelta { get; init; }
    public int SeatsDelta { get; init; }
    public decimal CostDelta { get; init; }
}
