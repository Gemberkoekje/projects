namespace Kluskat.core.Models;

/// <summary>
/// The household runs on Amsterdam time: "today", "this week" and the moment a klus becomes due are
/// all local. Every instant the domain stores is UTC; this is the one place that converts.
/// </summary>
public static class Kalender
{
    /// <summary>Europe/Amsterdam, resolved once. IANA ids work on Windows too since .NET 6 (ICU).</summary>
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>The local calendar date of an instant.</summary>
    /// <param name="utc">The instant (UTC).</param>
    /// <returns>The date on a wall calendar in Amsterdam.</returns>
    public static DateOnly Datum(DateTime utc) => DateOnly.FromDateTime(Lokaal(utc));

    /// <summary>The instant a local date starts.</summary>
    /// <param name="datum">The local date.</param>
    /// <returns>Local midnight of that date, as UTC.</returns>
    public static DateTime Middernacht(DateOnly datum)
        => TimeZoneInfo.ConvertTimeToUtc(datum.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);

    /// <summary>The wall-clock time in Amsterdam of an instant.</summary>
    /// <param name="utc">The instant (UTC).</param>
    /// <returns>The local date and time.</returns>
    public static DateTime Lokaal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(Utc(utc), Zone);

    /// <summary>The local Monday of the week (Monday to Sunday) containing <paramref name="utc"/>.</summary>
    /// <param name="utc">An instant in the week.</param>
    /// <returns>That Monday's date; it identifies the week.</returns>
    public static DateOnly Maandag(DateTime utc)
    {
        var datum = Datum(utc);
        var dagenSindsMaandag = ((int)datum.DayOfWeek + 6) % 7;
        return datum.AddDays(-dagenSindsMaandag);
    }

    /// <summary>The instant the local week (Monday to Sunday) containing <paramref name="utc"/> starts.</summary>
    /// <param name="utc">An instant in the week.</param>
    /// <returns>Local Monday 00:00 of that week, as UTC.</returns>
    public static DateTime BeginVanDeWeek(DateTime utc) => Middernacht(Maandag(utc));

    /// <summary>Middle of a local day, as UTC: a safe stand-in when only a date is known.</summary>
    /// <param name="datum">The local date.</param>
    /// <returns>Local noon of that date, as UTC.</returns>
    public static DateTime Middag(DateOnly datum)
        => TimeZoneInfo.ConvertTimeToUtc(datum.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified), Zone);

    private static DateTime Utc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
