using System;
using System.Collections.Immutable;

namespace AiUsageMonitor.Models;

public sealed record UsageSummary
{
    public Guid Id { get; init; }
    public string Provider { get; init; } = string.Empty;
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public long TotalTokensUsed { get; init; }
    public int TotalSeatsUsed { get; init; }
    public decimal TotalCostIncurred { get; init; }
    public ImmutableList<UsageDataPoint> UsageHistory { get; init; } = [];
    public DateTime LastUpdated { get; init; }
}
