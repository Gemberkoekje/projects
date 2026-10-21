using System.Globalization;
using Vista.Recipes;
using Vista.Sliders;

namespace Vista.Astronomy;

/// <summary>
/// The place, date and time the sliders settle on, resolved to a sun (vista-plan §3.5, §10 M1.2, M1.11): a location or custom
/// latitude, a date (the season's date for that hemisphere unless the date is pinned), and a local solar time — pinned, or the
/// time-of-day preset's.
///
/// <para><b>Presets</b> (<see cref="SolarHoursFor"/>, since M1.11) are sun heights, not offsets from sunrise and sunset: dawn is the
/// rising sun at <see cref="DawnElevationDegrees"/>, dusk the setting sun at <see cref="DuskElevationDegrees"/>, golden hour the
/// setting sun at <see cref="GoldenHourElevationDegrees"/>; day is solar noon and night solar midnight. Heights are geometric, as
/// twilight is defined (civil twilight ends at −6°). M1.2's "sunset − 60 min" put golden hour anywhere from 4.3° to 10.7° up,
/// depending on place and season; a height puts the light where it is warm everywhere. Where the sun never reaches a preset's
/// height that day — never sinks to −4° in a white night, never climbs to +2° near the polar night — the preset takes the
/// nearest the sun comes: its lowest (solar midnight) or its highest (solar noon), with a hint saying so (M1.2's polar
/// fallbacks, now for every height).</para>
///
/// <para>A pinned <c>sunElevation</c> replaces the preset's height with its own, rising for dawn and setting otherwise
/// (<see cref="SolarPosition.HoursAtElevation"/>); a pinned <c>solarTime</c> replaces the preset altogether.</para>
/// </summary>
/// <param name="Place">The place.</param>
/// <param name="Date">The date.</param>
/// <param name="DateFromSeason">True when <see cref="Date"/> came from the season rather than a pinned date.</param>
/// <param name="Season">The season: pinned, or the date's when the date is pinned (a date decides its season).</param>
/// <param name="Daylight">Sunrise and sunset on the date.</param>
/// <param name="SolarHours">The local solar time rendered, in hours.</param>
/// <param name="SolarTimeFrom">
/// How <see cref="SolarHours"/> was chosen, e.g. <c>"goldenHour: sun at +2°, setting"</c> or <c>"pinned solarTime"</c>.
/// </param>
/// <param name="Sun">Where the sun is.</param>
/// <param name="Hints">Notes for the user, e.g. that a white night has no dawn.</param>
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
    /// <summary>Dawn's preset: the rising sun this far below the horizon (geometric), in civil twilight.</summary>
    public const double DawnElevationDegrees = -4.0;

    /// <summary>
    /// Golden hour's preset: the setting sun this high (geometric), picked by M1.11's +2° / +4° / +6° sheet
    /// (<c>m1-11-golden-hour-heights.png</c>): only at +2° do the sunlit faces read golden rather than beige or cream.
    /// </summary>
    public const double GoldenHourElevationDegrees = 2.0;

    /// <summary>Dusk's preset: the setting sun this far below the horizon (geometric), in civil twilight.</summary>
    public const double DuskElevationDegrees = -4.0;

    /// <summary>
    /// Resolves the sliders of <paramref name="recipe"/> as its own fields pin them, every other slider auto. The preview reads a
    /// slider file and options too (<see cref="LinkedSliders"/>).
    /// </summary>
    public static Almanac For(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return LinkedSliders.Resolve(recipe, SliderPins.FromRecipe(recipe)).Almanac;
    }

    /// <summary>
    /// The almanac of a place and a date at a time of day: <paramref name="solarTime"/> when pinned, else the time the sun stands at
    /// <paramref name="sunElevation"/> when pinned, else <paramref name="time"/>'s preset.
    /// </summary>
    public static Almanac Resolve(PlaceInfo place, DateOnly date, bool dateFromSeason, Season season, TimeOfDay time, TimeOnly? solarTime,
        double? sunElevation)
    {
        ArgumentNullException.ThrowIfNull(place);
        Daylight daylight = SolarPosition.DaylightOn(place.LatitudeDegrees, date);
        var hints = new List<string>();
        double hours;
        string from;
        if (solarTime is TimeOnly pinned)
        {
            hours = pinned.ToTimeSpan().TotalHours;
            from = "pinned solarTime";
        }
        else
        {
            bool reachable;
            (hours, from, reachable) = sunElevation is double elevation
                ? AtElevation(place, date, elevation, time == TimeOfDay.Dawn, string.Create(CultureInfo.InvariantCulture, $"pinned sunElevation {Signed(elevation)}°"))
                : SolarHoursFor(time, place.LatitudeDegrees, date);
            if (!reachable)
                hints.Add(UnreachableHint(sunElevation is null ? JsonName(time) : "the pinned sun elevation", sunElevation ?? PresetElevation(time),
                    place, date, hours, daylight.Kind));
        }

        SunPosition sun = SolarPosition.At(place.LatitudeDegrees, date, hours);
        return new Almanac(place, date, dateFromSeason, season, daylight, hours, from, sun, hints);
    }

    /// <summary>
    /// The local solar time of <paramref name="time"/>'s preset at <paramref name="latitudeDegrees"/> on <paramref name="date"/>, in
    /// hours in [0, 24], how it was chosen, and whether the sun reaches the preset's height that day (when it does not, the time of
    /// its nearest approach: solar midnight or solar noon).
    /// </summary>
    public static (double Hours, string From, bool Reachable) SolarHoursFor(TimeOfDay time, double latitudeDegrees, DateOnly date)
    {
        string name = JsonName(time);
        switch (time)
        {
            case TimeOfDay.Day:
                return (12.0, $"{name}: solar noon", true);
            case TimeOfDay.Night:
                return (0.0, $"{name}: solar midnight", true);
            case TimeOfDay.Dawn or TimeOfDay.GoldenHour or TimeOfDay.Dusk:
                double elevation = PresetElevation(time);
                bool morning = time == TimeOfDay.Dawn;
                (double hours, bool reachable) = SolarPosition.HoursAtElevation(latitudeDegrees, date, elevation, morning);
                string from = string.Create(CultureInfo.InvariantCulture, $"{name}: sun at {Signed(elevation)}°, {(morning ? "rising" : "setting")}");
                if (!reachable)
                    from += hours is > 6.0 and < 18.0 ? "; never that high, so its highest" : "; never that low, so its lowest";
                return (hours, from, reachable);
            default:
                throw new ArgumentOutOfRangeException(nameof(time), time, "Unknown time of day.");
        }
    }

    /// <summary>The geometric sun height of a preset that has one (dawn, golden hour, dusk).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="time"/> is day or night, which are solar noon and midnight.</exception>
    public static double PresetElevation(TimeOfDay time) => time switch
    {
        TimeOfDay.Dawn => DawnElevationDegrees,
        TimeOfDay.GoldenHour => GoldenHourElevationDegrees,
        TimeOfDay.Dusk => DuskElevationDegrees,
        _ => throw new ArgumentOutOfRangeException(nameof(time), time, "Only dawn, golden hour and dusk are sun heights."),
    };

    /// <summary>Formats solar hours as <c>HH:mm:ss</c> (24:00:00 for the end of the day).</summary>
    public static string FormatHours(double hours)
    {
        long seconds = (long)Math.Round(hours * 3600.0, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    }

    private static (double Hours, string From, bool Reachable) AtElevation(PlaceInfo place, DateOnly date, double elevation, bool morning, string what)
    {
        (double hours, bool reachable) = SolarPosition.HoursAtElevation(place.LatitudeDegrees, date, elevation, morning);
        return (hours, $"{what}, {(morning ? "rising" : "setting")}", reachable);
    }

    private static string UnreachableHint(string what, double elevation, PlaceInfo place, DateOnly date, double hours, DaylightKind kind)
    {
        string where = $"{place.Name} on {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        bool highest = hours is > 6.0 and < 18.0;
        string polar = kind switch
        {
            DaylightKind.PolarDay => " (polar day)",
            DaylightKind.PolarNight => " (polar night)",
            _ => string.Empty,
        };
        return string.Create(CultureInfo.InvariantCulture,
            $"The sun never {(highest ? "climbs" : "sinks")} to {Signed(elevation)}° at {where}{polar}, so {what} uses its {(highest ? "highest" : "lowest")}, at {FormatHours(hours)} local solar time.");
    }

    /// <summary>Degrees with their sign, e.g. <c>+4</c>, <c>−4</c>, <c>0</c>.</summary>
    internal static string Signed(double degrees) => degrees.ToString("+0.##;−0.##;0", CultureInfo.InvariantCulture);

    private static string JsonName(TimeOfDay time) => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(time.ToString());
}
