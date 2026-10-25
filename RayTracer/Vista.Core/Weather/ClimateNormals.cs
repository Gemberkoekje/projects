using System.Globalization;
using Vista.Astronomy;

namespace Vista.Weather;

/// <summary>
/// A place's climate normals (vista-plan §3.6, §10 M1.11): the monthly means of the daily low and high at one station, entered by
/// hand from a published normals table and treated as constants, like the CIE tables (tier 5 until M2's manifest, §6.2). Since
/// M1.13 a dropdown place's normals also hold its monthly precipitation (<see cref="Precipitation"/>), which the season's snowline
/// reads. A custom latitude has no station; its normals come from <see cref="LatitudeClimate"/> instead, without precipitation.
/// </summary>
/// <param name="Station">The station, e.g. "Zermatt (MeteoSwiss)", or the latitude model.</param>
/// <param name="StationAltitudeMetres">The station's altitude above sea level: where these temperatures apply (others follow the lapse rate).</param>
/// <param name="Period">The normals period, e.g. "1991–2020".</param>
/// <param name="Source">Where the numbers come from.</param>
/// <param name="Highs">The mean daily high of each month, January first, °C.</param>
/// <param name="Lows">The mean daily low of each month, January first, °C.</param>
public sealed record ClimateNormals(string Station, double StationAltitudeMetres, string Period, string Source, IReadOnlyList<double> Highs,
    IReadOnlyList<double> Lows)
{
    /// <summary>
    /// The day's normal low and high at the station on <paramref name="date"/>: each month's mean taken at the middle of the month
    /// and interpolated linearly between months, across the new year too. The year's length is the date's own.
    /// </summary>
    public DayNormals On(DateOnly date)
    {
        (int before, int after, double t) = Between(date);
        return new DayNormals(Lerp(Lows[before], Lows[after], t), Lerp(Highs[before], Highs[after], t));
    }

    /// <summary>The months whose middles <paramref name="date"/> lies between, and how far it lies from the first to the second.</summary>
    private static (int Before, int After, double T) Between(DateOnly date)
    {
        int year = date.Year;
        int days = DateTime.IsLeapYear(year) ? 366 : 365;
        // Middles of the months, in days from the start of the year; the day's own middle is its day of the year less a half.
        var middles = new double[12];
        double start = 0.0;
        for (int m = 0; m < 12; m++)
        {
            int length = DateTime.DaysInMonth(year, m + 1);
            middles[m] = start + length / 2.0;
            start += length;
        }
        double day = date.DayOfYear - 0.5;
        int before;
        double from, to;
        if (day < middles[0])
            (before, from, to) = (11, middles[11] - days, middles[0]);
        else if (day >= middles[11])
            (before, from, to) = (11, middles[11], middles[0] + days);
        else
        {
            before = Array.FindLastIndex(middles, m => m <= day);
            (from, to) = (middles[before], middles[before + 1]);
        }
        return (before, (before + 1) % 12, (day - from) / (to - from));
    }

    /// <summary>
    /// The mean precipitation of each month, January first, mm (vista-plan §10 M1.13), from <see cref="PrecipitationSource"/>; null
    /// when the normals have none (a custom latitude), and the snowline assumes one rate for every day (<see cref="SnowCover"/>).
    /// </summary>
    public IReadOnlyList<double>? Precipitation { get; init; }

    /// <summary>
    /// The station and the table the precipitation comes from: the temperatures' own station unless that one publishes none.
    /// </summary>
    public string? PrecipitationSource { get; init; }

    /// <summary>The altitude of the precipitation's station above sea level, m (<see cref="StationAltitudeMetres"/> unless set).</summary>
    public double? PrecipitationAltitudeMetres { get; init; }

    /// <summary>
    /// The day's normal precipitation at the precipitation's station on <paramref name="date"/>, mm: each month's total spread over
    /// its days and taken at the middle of the month, interpolated linearly between months as the temperatures are (so a year's
    /// sum is close to, not exactly, the normals' annual total); null without precipitation.
    /// </summary>
    public double? PrecipitationOn(DateOnly date)
    {
        if (Precipitation is null)
            return null;
        int year = date.Year;
        var daily = new double[12];
        for (int m = 0; m < 12; m++)
            daily[m] = Precipitation[m] / DateTime.DaysInMonth(year, m + 1);
        (int before, int after, double t) = Between(date);
        return Lerp(daily[before], daily[after], t);
    }

    /// <summary>The month's mean of the daily mean (the average of its low and high), °C.</summary>
    public double MonthlyMean(int month) => 0.5 * (Lows[month] + Highs[month]);

    /// <summary>Problems that make these normals unusable (empty when they are valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Highs is not { Count: 12 } || Lows is not { Count: 12 })
        {
            errors.Add($"{Station}: twelve monthly highs and lows are needed.");
            return errors;
        }
        for (int m = 0; m < 12; m++)
        {
            if (!(Highs[m] > Lows[m]) || !double.IsFinite(Highs[m]) || !double.IsFinite(Lows[m]))
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"{Station}: month {m + 1}'s high {Highs[m]} must be above its low {Lows[m]}."));
        }
        if (string.IsNullOrWhiteSpace(Source) || string.IsNullOrWhiteSpace(Period))
            errors.Add($"{Station}: the source and the period must be given.");
        if (Precipitation is not null)
        {
            if (Precipitation.Count != 12 || Precipitation.Any(p => !(p >= 0.0) || !double.IsFinite(p)))
                errors.Add($"{Station}: twelve monthly precipitation totals, none negative, are needed.");
            if (string.IsNullOrWhiteSpace(PrecipitationSource))
                errors.Add($"{Station}: the precipitation's source must be given.");
        }
        return errors;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>A day's normal low and high, °C.</summary>
/// <param name="LowCelsius">The mean daily low.</param>
/// <param name="HighCelsius">The mean daily high.</param>
public readonly record struct DayNormals(double LowCelsius, double HighCelsius)
{
    /// <summary>The daily mean, the average of the low and the high.</summary>
    public double MeanCelsius => 0.5 * (LowCelsius + HighCelsius);

    /// <summary>These normals <paramref name="metres"/> higher (negative: lower), along <see cref="Lapse.RateCelsiusPerMetre"/>.</summary>
    public DayNormals Raised(double metres) =>
        new(LowCelsius - Lapse.RateCelsiusPerMetre * metres, HighCelsius - Lapse.RateCelsiusPerMetre * metres);
}

/// <summary>The environmental lapse rate (vista-plan §3.6): the air cools 6.5 °C per 1000 m of height (the standard atmosphere's troposphere).</summary>
public static class Lapse
{
    /// <summary>°C per metre of height.</summary>
    public const double RateCelsiusPerMetre = 0.0065;

    /// <summary>The altitude above sea level at which air <paramref name="celsius"/> warm at <paramref name="altitudeMetres"/> reaches 0 °C.</summary>
    public static double FreezingLevel(double altitudeMetres, double celsius) => altitudeMetres + celsius / RateCelsiusPerMetre;
}

/// <summary>
/// The climate normals of the location dropdown (vista-plan §3.5, §3.6) and of a custom latitude (<see cref="LatitudeClimate"/>).
/// </summary>
public static class Climate
{
    /// <summary>The normals for <paramref name="place"/>: its station's, or for a custom latitude the latitude model's.</summary>
    public static ClimateNormals For(PlaceInfo place)
    {
        ArgumentNullException.ThrowIfNull(place);
        return place.IsCustom ? LatitudeClimate.For(place.LatitudeDegrees) : ClimateTable.For(place.Place);
    }
}
