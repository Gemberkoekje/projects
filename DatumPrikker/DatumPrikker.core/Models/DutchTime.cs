namespace DatumPrikker.core.Models;

/// <summary>Local time as DatumPrikker means it: every day and time people type in is Amsterdam time.</summary>
public static class DutchTime
{
    /// <summary>Amsterdam, with its summer time.</summary>
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>A local day and time as a UTC moment.</summary>
    /// <param name="date">The local day.</param>
    /// <param name="time">The local time. One skipped by the switch to summer time counts as an hour later.</param>
    /// <returns>The moment in UTC.</returns>
    public static DateTime ToUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, Zone);
    }

    /// <summary>A UTC moment as local time.</summary>
    /// <param name="utc">The moment in UTC.</param>
    /// <returns>The local time.</returns>
    public static DateTime FromUtc(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>The moment a local day ends: midnight at the start of the next day, in UTC.</summary>
    /// <param name="date">The local day.</param>
    /// <returns>The moment in UTC.</returns>
    public static DateTime EndOfDay(DateOnly date)
    {
        // The day's last tick plus one, rather than the next day's midnight, so the last day there is
        // doesn't overflow. Amsterdam never switches to or from summer time at midnight.
        return ToUtc(date, TimeOnly.MaxValue).AddTicks(1);
    }
}
