using System.Globalization;
using Vista.Recipes;

namespace Vista.Astronomy;

/// <summary>
/// The place, date and time a recipe asks for, resolved to a sun (vista-plan §3.5, §10 M1.2): the recipe's
/// location or custom latitude, its date or its season's date for that hemisphere, and its local solar time
/// or its time-of-day preset relative to that day's sunrise and sunset.
///
/// <para>A recipe's <c>sunElevation</c> replaces the preset with the time the sun stands that high, rising for dawn
/// and setting otherwise (<see cref="SolarPosition.HoursAtElevation"/>); an elevation the sun never reaches that
/// day takes noon or midnight, with a hint.</para>
///
/// <para><b>Presets</b> (<see cref="SolarHoursFor"/>): dawn is sunrise − 20 min and dusk sunset + 20 min,
/// so both are in twilight with the sun a few degrees below the horizon; golden hour is sunset − 60 min (the
/// last hour before sunset, never before noon); day is solar noon and night solar midnight. When the sun
/// neither rises nor sets the anchors are missing, so in polar day the low-sun presets take the lowest sun
/// (midnight) and in polar night the daytime presets take the highest (noon), with a hint saying so.</para>
/// </summary>
/// <param name="Place">The place.</param>
/// <param name="Date">The date.</param>
/// <param name="DateFromSeason">True when <see cref="Date"/> came from the recipe's season rather than its date.</param>
/// <param name="Season">The season: the recipe's, or the date's when the recipe gives a date (a date overrides the season).</param>
/// <param name="Daylight">Sunrise and sunset on the date.</param>
/// <param name="SolarHours">The local solar time rendered, in hours.</param>
/// <param name="SolarTimeFrom">
/// How <see cref="SolarHours"/> was chosen, e.g. <c>"goldenHour: sunset − 60 min"</c> or
/// <c>"recipe solarTime"</c>.
/// </param>
/// <param name="Sun">Where the sun is.</param>
/// <param name="Hints">Notes for the user, e.g. that polar day has no dusk.</param>
public sealed record Almanac(
    PlaceInfo Place,
    DateOnly Date,
    bool DateFromSeason,
    Season Season,
    Daylight Daylight,
    double SolarHours,
    string SolarTimeFrom,
    SunPosition Sun,
    IReadOnlyList<string> Hints)
{
    /// <summary>Dawn's offset before sunrise.</summary>
    public static readonly TimeSpan DawnBeforeSunrise = TimeSpan.FromMinutes(20);

    /// <summary>Dusk's offset after sunset.</summary>
    public static readonly TimeSpan DuskAfterSunset = TimeSpan.FromMinutes(20);

    /// <summary>Golden hour's offset before sunset.</summary>
    public static readonly TimeSpan GoldenHourBeforeSunset = TimeSpan.FromMinutes(60);

    /// <summary>Resolves <paramref name="recipe"/> (which must be valid).</summary>
    public static Almanac For(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        PlaceInfo place = recipe.Latitude is double latitude ? Places.Custom(latitude) : Places.Get(recipe.Location);
        DateOnly date = recipe.Date ?? SeasonCalendar.DateFor(recipe.Season, place.LatitudeDegrees);
        Season season = recipe.Date is DateOnly given ? SeasonCalendar.SeasonOf(given, place.LatitudeDegrees) : recipe.Season;
        Daylight daylight = SolarPosition.DaylightOn(place.LatitudeDegrees, date);

        var hints = new List<string>();
        double hours;
        string from;
        if (recipe.SolarTime is TimeOnly time)
        {
            hours = time.ToTimeSpan().TotalHours;
            from = "recipe solarTime";
        }
        else if (recipe.SunElevation is double elevation)
        {
            bool morning = recipe.Time == TimeOfDay.Dawn;
            (hours, bool reachable) = SolarPosition.HoursAtElevation(place.LatitudeDegrees, date, elevation, morning);
            string side = morning ? "rising" : "setting";
            from = string.Create(CultureInfo.InvariantCulture, $"sunElevation {elevation:0.##}°, {side}");
            if (!reachable)
                hints.Add(string.Create(CultureInfo.InvariantCulture,
                    $"The sun never stands at {elevation:0.##}° at {place.Name} on {date:yyyy-MM-dd}, so the {side} sun uses {(hours is > 6.0 and < 18.0 ? "solar noon, its highest" : "solar midnight, its lowest")}."));
        }
        else
        {
            (hours, from) = SolarHoursFor(recipe.Time, daylight);
            if (!daylight.HasSunriseAndSunset && recipe.Time is TimeOfDay.Dawn or TimeOfDay.GoldenHour or TimeOfDay.Dusk)
                hints.Add(PolarHint(recipe.Time, daylight.Kind, place, date, hours));
        }

        SunPosition sun = SolarPosition.At(place.LatitudeDegrees, date, hours);
        return new Almanac(place, date, recipe.Date is null, season, daylight, hours, from, sun, hints);
    }

    /// <summary>
    /// The local solar time of <paramref name="time"/> on a day with <paramref name="daylight"/>, in hours in
    /// [0, 24], and how it was chosen.
    /// </summary>
    public static (double Hours, string From) SolarHoursFor(TimeOfDay time, Daylight daylight)
    {
        string name = JsonName(time);
        if (time == TimeOfDay.Day)
            return (12.0, $"{name}: solar noon");
        if (time == TimeOfDay.Night)
            return (0.0, $"{name}: solar midnight");
        return daylight.Kind switch
        {
            DaylightKind.RisesAndSets => time switch
            {
                TimeOfDay.Dawn => (Math.Max(0.0, daylight.SunriseHours - DawnBeforeSunrise.TotalHours), $"{name}: sunrise − {Minutes(DawnBeforeSunrise)} min"),
                TimeOfDay.GoldenHour => (Math.Max(12.0, daylight.SunsetHours - GoldenHourBeforeSunset.TotalHours), $"{name}: sunset − {Minutes(GoldenHourBeforeSunset)} min"),
                TimeOfDay.Dusk => (Math.Min(24.0, daylight.SunsetHours + DuskAfterSunset.TotalHours), $"{name}: sunset + {Minutes(DuskAfterSunset)} min"),
                _ => throw new ArgumentOutOfRangeException(nameof(time), time, "Unknown time of day."),
            },
            DaylightKind.PolarDay => (0.0, $"{name}: polar day, lowest sun (solar midnight)"),
            DaylightKind.PolarNight => (12.0, $"{name}: polar night, highest sun (solar noon)"),
            _ => throw new ArgumentOutOfRangeException(nameof(daylight), daylight.Kind, "Daylight was not computed."),
        };
    }

    /// <summary>Formats solar hours as <c>HH:mm:ss</c> (24:00:00 for the end of the day).</summary>
    public static string FormatHours(double hours)
    {
        long seconds = (long)Math.Round(hours * 3600.0, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    }

    private static string PolarHint(TimeOfDay time, DaylightKind kind, PlaceInfo place, DateOnly date, double hours)
    {
        string where = $"{place.Name} on {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        return kind == DaylightKind.PolarDay
            ? $"Polar day at {where}: the sun never sets, so {JsonName(time)} uses the lowest sun, at {FormatHours(hours)} local solar time."
            : $"Polar night at {where}: the sun never rises, so {JsonName(time)} uses the highest sun, at {FormatHours(hours)} local solar time.";
    }

    private static int Minutes(TimeSpan span) => (int)span.TotalMinutes;

    private static string JsonName(TimeOfDay time) => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(time.ToString());
}
