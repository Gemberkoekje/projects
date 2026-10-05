namespace SpaceTraders.Application.Trading;

/// <summary>
/// What trade trips have actually earned (PLAN.md slice 6.21, D87): the trip book notes each trade trip that ends, with when it
/// began and ended and what it made after fuel, and the role board caps a ship's trade estimate at what the trips that ended
/// in the last <see cref="Window"/> made per hour of a trader's time. Asked on 2026-10-05, when the board estimated SPECTER-1's
/// trading at 0.14 to 1.77 million an hour, one trip's profit over its flight time, while its trading made about 83,000 an
/// hour: "Cap at realized". In memory: a start begins with none, and no cap.
/// </summary>
/// <remarks>Thread-safe: trips end on several goal steps at once.</remarks>
public sealed class TradeEarnings
{
    /// <summary>How far back the rate looks: the trade trips that ended in the last two hours.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(2);

    /// <summary>The least time a trip counts for, so a sale where the cargo already lay isn't an hour's worth in no time.</summary>
    private static readonly TimeSpan ShortestTrip = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly List<(DateTimeOffset EndedAt, TimeSpan Took, long Profit)> _trips = [];

    /// <summary>Notes a trade trip that ended.</summary>
    /// <param name="startedAt">When it began.</param>
    /// <param name="endedAt">When it ended.</param>
    /// <param name="profit">What it made after fuel, as the trip book booked it.</param>
    public void Ended(DateTimeOffset startedAt, DateTimeOffset endedAt, long profit)
    {
        var took = endedAt - startedAt;
        lock (_gate)
        {
            _trips.Add((endedAt, took < ShortestTrip ? ShortestTrip : took, profit));
            _trips.RemoveAll(trip => trip.EndedAt <= endedAt - Window);
        }
    }

    /// <summary>
    /// What the trade trips that ended in the last <see cref="Window"/> made per hour of their time: their profits over their
    /// hours. Infinite while none ended in it: then nothing caps an estimate.
    /// </summary>
    /// <param name="now">The time.</param>
    /// <returns>The credits per hour.</returns>
    public double PerHour(DateTimeOffset now)
    {
        lock (_gate)
        {
            var recent = _trips.Where(trip => trip.EndedAt > now - Window && trip.EndedAt <= now).ToList();
            return recent.Count == 0
                ? double.PositiveInfinity
                : recent.Sum(trip => trip.Profit) / recent.Sum(trip => trip.Took.TotalHours);
        }
    }
}
