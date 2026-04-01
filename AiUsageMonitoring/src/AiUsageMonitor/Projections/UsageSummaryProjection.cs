using AiUsageMonitor.Events;
using AiUsageMonitor.Models;
using Marten.Events.Aggregation;
using System;
using System.Collections.Immutable;

namespace AiUsageMonitor.Projections;

public sealed class UsageSummaryProjection : SingleStreamProjection<UsageSummary, Guid>
{
    public UsageSummary Create(UsageSnapshotRecorded @event)
    {
        return new UsageSummary
        {
            Id = Guid.NewGuid(),
            Provider = @event.Provider,
            PeriodStart = @event.PeriodStart,
            PeriodEnd = @event.PeriodEnd,
            TotalTokensUsed = @event.TokensUsed,
            TotalSeatsUsed = @event.SeatsUsed,
            TotalCostIncurred = @event.CostIncurred,
            UsageHistory = [
                new UsageDataPoint
                {
                    Timestamp = @event.Timestamp,
                    TokensDelta = @event.TokensUsed,
                    SeatsDelta = @event.SeatsUsed,
                    CostDelta = @event.CostIncurred,
                }
            ],
            LastUpdated = @event.Timestamp,
        };
    }

    public UsageSummary Apply(UsageSnapshotRecorded @event, UsageSummary current)
    {
        // Calculate deltas against the current aggregate totals (immutable pattern)
        var previousTotalTokens = current.TotalTokensUsed;
        var previousTotalSeats = current.TotalSeatsUsed;
        var previousTotalCost = current.TotalCostIncurred;

        long tokensDelta = @event.TokensUsed - previousTotalTokens;
        int seatsDelta = @event.SeatsUsed - previousTotalSeats;
        var costDelta = @event.CostIncurred - previousTotalCost;

        var newHistory = current.UsageHistory.Add(new UsageDataPoint
        {
            Timestamp = @event.Timestamp,
            TokensDelta = tokensDelta,
            SeatsDelta = seatsDelta,
            CostDelta = costDelta,
        });

        return current with
        {
            TotalTokensUsed = @event.TokensUsed,
            TotalSeatsUsed = @event.SeatsUsed,
            TotalCostIncurred = @event.CostIncurred,
            PeriodStart = @event.PeriodStart,
            PeriodEnd = @event.PeriodEnd,
            LastUpdated = @event.Timestamp,
            UsageHistory = newHistory
        };
    }
}
