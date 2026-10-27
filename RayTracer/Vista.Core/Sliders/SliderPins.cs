using System.Globalization;
using Vista.Astronomy;
using Vista.Recipes;

namespace Vista.Sliders;

/// <summary>
/// The linked sliders (vista-plan §3.4, §3.6, §10 M1.11). Each is <b>auto</b> — derived from the controls upstream of it — or
/// <b>pinned</b> to a value the user chose; pinning one stops only that one, and resetting it re-links it. Basic: season, time of
/// day, snow, fog/haze. Advanced: location, altitude, date, the solar time, temperature, humidity, the valley haze and the mist.
/// </summary>
public enum Slider
{
    /// <summary>Not a slider.</summary>
    None,

    /// <summary>The place (advanced): a dropdown place or a custom latitude. Auto: the set pieces' place, else <see cref="Places.Default"/>.</summary>
    Location,

    /// <summary>The scene's base altitude (advanced), m above sea level. Auto: the place's valley floor.</summary>
    Altitude,

    /// <summary>The season (basic). Auto: the pinned date's, else the set pieces', else summer.</summary>
    Season,

    /// <summary>The date (advanced). Auto: the season's date for the place's hemisphere.</summary>
    Date,

    /// <summary>The time-of-day preset (basic). Auto: the set pieces', else day.</summary>
    Time,

    /// <summary>The local solar time (advanced), pinned as a time or as a sun height. Auto: the preset's time.</summary>
    SolarTime,

    /// <summary>The air temperature at the scene's valley floor (advanced), °C. Auto: the climate normals on a daily curve.</summary>
    Temperature,

    /// <summary>The relative humidity at the valley floor (advanced), %. Auto: from the temperature and the day's dew point.</summary>
    Humidity,

    /// <summary>Snow (basic): the seasonal snowline, m above sea level. Auto: the season's snow cover, nudged by the freezing level.</summary>
    Snow,

    /// <summary>Fog / haze (basic): the visibility at the valley floor, km. Auto: from the humidity.</summary>
    Haze,

    /// <summary>
    /// Valley haze (advanced): the visibility under the lid of the haze an inversion holds in the valleys, km. Auto: from the night's
    /// cold pool and what the sun has left of it (vista-plan §10 M1.13a). Since M1.13b each valley's pool lies on its own floor, and
    /// the value is the visibility at the floor of the densest.
    /// </summary>
    ValleyHaze,

    /// <summary>
    /// Mist (advanced): the visibility at the floor of the scene's valley's mist, km, or none. Auto: where the pools' air cools to its
    /// dew point, from the place's humidity normals (vista-plan §10 M1.13b).
    /// </summary>
    Mist,
}

/// <summary>How a temperature is shown (vista-plan §3.3: °C, with °F on request). Values are always stored in °C.</summary>
public enum TemperatureUnit
{
    /// <summary>Not set: shown in °C.</summary>
    None,

    /// <summary>Degrees Celsius.</summary>
    Celsius,

    /// <summary>Degrees Fahrenheit.</summary>
    Fahrenheit,
}

/// <summary>
/// A slider file (<c>*.sliders.json</c>, vista-plan §10 M1.11): the sliders the user pinned and their values. A slider the file
/// does not name is auto. The file holds no set pieces, so changing it never starts a new scene: it changes the place, the time
/// and the weather the world is seen in (§3.4). The preview resolves it with the recipe's set pieces through
/// <see cref="LinkedSliders.Resolve"/>, and the sidecar records every slider's value and whether it was auto or pinned.
///
/// <para><see cref="Location"/> and <see cref="Latitude"/> pin one slider (the place), as <see cref="SolarTime"/> and
/// <see cref="SunElevation"/> do (the time of day, by the clock or by the sun's height): give at most one of each pair.
/// Temperatures are °C; <see cref="TemperatureUnit"/> only changes how they are shown.</para>
/// </summary>
public sealed record SliderPins
{
    /// <summary>The slider file format version this build writes and reads.</summary>
    public const int CurrentSchema = 1;

    /// <summary>The highest base <see cref="Altitude"/> accepted, m.</summary>
    public const double MaxAltitudeMetres = 8000.0;

    /// <summary>The coldest and warmest <see cref="Temperature"/> accepted, °C.</summary>
    public const double MinTemperatureCelsius = -60.0, MaxTemperatureCelsius = 50.0;

    /// <summary>The highest <see cref="Snowline"/> accepted, m: above every peak a scene can hold (8000 m of base plus the relief), so no seasonal snow.</summary>
    public const double MaxSnowlineMetres = 12_000.0;

    /// <summary>The shortest and longest <see cref="Visibility"/> accepted, km. Fog proper (under 1 km) is M4's.</summary>
    public const double MinVisibilityKm = 1.0, MaxVisibilityKm = 500.0;

    /// <summary>
    /// The shortest and longest <see cref="Mist"/> accepted, km: a few hundred metres to see is mist (fog proper, under 200 m, is M4's),
    /// and past 5 km the air is haze. A pinned <see cref="Mist"/> of 0 means none.
    /// </summary>
    public const double MinMistKm = 0.2, MaxMistKm = 5.0;

    /// <summary>The JSON field names that pin a slider, by slider (a sheet axis over one of them varies that slider).</summary>
    public static IReadOnlyDictionary<string, Slider> Fields { get; } = new Dictionary<string, Slider>(StringComparer.Ordinal)
    {
        ["location"] = Slider.Location,
        ["latitude"] = Slider.Location,
        ["altitude"] = Slider.Altitude,
        ["season"] = Slider.Season,
        ["date"] = Slider.Date,
        ["time"] = Slider.Time,
        ["solarTime"] = Slider.SolarTime,
        ["sunElevation"] = Slider.SolarTime,
        ["temperature"] = Slider.Temperature,
        ["humidity"] = Slider.Humidity,
        ["snowline"] = Slider.Snow,
        ["visibility"] = Slider.Haze,
        ["valleyHaze"] = Slider.ValleyHaze,
        ["mist"] = Slider.Mist,
    };

    /// <summary>File format version.</summary>
    public int Schema { get; init; } = CurrentSchema;

    /// <summary>The place from the location dropdown; <see cref="Place.None"/> for none.</summary>
    public Place Location { get; init; } = Place.None;

    /// <summary>A custom latitude in degrees (north positive), instead of a <see cref="Location"/>; null for none.</summary>
    public double? Latitude { get; init; }

    /// <summary>
    /// The scene's base altitude above sea level in metres — world <c>y = 0</c>, the valley floor, where the ground sphere of the
    /// air sits (§3.6: thinner air gives a deeper blue, less haze and a brighter sun); null for the place's valley floor. The
    /// terrain keeps its shape: the whole scene moves up or down in the air, and the climate's temperatures follow the lapse rate.
    /// </summary>
    public double? Altitude { get; init; }

    /// <summary>The season; null for auto.</summary>
    public Season? Season { get; init; }

    /// <summary>The date (<c>yyyy-MM-dd</c>); null for the season's date.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>The time-of-day preset; null for auto.</summary>
    public TimeOfDay? Time { get; init; }

    /// <summary>A local solar time (12:00 is solar noon) instead of the preset's; null for none.</summary>
    public TimeOnly? SolarTime { get; init; }

    /// <summary>
    /// A geometric sun elevation in degrees instead of the preset's height: the time the sun stands this high, rising when the
    /// time of day is dawn and setting otherwise; null for none. Not together with <see cref="SolarTime"/>.
    /// </summary>
    public double? SunElevation { get; init; }

    /// <summary>The air temperature at the valley floor (world <c>y = 0</c>), °C; null for auto.</summary>
    public double? Temperature { get; init; }

    /// <summary>How temperatures are shown (the sidecar, sheet labels); values stay °C.</summary>
    public TemperatureUnit TemperatureUnit { get; init; } = TemperatureUnit.Celsius;

    /// <summary>The relative humidity at the valley floor, % (0 to 100); null for auto.</summary>
    public double? Humidity { get; init; }

    /// <summary>The seasonal snowline, m above sea level: seasonal snow lies above it (the recipe's glaciers whatever it is); null for auto.</summary>
    public double? Snowline { get; init; }

    /// <summary>The visibility at the valley floor, km (fog/haze); null for auto.</summary>
    public double? Visibility { get; init; }

    /// <summary>
    /// The visibility under the valley haze's lid, km; null for auto. The layer adds what the valley's own haze lacks to reach it, so a
    /// visibility no shorter than the valley's (<see cref="MaxVisibilityKm"/>, say) means no valley haze.
    /// </summary>
    public double? ValleyHaze { get; init; }

    /// <summary>
    /// The visibility at the floor of the scene's valley's mist, km (vista-plan §10 M1.13b), 0 for no mist; null for auto. A pinned mist
    /// fills the lower part of every valley's pool and keeps the night's whole pool at any hour, as a pinned valley haze does.
    /// </summary>
    public double? Mist { get; init; }

    /// <summary>No pins: every slider auto.</summary>
    public static SliderPins None { get; } = new();

    /// <summary>
    /// The pins of a recipe's own place and time fields: each field it gives pins its slider (one that gives none leaves every
    /// slider auto). A recipe's date has replaced its season since M1.2, so with a date the season is left auto: the date's.
    /// </summary>
    public static SliderPins FromRecipe(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return new SliderPins
        {
            Location = recipe.Location,
            Latitude = recipe.Latitude,
            Altitude = recipe.Altitude,
            Season = recipe.Date is null ? recipe.Season : null,
            Date = recipe.Date,
            Time = recipe.Time,
            SolarTime = recipe.SolarTime,
            SunElevation = recipe.SunElevation,
        };
    }

    /// <summary>True when <paramref name="slider"/> is pinned.</summary>
    public bool IsPinned(Slider slider) => slider switch
    {
        Slider.Location => Location != Place.None || Latitude is not null,
        Slider.Altitude => Altitude is not null,
        Slider.Season => Season is not null,
        Slider.Date => Date is not null,
        Slider.Time => Time is not null,
        Slider.SolarTime => SolarTime is not null || SunElevation is not null,
        Slider.Temperature => Temperature is not null,
        Slider.Humidity => Humidity is not null,
        Slider.Snow => Snowline is not null,
        Slider.Haze => Visibility is not null,
        Slider.ValleyHaze => ValleyHaze is not null,
        Slider.Mist => Mist is not null,
        _ => throw new ArgumentOutOfRangeException(nameof(slider), slider, "Unknown slider."),
    };

    /// <summary>The sliders pinned, in <see cref="Slider"/> order.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<Slider> Pinned => [.. Enum.GetValues<Slider>().Where(s => s != Slider.None && IsPinned(s))];

    /// <summary>These pins with <paramref name="slider"/> reset: it is auto again, re-linked to the controls upstream of it.</summary>
    public SliderPins Reset(Slider slider) => slider switch
    {
        Slider.Location => this with { Location = Place.None, Latitude = null },
        Slider.Altitude => this with { Altitude = null },
        Slider.Season => this with { Season = null },
        Slider.Date => this with { Date = null },
        Slider.Time => this with { Time = null },
        Slider.SolarTime => this with { SolarTime = null, SunElevation = null },
        Slider.Temperature => this with { Temperature = null },
        Slider.Humidity => this with { Humidity = null },
        Slider.Snow => this with { Snowline = null },
        Slider.Haze => this with { Visibility = null },
        Slider.ValleyHaze => this with { ValleyHaze = null },
        Slider.Mist => this with { Mist = null },
        _ => throw new ArgumentOutOfRangeException(nameof(slider), slider, "Unknown slider."),
    };

    /// <summary>Problems that make these pins unusable (empty when they are valid). Implausible values are not problems.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Schema != CurrentSchema)
            errors.Add($"Unsupported slider file schema {Schema}; this build reads schema {CurrentSchema}.");
        if (!Enum.IsDefined(Location))
            errors.Add($"Unknown location {(int)Location}.");
        Range(Latitude, -90.0, 90.0, "Latitude", "degrees", errors);
        if (Latitude is not null && Location != Place.None)
            errors.Add("Give a location or a custom latitude, not both.");
        Range(SunElevation, -90.0, 90.0, "The sun elevation", "degrees", errors);
        if (SunElevation is not null && SolarTime is not null)
            errors.Add("Give a solar time or a sun elevation, not both.");
        Range(Altitude, 0.0, MaxAltitudeMetres, "The altitude", "m above sea level", errors);
        if (Season is Season season && !Enum.IsDefined(season))
            errors.Add($"Unknown season {(int)season}.");
        if (Time is TimeOfDay time && !Enum.IsDefined(time))
            errors.Add($"Unknown time of day {(int)time}.");
        Range(Temperature, MinTemperatureCelsius, MaxTemperatureCelsius, "The temperature", "°C", errors);
        if (TemperatureUnit == TemperatureUnit.None || !Enum.IsDefined(TemperatureUnit))
            errors.Add("The temperature unit must be celsius or fahrenheit.");
        Range(Humidity, 0.0, 100.0, "The humidity", "%", errors);
        Range(Snowline, 0.0, MaxSnowlineMetres, "The snowline", "m above sea level", errors);
        Range(Visibility, MinVisibilityKm, MaxVisibilityKm, "The visibility", "km", errors);
        Range(ValleyHaze, MinVisibilityKm, MaxVisibilityKm, "The visibility under the valley haze", "km", errors);
        if (Mist is not 0.0)
            Range(Mist, MinMistKm, MaxMistKm, "The visibility in the mist", "km (or 0 for none)", errors);
        return errors;
    }

    private static void Range(double? value, double min, double max, string what, string unit, List<string> errors)
    {
        if (value is double v && !(v >= min && v <= max))
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{what} must be in [{min}, {max}] {unit}, got {v}."));
    }
}
