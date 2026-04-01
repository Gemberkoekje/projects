using AiUsageMonitor.Events;
using AiUsageMonitor.Models;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Qowaiv;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AiUsageMonitor.Services;

public sealed class SpikeDetector(
    IDocumentStore documentStore,
    IConfiguration configuration,
    ILogger<SpikeDetector> logger) : ISpikeDetector
{
    private readonly double _spikeMultiplier = configuration.GetValue("Burndown:SpikeMultiplier", 2.5);
    private readonly double _projectionThreshold = configuration.GetValue("Burndown:ProjectionThreshold", 1.1);

    public async Task<SpikeDetectionResult> DetectAndRecordAsync(
        BurndownReport report,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Running spike detection for provider: {Provider}", report.Provider);

        await using var session = documentStore.LightweightSession();

        // Scope deduplication to the current delta interval so we never re-fire
        // an alert for a spike we already reported on the previous run.
        var intervalStart = report.PreviousSnapshotAt ?? report.PeriodStart;

        var existingAlerts = await session.Events
            .QueryRawEventDataOnly<AlertFired>()
            .Where(e => e.Provider == report.Provider && e.Timestamp > intervalStart)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var alreadySpikeAlerted = existingAlerts.Any(a => a.Reason == AlertReasons.SpikeDetected);
        var alreadyProjectionAlerted = existingAlerts.Any(a => a.Reason == AlertReasons.ProjectionExceeded);

        var now = Clock.UtcNow();

        var firedAlerts = EvaluateSpikes(report, alreadySpikeAlerted, alreadyProjectionAlerted, _spikeMultiplier, _projectionThreshold, now);

        foreach (var alert in firedAlerts)
        {
            if (alert.Reason == AlertReasons.SpikeDetected && report.RollingAverageDelta > 0)
            {
                logger.LogWarning(
                    "Spike detected for {Provider}: delta {CurrentDelta:N0} is {Ratio:F1}× rolling average {Average:F0} (threshold {Multiplier:F1}×)",
                    report.Provider,
                    report.CurrentDelta,
                    report.CurrentDelta / report.RollingAverageDelta,
                    report.RollingAverageDelta,
                    _spikeMultiplier);
            }
            else if (alert.Reason == AlertReasons.ProjectionExceeded && report.Budget > 0)
            {
                logger.LogWarning(
                    "Projection exceeded for {Provider}: projected {Projected:N0} ({ProjectedPercent:F1}%) exceeds {Threshold:P0} of budget {Budget:N0}",
                    report.Provider,
                    report.ProjectedUsage,
                    (double)report.ProjectedUsage / report.Budget * 100,
                    _projectionThreshold,
                    report.Budget);
            }
        }

        if (firedAlerts.Count > 0)
        {
            foreach (var alert in firedAlerts)
            {
                session.Events.Append(report.Provider, alert);
            }

            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("{Count} alert(s) recorded for provider: {Provider}", firedAlerts.Count, report.Provider);
        }
        else
        {
            logger.LogInformation("No alerts triggered for provider: {Provider}", report.Provider);
        }

        return new SpikeDetectionResult
        {
            Provider = report.Provider,
            FiredAlerts = firedAlerts,
        };
    }

    internal static List<AlertFired> EvaluateSpikes(
        BurndownReport report,
        bool alreadySpikeAlerted,
        bool alreadyProjectionAlerted,
        double spikeMultiplier,
        double projectionThreshold,
        DateTime now)
    {
        var firedAlerts = new List<AlertFired>();

        // Spike check: current delta > rolling average × multiplier.
        // Guard: require at least 2 delta samples so that the rolling average
        // is a meaningful baseline independent of the current interval.
        if (!alreadySpikeAlerted
            && report.DeltaSampleCount >= 2
            && report.RollingAverageDelta > 0
            && report.CurrentDelta > report.RollingAverageDelta * spikeMultiplier)
        {
            firedAlerts.Add(BuildAlert(report, AlertReasons.SpikeDetected, spikeMultiplier, projectionThreshold, now));
        }

        // Projection check: extrapolated end-of-period usage > budget × threshold.
        // Guard: require at least 1 elapsed day so the projection is meaningful.
        if (!alreadyProjectionAlerted
            && report.Budget > 0
            && report.DaysElapsed > 0
            && report.ProjectedUsage > (long)(report.Budget * projectionThreshold))
        {
            firedAlerts.Add(BuildAlert(report, AlertReasons.ProjectionExceeded, spikeMultiplier, projectionThreshold, now));
        }

        return firedAlerts;
    }

    private static AlertFired BuildAlert(BurndownReport report, string reason, double spikeMultiplier, double projectionThreshold, DateTime timestamp) =>
        new ()
        {
            Provider = report.Provider,
            Timestamp = timestamp,
            Reason = reason,
            DeltaUsage = report.CurrentDelta,
            AverageUsage = report.RollingAverageDelta,
            ThresholdMultiplier = spikeMultiplier,
            ProjectedUsage = report.ProjectedUsage,
            Budget = report.Budget,
            ProjectionThreshold = projectionThreshold,
        };
}
