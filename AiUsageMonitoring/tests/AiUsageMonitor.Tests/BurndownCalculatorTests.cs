using AiUsageMonitor.Events;
using AiUsageMonitor.Models;
using AiUsageMonitor.Services;
using System;
using System.Collections.Generic;
using Xunit;

namespace AiUsageMonitor.Tests;

public class BurndownCalculatorTests
{
    private static readonly DateTime PeriodStart = new(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private const long Budget = 100_000;
    private const string Provider = "copilot";
    private const int DefaultWindowSize = 7;



    [Fact]
    public void EmptySnapshots_ReturnsEmptyReport()
    {
        var now = new DateTime(2025, 7, 15, 12, 0, 0, DateTimeKind.Utc);

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, [], DefaultWindowSize, now);

        Assert.Equal(Provider, report.Provider);
        Assert.Equal(Budget, report.Budget);
        Assert.Equal(now, report.CalculatedAt);
        Assert.Equal(0, report.ActualUsage);
        Assert.Equal(0, report.IdealUsage);
        Assert.Equal(0, report.ProjectedUsage);
        Assert.Equal(Budget, report.RemainingBudget);
        Assert.Equal(0, report.TotalDaysInPeriod);
        Assert.Equal(0, report.DaysElapsed);
        Assert.Equal(0, report.DaysRemaining);
        Assert.Equal(0, report.CurrentDelta);
        Assert.Equal(0, report.RollingAverageDelta);
        Assert.Equal(0, report.DeltaSampleCount);
        Assert.Null(report.PreviousSnapshotAt);
    }





    [Fact]
    public void SingleSnapshot_DeltaEqualsTokensUsed()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 500),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(1));

        Assert.Equal(500, report.CurrentDelta);
        Assert.Null(report.PreviousSnapshotAt);
    }

    [Fact]
    public void SingleSnapshot_RollingAverageEqualsTokensUsed()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 500),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(1));

        Assert.Equal(500, report.RollingAverageDelta);
        Assert.Equal(1, report.DeltaSampleCount);
    }

    [Fact]
    public void SingleSnapshot_PeriodMetricsCorrect()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(5), tokensUsed: 10_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(5));

        Assert.Equal(31, report.TotalDaysInPeriod);
        Assert.Equal(5, report.DaysElapsed);
        Assert.Equal(26, report.DaysRemaining);
        Assert.Equal(10_000, report.ActualUsage);
    }





    [Fact]
    public void TwoSnapshots_DeltaIsTokenDifference()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 500),
            Snapshot(PeriodStart.AddDays(2), tokensUsed: 1500),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(2));

        Assert.Equal(1000, report.CurrentDelta);
    }

    [Fact]
    public void TwoSnapshots_RollingAverageMatchesSingleDelta()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 500),
            Snapshot(PeriodStart.AddDays(2), tokensUsed: 1500),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(2));

        Assert.Equal(1000, report.RollingAverageDelta);
        Assert.Equal(2, report.DeltaSampleCount);
        Assert.Equal(snapshots[0].Timestamp, report.PreviousSnapshotAt);
    }





    [Fact]
    public void RollingAverage_UniformDeltas_AverageEqualsEachDelta()
    {
        var snapshots = new List<UsageSnapshotRecorded>();
        for (int i = 0; i < 10; i++)
        {
            snapshots.Add(Snapshot(PeriodStart.AddDays(i + 1), tokensUsed: (i + 1) * 1000));
        }

        // With window size 3, rolling average should use last 3 deltas (each is 1000)
        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, 3, PeriodStart.AddDays(10));

        Assert.Equal(1000, report.RollingAverageDelta);
        Assert.Equal(3, report.DeltaSampleCount);
    }

    [Fact]
    public void RollingAverage_FewerSnapshotsThanWindow_UsesAllAvailable()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 1000),
            Snapshot(PeriodStart.AddDays(2), tokensUsed: 3000),
            Snapshot(PeriodStart.AddDays(3), tokensUsed: 4000),
        };

        // Window is 7 but only 3 snapshots → 2 deltas: 2000, 1000 → average 1500
        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, 7, PeriodStart.AddDays(3));

        Assert.Equal(1500, report.RollingAverageDelta);
        Assert.Equal(3, report.DeltaSampleCount);
    }

    [Fact]
    public void RollingAverage_VaryingDeltas_WindowSelectsCorrectTail()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 1000),   // base
            Snapshot(PeriodStart.AddDays(2), tokensUsed: 2000),   // delta: 1000
            Snapshot(PeriodStart.AddDays(3), tokensUsed: 5000),   // delta: 3000
            Snapshot(PeriodStart.AddDays(4), tokensUsed: 6000),   // delta: 1000
            Snapshot(PeriodStart.AddDays(5), tokensUsed: 10000),  // delta: 4000
            Snapshot(PeriodStart.AddDays(6), tokensUsed: 11000),  // delta: 1000
        };

        // Window size 3: last 3 deltas are 1000, 4000, 1000 → average 2000
        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, 3, PeriodStart.AddDays(6));

        Assert.Equal(2000, report.RollingAverageDelta);
    }

    [Fact]
    public void RollingAverage_LargeWindow_UsesAllDeltas()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 0),
            Snapshot(PeriodStart.AddDays(2), tokensUsed: 100),
            Snapshot(PeriodStart.AddDays(3), tokensUsed: 300),
            Snapshot(PeriodStart.AddDays(4), tokensUsed: 600),
        };

        // Window size 100 → all 3 deltas: 100, 200, 300 → average 200
        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, 100, PeriodStart.AddDays(4));

        Assert.Equal(200, report.RollingAverageDelta);
    }





    [Fact]
    public void ZeroDeltas_ForSeveralRuns_DeltaAndAverageAreZero()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 5000),
            Snapshot(PeriodStart.AddDays(2), tokensUsed: 5000),
            Snapshot(PeriodStart.AddDays(3), tokensUsed: 5000),
            Snapshot(PeriodStart.AddDays(4), tokensUsed: 5000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(4));

        Assert.Equal(0, report.CurrentDelta);
        Assert.Equal(0, report.RollingAverageDelta);
        Assert.Equal(5000, report.ActualUsage);
    }

    [Fact]
    public void ZeroDeltas_ThenSpike_DeltaReflectsSpike()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 5000),
            Snapshot(PeriodStart.AddDays(2), tokensUsed: 5000),
            Snapshot(PeriodStart.AddDays(3), tokensUsed: 5000),
            Snapshot(PeriodStart.AddDays(4), tokensUsed: 15000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(4));

        Assert.Equal(10000, report.CurrentDelta);
    }





    [Fact]
    public void IdealUsage_Midway_ProportionalToBudget()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(15), tokensUsed: 50_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(15));

        // Ideal: 100000 * 15/31 ≈ 48387
        var expectedIdeal = (long)(Budget * (15.0 / 31));
        Assert.Equal(expectedIdeal, report.IdealUsage);
    }

    [Fact]
    public void IdealUsage_AtPeriodEnd_EqualsBudget()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodEnd.AddDays(-1), tokensUsed: 90_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodEnd);

        // At end of period, ideal should equal budget
        Assert.Equal(Budget, report.IdealUsage);
    }

    [Fact]
    public void IdealUsage_AtPeriodStart_IsZero()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart, tokensUsed: 0),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart);

        Assert.Equal(0, report.DaysElapsed);
        Assert.Equal(0, report.IdealUsage);
    }





    [Fact]
    public void ProjectedUsage_ExtrapolatesFromCurrentPace()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(10), tokensUsed: 50_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(10));

        // Projected: 50000 * 31/10 = 155000
        var expectedProjection = (long)(50_000L * (31.0 / 10));
        Assert.Equal(expectedProjection, report.ProjectedUsage);
    }

    [Fact]
    public void ProjectedUsage_DayZero_FallsBackToActualUsage()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart, tokensUsed: 1000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart);

        Assert.Equal(0, report.DaysElapsed);
        Assert.Equal(1000, report.ProjectedUsage);
    }

    [Fact]
    public void ProjectedUsage_OnTrack_MatchesBudget()
    {
        // 50k used in 15.5 days of a 31-day period → projection ≈ 100k
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(15), tokensUsed: 50_000),
        };

        // Using a time where daysElapsed = 15 (half of 30.x)
        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(15));

        // Projected: 50000 * 31/15 ≈ 103333
        var expectedProjection = (long)(50_000L * (31.0 / 15));
        Assert.Equal(expectedProjection, report.ProjectedUsage);
    }





    [Fact]
    public void DaysElapsed_ClampedToZero_WhenBeforePeriodStart()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(-1), tokensUsed: 0),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(-2));

        Assert.Equal(0, report.DaysElapsed);
        Assert.Equal(31, report.DaysRemaining);
    }

    [Fact]
    public void DaysElapsed_ClampedToTotalDays_WhenPastPeriodEnd()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodEnd.AddDays(-1), tokensUsed: 90_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodEnd.AddDays(5));

        Assert.Equal(report.TotalDaysInPeriod, report.DaysElapsed);
        Assert.Equal(0, report.DaysRemaining);
    }





    [Fact]
    public void RemainingBudget_PositiveWhenUnderBudget()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(10), tokensUsed: 30_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(10));

        Assert.Equal(70_000, report.RemainingBudget);
        Assert.False(report.IsOverBudget);
    }

    [Fact]
    public void RemainingBudget_NegativeWhenOverBudget()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(20), tokensUsed: 120_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(20));

        Assert.Equal(-20_000, report.RemainingBudget);
        Assert.True(report.IsOverBudget);
    }





    [Fact]
    public void PercentUsed_QuarterBudget_Returns25Percent()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(15), tokensUsed: 25_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(15));

        Assert.Equal(25.0, report.PercentUsed);
    }

    [Fact]
    public void IsProjectedToExceedBudget_True_WhenPaceIsHigh()
    {
        // 50k in 10 days → projected 155k > 100k
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(10), tokensUsed: 50_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(10));

        Assert.True(report.IsProjectedToExceedBudget);
    }

    [Fact]
    public void IsProjectedToExceedBudget_False_WhenPaceIsLow()
    {
        // 10k in 15 days → projected ≈ 20.7k < 100k
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(15), tokensUsed: 10_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(15));

        Assert.False(report.IsProjectedToExceedBudget);
    }

    [Fact]
    public void PercentOfIdeal_Over100_WhenAheadOfSchedule()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(10), tokensUsed: 50_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(10));

        // Ideal ≈ 32258, actual = 50000 → 155% of ideal
        Assert.True(report.PercentOfIdeal > 100);
    }

    [Fact]
    public void PercentOfIdeal_Under100_WhenBehindSchedule()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(15), tokensUsed: 10_000),
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, PeriodStart.AddDays(15));

        // Ideal ≈ 48387, actual = 10000 → ~20% of ideal
        Assert.True(report.PercentOfIdeal < 100);
    }





    [Fact]
    public void CalculateCurrentDelta_SingleSnapshot_ReturnsTokensUsed()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 3000),
        };

        var delta = BurndownCalculator.CalculateCurrentDelta(snapshots);

        Assert.Equal(3000, delta);
    }

    [Fact]
    public void CalculateCurrentDelta_TwoSnapshots_ReturnsDifference()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 1000),
            Snapshot(PeriodStart.AddDays(2), tokensUsed: 4500),
        };

        var delta = BurndownCalculator.CalculateCurrentDelta(snapshots);

        Assert.Equal(3500, delta);
    }

    [Fact]
    public void CalculateCurrentDelta_ManySnapshots_UsesOnlyLastTwo()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 1000),
            Snapshot(PeriodStart.AddDays(2), tokensUsed: 5000),
            Snapshot(PeriodStart.AddDays(3), tokensUsed: 5200),
        };

        var delta = BurndownCalculator.CalculateCurrentDelta(snapshots);

        Assert.Equal(200, delta);
    }





    [Fact]
    public void CalculateRollingAverage_SingleSnapshot_ReturnsTokensUsed()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 2500),
        };

        var avg = BurndownCalculator.CalculateRollingAverageDelta(snapshots, 7);

        Assert.Equal(2500, avg);
    }

    [Fact]
    public void CalculateRollingAverage_EmptySnapshots_ReturnsZero()
    {
        var avg = BurndownCalculator.CalculateRollingAverageDelta([], 7);

        Assert.Equal(0, avg);
    }

    [Fact]
    public void CalculateRollingAverage_WindowSmallerThanData_SelectsTail()
    {
        var snapshots = new List<UsageSnapshotRecorded>
        {
            Snapshot(PeriodStart.AddDays(1), tokensUsed: 0),
            Snapshot(PeriodStart.AddDays(2), tokensUsed: 100),   // delta: 100
            Snapshot(PeriodStart.AddDays(3), tokensUsed: 200),   // delta: 100
            Snapshot(PeriodStart.AddDays(4), tokensUsed: 500),   // delta: 300
            Snapshot(PeriodStart.AddDays(5), tokensUsed: 600),   // delta: 100
            Snapshot(PeriodStart.AddDays(6), tokensUsed: 1600),  // delta: 1000
        };

        // Window 2: last 2 deltas are 100, 1000 → average 550
        var avg = BurndownCalculator.CalculateRollingAverageDelta(snapshots, 2);

        Assert.Equal(550, avg);
    }





    [Fact]
    public void ZeroDayPeriod_IdealUsageIsZero()
    {
        var sameDay = new DateTime(2025, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        var snapshots = new List<UsageSnapshotRecorded>
        {
            new()
            {
                Provider = Provider,
                Timestamp = sameDay,
                PeriodStart = sameDay,
                PeriodEnd = sameDay,
                TokensUsed = 5000,
            },
        };

        var report = BurndownCalculator.CalculateFromSnapshots(Provider, Budget, snapshots, DefaultWindowSize, sameDay);

        Assert.Equal(0, report.TotalDaysInPeriod);
        Assert.Equal(0, report.IdealUsage);
    }



    private static UsageSnapshotRecorded Snapshot(DateTime timestamp, long tokensUsed) =>
        new()
        {
            Provider = Provider,
            Timestamp = timestamp,
            PeriodStart = PeriodStart,
            PeriodEnd = PeriodEnd,
            TokensUsed = tokensUsed,
        };
}
