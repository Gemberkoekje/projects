using System.Globalization;

namespace Klaverjas.core.Models;

/// <summary>
/// The club runs on Amsterdam time and Dutch week numbers. This is the one place that turns an
/// instant into a date, and a date into a week or a quarter.
/// </summary>
public static class Kalender
{
    /// <summary>Europe/Amsterdam, resolved once. IANA ids work on Windows too since .NET 6 (ICU).</summary>
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>Today on a wall calendar in Amsterdam.</summary>
    /// <returns>The local date.</returns>
    public static DateOnly Vandaag() => Datum(Clock.UtcNow());

    /// <summary>The local calendar date of an instant.</summary>
    /// <param name="utc">The instant (UTC).</param>
    /// <returns>The date in Amsterdam.</returns>
    public static DateOnly Datum(DateTime utc)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone));

    /// <summary>The ISO week number, as on Dad's sheet ("week 47").</summary>
    /// <param name="datum">The date.</param>
    /// <returns>1 to 53.</returns>
    public static int Week(DateOnly datum) => ISOWeek.GetWeekOfYear(datum.ToDateTime(TimeOnly.MinValue));

    /// <summary>The quarter of the year, for "Laagste waarde vervalt" (R16).</summary>
    /// <param name="datum">The date.</param>
    /// <returns>1 to 4.</returns>
    public static int Kwartaal(DateOnly datum) => ((datum.Month - 1) / 3) + 1;

    /// <summary>Every date in a month that falls on the club's weekday: the month columns (R8).</summary>
    /// <param name="jaar">The year.</param>
    /// <param name="maand">The month (1–12).</param>
    /// <param name="speeldag">The club's weekday.</param>
    /// <returns>The dates, in order.</returns>
    public static IReadOnlyList<DateOnly> Speeldagen(int jaar, int maand, DayOfWeek speeldag)
    {
        var eerste = new DateOnly(jaar, maand, 01);
        var dag = eerste.AddDays(((int)speeldag - (int)eerste.DayOfWeek + 7) % 7);
        var dagen = new List<DateOnly>();

        for (; dag.Month == maand; dag = dag.AddDays(7))
        {
            dagen.Add(dag);
        }

        return dagen;
    }

    /// <summary>
    /// The next evening to enter: the first club day after the latest entered evening (or from the
    /// season's first evening), skipping evenings without play.
    /// </summary>
    /// <param name="instellingen">The play day, the first evening and the evenings without play.</param>
    /// <param name="ingevuld">The dates already entered.</param>
    /// <returns>The date.</returns>
    public static DateOnly VolgendeAvond(Instellingen instellingen, IEnumerable<DateOnly> ingevuld)
    {
        ArgumentNullException.ThrowIfNull(instellingen);
        ArgumentNullException.ThrowIfNull(ingevuld);

        var laatste = ingevuld.DefaultIfEmpty(DateOnly.MinValue).Max();
        var vanaf = laatste == DateOnly.MinValue || laatste < instellingen.EersteAvond
            ? instellingen.EersteAvond
            : laatste.AddDays(1);
        var dag = vanaf.AddDays(((int)instellingen.Speeldag - (int)vanaf.DayOfWeek + 7) % 7);

        while (instellingen.IsGeenSpeelavond(dag))
        {
            dag = dag.AddDays(7);
        }

        return dag;
    }
}
