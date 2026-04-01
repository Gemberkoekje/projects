using AiUsageMonitor.Events;
using AiUsageMonitor.Models;
using AiUsageMonitor.Services;
using System;
using System.Linq;
using Xunit;

namespace AiUsageMonitor.Tests;

public class SpikeDetectorTests
{
    private static readonly DateTime Now = new(2025, 7, 15, 12, 0, 0, DateTimeKind.Utc);
    private const double DefaultSpikeMultiplier = 2.5;
    private const double DefaultProjectionThreshold = 1.1;



    [Fact]
    public void NoSpike_WhenDeltaBelowThreshold()
    {
        var report = Report(currentDelta: 2000, rollingAverageDelta: 1000, deltaSampleCount: 5);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }

    [Fact]
    public void Spike_WhenDeltaExceedsThreshold()
    {
        // delta 3000 > average 1000 × 2.5 = 2500
        var report = Report(currentDelta: 3000, rollingAverageDelta: 1000, deltaSampleCount: 5);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.Contains(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }

    [Fact]
    public void NoSpike_ExactlyAtThreshold()
    {
        // delta 2500 == average 1000 × 2.5; condition is strictly greater than
        var report = Report(currentDelta: 2500, rollingAverageDelta: 1000, deltaSampleCount: 5);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }

    [Fact]
    public void Spike_JustAboveThreshold()
    {
        // delta 2501 > average 1000 × 2.5 = 2500
        var report = Report(currentDelta: 2501, rollingAverageDelta: 1000, deltaSampleCount: 5);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.Contains(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }





    [Fact]
    public void NoSpike_WhenAlreadyAlerted()
    {
        var report = Report(currentDelta: 5000, rollingAverageDelta: 1000, deltaSampleCount: 5);

        var alerts = SpikeDetector.EvaluateSpikes(report, alreadySpikeAlerted: true, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }

    [Fact]
    public void NoSpike_WhenOnlyOneSample()
    {
        var report = Report(currentDelta: 5000, rollingAverageDelta: 1000, deltaSampleCount: 1);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }

    [Fact]
    public void NoSpike_WhenRollingAverageIsZero()
    {
        var report = Report(currentDelta: 5000, rollingAverageDelta: 0, deltaSampleCount: 5);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }

    [Fact]
    public void NoSpike_WhenDeltaSampleCountIsZero()
    {
        var report = Report(currentDelta: 5000, rollingAverageDelta: 1000, deltaSampleCount: 0);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }





    [Fact]
    public void ProjectionAlert_WhenProjectedExceedsThreshold()
    {
        // Projected 115000 > budget 100000 × 1.1 = 110000
        var report = Report(projectedUsage: 115_000, budget: 100_000, daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.Contains(alerts, a => a.Reason == AlertReasons.ProjectionExceeded);
    }

    [Fact]
    public void NoProjectionAlert_WhenProjectedBelowThreshold()
    {
        // Projected 105000 < budget 100000 × 1.1 = 110000
        var report = Report(projectedUsage: 105_000, budget: 100_000, daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.ProjectionExceeded);
    }

    [Fact]
    public void NoProjectionAlert_ExactlyAtThreshold()
    {
        // Projected 110000 == budget 100000 × 1.1; condition is strictly greater than
        var report = Report(projectedUsage: 110_000, budget: 100_000, daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.ProjectionExceeded);
    }





    [Fact]
    public void NoProjectionAlert_WhenAlreadyAlerted()
    {
        var report = Report(projectedUsage: 200_000, budget: 100_000, daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, alreadyProjectionAlerted: true, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.ProjectionExceeded);
    }

    [Fact]
    public void NoProjectionAlert_WhenDaysElapsedIsZero()
    {
        var report = Report(projectedUsage: 200_000, budget: 100_000, daysElapsed: 0);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.ProjectionExceeded);
    }

    [Fact]
    public void NoProjectionAlert_WhenBudgetIsZero()
    {
        var report = Report(projectedUsage: 200_000, budget: 0, daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.ProjectionExceeded);
    }





    [Fact]
    public void BothAlerts_CanFireSimultaneously()
    {
        var report = Report(
            currentDelta: 5000,
            rollingAverageDelta: 1000,
            deltaSampleCount: 5,
            projectedUsage: 200_000,
            budget: 100_000,
            daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, a => a.Reason == AlertReasons.SpikeDetected);
        Assert.Contains(alerts, a => a.Reason == AlertReasons.ProjectionExceeded);
    }

    [Fact]
    public void NoAlerts_WhenBothAlreadyAlerted()
    {
        var report = Report(
            currentDelta: 5000,
            rollingAverageDelta: 1000,
            deltaSampleCount: 5,
            projectedUsage: 200_000,
            budget: 100_000,
            daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, alreadySpikeAlerted: true, alreadyProjectionAlerted: true, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.Empty(alerts);
    }

    [Fact]
    public void OnlySpikeAlert_WhenProjectionAlreadyAlerted()
    {
        var report = Report(
            currentDelta: 5000,
            rollingAverageDelta: 1000,
            deltaSampleCount: 5,
            projectedUsage: 200_000,
            budget: 100_000,
            daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, alreadyProjectionAlerted: true, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        var single = Assert.Single(alerts);
        Assert.Equal(AlertReasons.SpikeDetected, single.Reason);
    }

    [Fact]
    public void OnlyProjectionAlert_WhenSpikeAlreadyAlerted()
    {
        var report = Report(
            currentDelta: 5000,
            rollingAverageDelta: 1000,
            deltaSampleCount: 5,
            projectedUsage: 200_000,
            budget: 100_000,
            daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, alreadySpikeAlerted: true, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        var single = Assert.Single(alerts);
        Assert.Equal(AlertReasons.ProjectionExceeded, single.Reason);
    }





    [Fact]
    public void SpikeAlert_PopulatesAllFields()
    {
        var report = Report(
            currentDelta: 5000,
            rollingAverageDelta: 1000,
            deltaSampleCount: 5,
            projectedUsage: 150_000,
            budget: 100_000,
            daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        var spike = alerts.Single(a => a.Reason == AlertReasons.SpikeDetected);
        Assert.Equal("copilot", spike.Provider);
        Assert.Equal(Now, spike.Timestamp);
        Assert.Equal(5000, spike.DeltaUsage);
        Assert.Equal(1000, spike.AverageUsage);
        Assert.Equal(DefaultSpikeMultiplier, spike.ThresholdMultiplier);
        Assert.Equal(150_000, spike.ProjectedUsage);
        Assert.Equal(100_000, spike.Budget);
        Assert.Equal(DefaultProjectionThreshold, spike.ProjectionThreshold);
    }

    [Fact]
    public void ProjectionAlert_PopulatesAllFields()
    {
        var report = Report(projectedUsage: 150_000, budget: 100_000, daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        var projection = alerts.Single(a => a.Reason == AlertReasons.ProjectionExceeded);
        Assert.Equal("copilot", projection.Provider);
        Assert.Equal(Now, projection.Timestamp);
        Assert.Equal(DefaultProjectionThreshold, projection.ProjectionThreshold);
        Assert.Equal(150_000, projection.ProjectedUsage);
        Assert.Equal(100_000, projection.Budget);
    }





    [Fact]
    public void CustomMultiplier_LowerThreshold_TriggersSpike()
    {
        // Default 2.5×: delta 2000 vs average 1000 → 2000 < 2500 → no spike
        // Custom 1.5×: delta 2000 vs average 1000 → 2000 > 1500 → spike!
        var report = Report(currentDelta: 2000, rollingAverageDelta: 1000, deltaSampleCount: 5);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, 1.5, DefaultProjectionThreshold, Now);

        Assert.Contains(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }

    [Fact]
    public void CustomMultiplier_HigherThreshold_SuppressesSpike()
    {
        // delta 3000 vs average 1000 × 5 = 5000 → no spike
        var report = Report(currentDelta: 3000, rollingAverageDelta: 1000, deltaSampleCount: 5);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, 5.0, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }

    [Fact]
    public void CustomProjectionThreshold_LowerValue_TriggersAlert()
    {
        // Default 1.1: projected 105000 vs budget 100000 × 1.1 = 110000 → no alert
        // Custom 1.0: projected 105000 vs budget 100000 × 1.0 = 100000 → alert!
        var report = Report(projectedUsage: 105_000, budget: 100_000, daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, 1.0, Now);

        Assert.Contains(alerts, a => a.Reason == AlertReasons.ProjectionExceeded);
    }

    [Fact]
    public void CustomProjectionThreshold_HigherValue_SuppressesAlert()
    {
        // projected 115000 vs budget 100000 × 1.5 = 150000 → no alert
        var report = Report(projectedUsage: 115_000, budget: 100_000, daysElapsed: 10);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, 1.5, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.ProjectionExceeded);
    }





    [Fact]
    public void NoAlerts_WhenReportHasAllZeroes()
    {
        var report = new BurndownReport();

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.Empty(alerts);
    }

    [Fact]
    public void NoSpike_WhenCurrentDeltaIsZero()
    {
        var report = Report(currentDelta: 0, rollingAverageDelta: 1000, deltaSampleCount: 5);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }

    [Fact]
    public void NoSpike_WhenBothDeltaAndAverageAreZero()
    {
        var report = Report(currentDelta: 0, rollingAverageDelta: 0, deltaSampleCount: 5);

        var alerts = SpikeDetector.EvaluateSpikes(report, false, false, DefaultSpikeMultiplier, DefaultProjectionThreshold, Now);

        Assert.DoesNotContain(alerts, a => a.Reason == AlertReasons.SpikeDetected);
    }



    private static BurndownReport Report(
        long currentDelta = 0,
        double rollingAverageDelta = 0,
        int deltaSampleCount = 5,
        long projectedUsage = 0,
        long budget = 100_000,
        int daysElapsed = 15) =>
        new()
        {
            Provider = "copilot",
            CalculatedAt = Now,
            Budget = budget,
            PeriodStart = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            TotalDaysInPeriod = 31,
            DaysElapsed = daysElapsed,
            DaysRemaining = 31 - daysElapsed,
            ActualUsage = 50_000,
            IdealUsage = 48_387,
            ProjectedUsage = projectedUsage,
            RemainingBudget = budget - 50_000,
            CurrentDelta = currentDelta,
            RollingAverageDelta = rollingAverageDelta,
            DeltaSampleCount = deltaSampleCount,
        };
}
