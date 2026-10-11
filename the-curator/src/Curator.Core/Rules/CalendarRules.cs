using Curator.Core.Content;

namespace Curator.Core.Rules;

/// <summary>Prints game days as in-world dates.</summary>
public static class CalendarRules
{
    /// <summary>The date of a game day, e.g. "3 Seedmonth". Days before day 1 count backwards.</summary>
    /// <param name="calendar">The calendar.</param>
    /// <param name="day">The game day (0 or less for history).</param>
    /// <returns>The date.</returns>
    public static string DateText(Calendar calendar, int day)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var months = calendar.Months.Count;
        var firstMonth = calendar.Months.ToList().IndexOf(calendar.Day1.Month);
        var offset = calendar.Day1.Day - 1 + (day - 1);
        var monthShift = (int)Math.Floor(offset / (double)calendar.DaysPerMonth);
        var dayOfMonth = offset - (monthShift * calendar.DaysPerMonth) + 1;
        var month = calendar.Months[(((firstMonth + monthShift) % months) + months) % months];
        return $"{dayOfMonth} {month}";
    }
}
