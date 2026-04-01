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

public sealed class BurndownCalculator(
    IDocumentStore documentStore,
    IConfiguration configuration,
    ILogger<BurndownCalculator> logger) : IBurndownCalculator
{
    private readonly int _rollingWindowSize = configuration.GetValue("Burndown:RollingWindowSize", 7);

    public async Task<BurndownReport> CalculateBurndownAsync(
        string provider,
        long budget,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Calculating burndown for provider: {Provider}", provider);

        await using var session = documentStore.LightweightSession();

        // Fetch all usage snapshots for this provider, ordered by timestamp
        var snapshots = await session.Events
            .QueryRawEventDataOnly<UsageSnapshotRecorded>()
            .Where(e => e.Provider == provider)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (snapshots.Count == 0)
        {
            logger.LogWarning("No usage snapshots found for provider: {Provider}", provider);
        }

        var report = CalculateFromSnapshots(provider, budget, snapshots, _rollingWindowSize, Clock.UtcNow());

        if (snapshots.Count > 0)
        {
            logger.LogInformation(
                "Burndown calculated for {Provider}: {PercentUsed:F1}% used, projected {ProjectedUsage}/{Budget}",
                provider,
                report.PercentUsed,
                report.ProjectedUsage,
                report.Budget);
        }

        return report;
    }

    internal static BurndownReport CalculateFromSnapshots(
        string provider,
        long budget,
        IReadOnlyList<UsageSnapshotRecorded> snapshots,
        int rollingWindowSize,
        DateTime now)
    {
        if (snapshots.Count == 0)
        {
            return new BurndownReport
            {
                Provider = provider,
                CalculatedAt = now,
                Budget = budget,
                PeriodStart = now,
                PeriodEnd = now,
                TotalDaysInPeriod = 0,
                DaysElapsed = 0,
                DaysRemaining = 0,
                ActualUsage = 0,
                IdealUsage = 0,
                ProjectedUsage = 0,
                RemainingBudget = budget,
                CurrentDelta = 0,
                RollingAverageDelta = 0,
                DeltaSampleCount = 0,
            };
        }

        var latestSnapshot = snapshots[^1];

        // Calculate period metrics
        var periodStart = latestSnapshot.PeriodStart;
        var periodEnd = latestSnapshot.PeriodEnd;
        var totalDaysInPeriod = (int)(periodEnd - periodStart).TotalDays;
        var daysElapsed = (int)(now - periodStart).TotalDays;

        // Ensure we don't have negative days or exceed the period
        if (daysElapsed < 0)
            daysElapsed = 0;
        if (daysElapsed > totalDaysInPeriod)
            daysElapsed = totalDaysInPeriod;

        var daysRemaining = totalDaysInPeriod - daysElapsed;
        if (daysRemaining < 0)
            daysRemaining = 0;

        // Calculate actual usage (using tokens as the primary metric)
        var actualUsage = latestSnapshot.TokensUsed;

        // Calculate ideal usage (what should have been consumed by now at a steady pace)
        var idealUsage = totalDaysInPeriod > 0
            ? (long)(budget * ((double)daysElapsed / totalDaysInPeriod))
            : 0;

        // Calculate projected usage (where we'll end up if current pace continues)
        var projectedUsage = daysElapsed > 0
            ? (long)(actualUsage * ((double)totalDaysInPeriod / daysElapsed))
            : actualUsage;

        // Calculate delta metrics
        var currentDelta = CalculateCurrentDelta(snapshots);
        var rollingAverageDelta = CalculateRollingAverageDelta(snapshots, rollingWindowSize);
        var deltaSampleCount = Math.Min(snapshots.Count, rollingWindowSize);
        DateTime? previousSnapshotAt = snapshots.Count >= 2 ? snapshots[^2].Timestamp : null;

        return new BurndownReport
        {
            Provider = provider,
            CalculatedAt = now,
            Budget = budget,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            TotalDaysInPeriod = totalDaysInPeriod,
            DaysElapsed = daysElapsed,
            DaysRemaining = daysRemaining,
            ActualUsage = actualUsage,
            IdealUsage = idealUsage,
            ProjectedUsage = projectedUsage,
            RemainingBudget = budget - actualUsage,
            CurrentDelta = currentDelta,
            RollingAverageDelta = rollingAverageDelta,
            DeltaSampleCount = deltaSampleCount,
            PreviousSnapshotAt = previousSnapshotAt,
        };
    }

    internal static long CalculateCurrentDelta(IReadOnlyList<UsageSnapshotRecorded> snapshots)
    {
        if (snapshots.Count < 2)
        {
            // If only one snapshot, delta is the usage itself
            return snapshots[0].TokensUsed;
        }

        // Delta is the difference between the last two snapshots
        var latest = snapshots[^1];
        var previous = snapshots[^2];
        return latest.TokensUsed - previous.TokensUsed;
    }

    internal static double CalculateRollingAverageDelta(
        IReadOnlyList<UsageSnapshotRecorded> snapshots,
        int windowSize)
    {
        if (snapshots.Count < 2)
        {
            // Not enough data for delta calculation
            return snapshots.Count == 1 ? snapshots[0].TokensUsed : 0;
        }

        // Calculate deltas for the last N snapshots (or all if fewer than N)
        var effectiveWindowSize = Math.Min(snapshots.Count, windowSize + 1);
        var relevantSnapshots = snapshots.Skip(snapshots.Count - effectiveWindowSize).ToList();

        var deltas = new List<long>();
        for (int i = 1; i < relevantSnapshots.Count; i++)
        {
            var delta = relevantSnapshots[i].TokensUsed - relevantSnapshots[i - 1].TokensUsed;
            deltas.Add(delta);
        }

        return deltas.Count > 0 ? deltas.Average() : 0;
    }
}
