using System;
using System.Globalization;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// Game time (design §8.3, D21). One tick is one game hour and tick 0 is Day 0 · 00:00; earlier ticks
/// (a ship's history before the game starts) are negative days. The interface shows dates and durations,
/// never raw ticks: "Day 4 · 14:00", "1d 6h", "in 1d 6h".
/// </summary>
public static class GameTime
{
    public const long HoursPerDay = 24;

    // The typographic minus, as the mockup sets negative days ("Day −410").
    private const string Minus = "\u2212";

    /// <summary>The day a tick falls on. Floors, so tick −1 is the last hour of Day −1.</summary>
    public static long Day(long tick)
    {
        long day = tick / HoursPerDay;
        return tick % HoursPerDay < 0 ? day - 1 : day;
    }

    public static int HourOfDay(long tick) => (int)(tick - (Day(tick) * HoursPerDay));

    /// <summary>The tick of an hour on a day.</summary>
    public static long At(long day, int hour)
    {
        if (hour is < 0 or >= (int)HoursPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), hour, "An hour of the day is 0 to 23.");
        }

        return (day * HoursPerDay) + hour;
    }

    /// <summary>A duration of whole days, in ticks.</summary>
    public static long Days(long days) => days * HoursPerDay;

    /// <summary>"Day 4 · 14:00".</summary>
    public static string FormatDate(long tick)
    {
        long day = Day(tick);
        string dayText = day < 0
            ? Minus + (-day).ToString(CultureInfo.InvariantCulture)
            : day.ToString(CultureInfo.InvariantCulture);
        return $"Day {dayText} · {HourOfDay(tick).ToString("00", CultureInfo.InvariantCulture)}:00";
    }

    /// <summary>"1d 6h", "2d", "9h", "0h". A duration is never negative; see <see cref="FormatRelative"/>.</summary>
    public static string FormatDuration(long ticks)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "A duration is never negative.");
        }

        long days = ticks / HoursPerDay;
        long hours = ticks % HoursPerDay;
        string d = days.ToString(CultureInfo.InvariantCulture) + "d";
        string h = hours.ToString(CultureInfo.InvariantCulture) + "h";
        if (days == 0)
        {
            return h;
        }

        return hours == 0 ? d : d + " " + h;
    }

    /// <summary>When <paramref name="tick"/> is, seen from <paramref name="now"/>: "in 1d 6h", "3h ago", "now".</summary>
    public static string FormatRelative(long tick, long now)
    {
        long delta = tick - now;
        if (delta == 0)
        {
            return "now";
        }

        return delta > 0 ? "in " + FormatDuration(delta) : FormatDuration(-delta) + " ago";
    }
}
