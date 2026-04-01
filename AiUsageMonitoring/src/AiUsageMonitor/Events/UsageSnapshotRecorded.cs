using System;
using System.Collections.Immutable;

namespace AiUsageMonitor.Events;

public sealed record UsageSnapshotRecorded
{
    public string Provider { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public int SeatsUsed { get; init; }
    public long TokensUsed { get; init; }
    public decimal CostIncurred { get; init; }
    public ImmutableDictionary<string, object> AdditionalMetadata { get; init; } = [];
}
