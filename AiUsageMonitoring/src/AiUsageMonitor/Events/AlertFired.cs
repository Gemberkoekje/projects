using System;

namespace AiUsageMonitor.Events;

public sealed record AlertFired
{
    public string Provider { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public string Reason { get; init; } = string.Empty;
    // Spike detection fields
    public long DeltaUsage { get; init; }
    public double AverageUsage { get; init; }
    public double ThresholdMultiplier { get; init; }
    // Projection fields
    public long ProjectedUsage { get; init; }
    public long Budget { get; init; }
    public double ProjectionThreshold { get; init; }
}
