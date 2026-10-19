using Vista.Recipes;

namespace Vista.Astronomy;

/// <summary>
/// Season ↔ date by hemisphere (vista-plan §3.5): "autumn" at Torres del Paine is April, so the sun path
/// always matches the season. Seasons are the meteorological ones (whole months: spring is March to May in the
/// north), and a season's date is the 15th of its middle month in <see cref="ReferenceYear"/>. The equator
/// counts as north.
/// </summary>
public static class SeasonCalendar
{
    /// <summary>
    /// The year of every date derived from a season. Fixed, so a recipe always gives the same sun: the
    /// declination for a calendar date drifts by up to about 0.1° over the four-year leap cycle.
    /// </summary>
    public const int ReferenceYear = 2026;

    /// <summary>The representative date of <paramref name="season"/> at <paramref name="latitudeDegrees"/>.</summary>
    public static DateOnly DateFor(Season season, double latitudeDegrees)
    {
        int month = season switch
        {
            Season.Spring => 4,
            Season.Summer => 7,
            Season.Autumn => 10,
            Season.Winter => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(season), season, "Unknown season."),
        };
        if (latitudeDegrees < 0.0)
            month = (month + 5) % 12 + 1;
        return new DateOnly(ReferenceYear, month, 15);
    }

    /// <summary>The season <paramref name="date"/> falls in at <paramref name="latitudeDegrees"/>.</summary>
    public static Season SeasonOf(DateOnly date, double latitudeDegrees)
    {
        int month = latitudeDegrees < 0.0 ? (date.Month + 5) % 12 + 1 : date.Month;
        return month switch
        {
            3 or 4 or 5 => Season.Spring,
            6 or 7 or 8 => Season.Summer,
            9 or 10 or 11 => Season.Autumn,
            _ => Season.Winter,
        };
    }
}
