using System;

namespace AiUsageMonitor.Models;

public sealed record BurndownReport
{
    public string Provider { get; init; } = string.Empty;
    public DateTime CalculatedAt { get; init; }

    // Budget and period information
    public long Budget { get; init; }
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public int TotalDaysInPeriod { get; init; }
    public int DaysElapsed { get; init; }
    public int DaysRemaining { get; init; }

    // Consumption metrics
    public long ActualUsage { get; init; }
    public long IdealUsage { get; init; }
    public long ProjectedUsage { get; init; }
    public long RemainingBudget { get; init; }

    // Delta metrics
    public long CurrentDelta { get; init; }
    public double RollingAverageDelta { get; init; }
    public int DeltaSampleCount { get; init; }
    public DateTime? PreviousSnapshotAt { get; init; }

    // Status indicators
    public bool IsOverBudget => ActualUsage > Budget;
    public bool IsProjectedToExceedBudget => ProjectedUsage > Budget;
    public double PercentUsed => Budget > 0 ? (double)ActualUsage / Budget * 100 : 0;
    public double PercentOfIdeal => IdealUsage > 0 ? (double)ActualUsage / IdealUsage * 100 : 0;
}
