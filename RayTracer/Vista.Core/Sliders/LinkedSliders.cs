using System.Globalization;
using System.Text.Json;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Recipes;
using Vista.Weather;

namespace Vista.Sliders;

/// <summary>
/// The linked sliders resolved (vista-plan §3.4, §3.6, §10 M1.11): every slider's value, whether it was pinned, and where an auto
/// value came from, plus what the weather chain worked out on the way.
/// </summary>
/// <param name="Pins">The pins it was resolved from.</param>
/// <param name="Pushes">What the recipe's set pieces pushed.</param>
/// <param name="Almanac">The place, date, time and sun.</param>
/// <param name="BaseAltitudeMetres">The scene's base altitude above sea level (world <c>y = 0</c>, the valley floor).</param>
/// <param name="Climate">The place's climate normals.</param>
/// <param name="StationDay">The date's normal low and high at the climate's station.</param>
/// <param name="Day">The date's normal low and high at the scene's valley floor (the station's, along the lapse rate).</param>
/// <param name="ClimateTemperatureCelsius">The climate's temperature at the valley floor at the rendered hour, before any pin or push.</param>
/// <param name="TemperatureCelsius">The temperature slider: the air at the valley floor, °C.</param>
/// <param name="DewPointCelsius">The dew point at the valley floor: the day's normal low (<see cref="Humidity"/>).</param>
/// <param name="HumidityPercent">The humidity slider: the relative humidity at the valley floor, %.</param>
/// <param name="FreezingLevelMetres">Where the air reaches 0 °C, m above sea level, from the temperature and the lapse rate.</param>
/// <param name="ClimateSnowlineMetres">The season's snowline from the climate alone (<see cref="SnowCover"/>), m above sea level.</param>
/// <param name="SnowlineMetres">The snow slider: the seasonal snowline, m above sea level.</param>
/// <param name="HazeHumidityPercent">The humidity the haze grew by: pinned, or the day's (at its mean temperature), %.</param>
/// <param name="AerosolOpticalDepth550">The haze the render gets: vertical optical depth at 550 nm from sea level.</param>
/// <param name="VisibilityKm">The fog/haze slider: the visibility at the valley floor, km.</param>
/// <param name="Readings">Every slider's value, pin and origin, by slider.</param>
/// <param name="Implausible">Contradictions between pins, or between a pin and the set pieces: rendered as asked, with these hints.</param>
/// <param name="Notes">Other notes for the user (a visibility clean air cannot reach).</param>
public sealed record SliderState(
    SliderPins Pins,
    SliderPushes Pushes,
    Almanac Almanac,
    double BaseAltitudeMetres,
    ClimateNormals Climate,
    DayNormals StationDay,
    DayNormals Day,
    double ClimateTemperatureCelsius,
    double TemperatureCelsius,
    double DewPointCelsius,
    double HumidityPercent,
    double FreezingLevelMetres,
    double ClimateSnowlineMetres,
    double SnowlineMetres,
    double HazeHumidityPercent,
    double AerosolOpticalDepth550,
    double VisibilityKm,
    IReadOnlyDictionary<Slider, SliderReading> Readings,
    IReadOnlyList<string> Implausible,
    IReadOnlyList<string> Notes)
{
    /// <summary>Every hint for the user: the almanac's (a preset height the sun never reaches), the implausible ones, the notes.</summary>
    public IReadOnlyList<string> Hints => [.. Almanac.Hints, .. Implausible, .. Notes];

    /// <summary><paramref name="model"/> with the haze the sliders settled on.</summary>
    public AtmosphereModel Air(AtmosphereModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model with { AerosolOpticalDepth550 = AerosolOpticalDepth550 };
    }
}

/// <summary>One slider's value as resolved.</summary>
/// <param name="Value">The value as shown, e.g. <c>zermatt</c>, <c>2026-07-15</c>, <c>19:24:10</c>, <c>12.3 °C</c>.</param>
/// <param name="Pinned">True when the user pinned it; false when it is auto.</param>
/// <param name="From">Where the value came from: <c>pinned</c>, or <c>auto: …</c> with what it followed.</param>
public sealed record SliderReading(string Value, bool Pinned, string From)
{
    /// <summary>The value as a number in the slider's unit (<see cref="Unit"/>); null for a slider that is not a number.</summary>
    public double? Number { get; init; }

    /// <summary>The unit of <see cref="Number"/>: m, °C, %, km or h (local solar hours); null with no number.</summary>
    public string? Unit { get; init; }
}

/// <summary>
/// Resolves the linked sliders (vista-plan §3.4, §3.6, §10 M1.11): every slider is pinned (the slider file's value) or auto, and
/// an auto slider follows the controls upstream of it, in this order — place → altitude → season ⇄ date → time of day → solar
/// time → the climate's day at the valley floor → temperature → humidity → freezing level → snow; the day's humidity → haze. Pinning one
/// slider stops only that one: a pinned temperature still moves the auto humidity, snow and haze. The set pieces push defaults
/// onto auto sliders (<see cref="SliderPushes"/>). Contradictions are never errors: they render as asked, with an "implausible"
/// hint.
///
/// <para><b>Season and date drive each other</b> (§3.5): a pinned date decides the auto season (autumn at Torres del Paine is
/// April), and a pinned or default season decides the auto date. <b>The weather chain:</b> the climate normals' low and high for
/// the date, moved from the station to the valley floor along the lapse rate, on a daily curve (<see cref="DailyTemperature"/>:
/// the low at sunrise, the high at 14:00) give the temperature; the day's low is the dew point, so the humidity follows the
/// temperature (<see cref="Humidity"/>); the temperature and the lapse rate give the freezing level. <b>Snow is history,
/// temperature is now:</b> the season's snowline comes from a degree-day snowpack over the climate's year
/// (<see cref="SnowCover"/>), and a temperature away from the climate's at the hour — pinned, or pushed by a frozen lake — moves
/// it by <see cref="SnowFollowsFreezingLevel"/> of the freezing level's move, so temperature nudges the snow without deciding it.
/// <b>Haze</b> grows with the day's humidity — at the day's mean temperature, so the hour does not move it (<see cref="Haze"/>);
/// a pinned humidity grows it instead, and a pinned visibility sets it.</para>
///
/// <para>Pure: the same recipe and pins give the same state, bit for bit.</para>
/// </summary>
public static class LinkedSliders
{
    /// <summary>The share of the freezing level's move (from a temperature off the climate's) that the auto snowline follows.</summary>
    public const double SnowFollowsFreezingLevel = 0.5;

    /// <summary>A frozen lake with the valley warmer than this, °C, is implausible.</summary>
    public const double FrozenLakeImplausibleCelsius = 2.0;

    /// <summary>Seasonal snow lying more than this far below the freezing level, m, is implausible.</summary>
    public const double SnowBelowFreezingLevelImplausibleMetres = 1500.0;

    /// <summary>Resolves <paramref name="pins"/> for <paramref name="recipe"/>'s set pieces, the haze in <paramref name="air"/> (the default model when null).</summary>
    public static SliderState Resolve(Recipe recipe, SliderPins pins, AtmosphereModel? air = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(pins);
        SliderPushes pushes = SliderPushes.For(recipe.SetPieces);
        var readings = new Dictionary<Slider, SliderReading>();
        var implausible = new List<string>();
        var notes = new List<string>();

        // The place.
        PlaceInfo place;
        if (pins.Latitude is double latitude)
        {
            place = Places.Custom(latitude);
            readings[Slider.Location] = new SliderReading(Invariant($"latitude {latitude}°"), true, "pinned") { Number = latitude, Unit = "°" };
        }
        else
        {
            (Place chosen, bool pinned, string from) = pins.Location != Place.None
                ? (pins.Location, true, "pinned")
                : pushes.Place is Place pushed ? (pushed, false, "auto: " + pushes.By(Slider.Location)) : (Places.Default, false, "auto: the default place");
            place = Places.Get(chosen);
            readings[Slider.Location] = new SliderReading(JsonName(chosen.ToString()), pinned, from);
        }

        // The scene's base altitude.
        double altitude = pins.Altitude ?? place.ValleyAltitudeMetres;
        readings[Slider.Altitude] = new SliderReading(Invariant($"{altitude:0.#} m"), pins.Altitude is not null,
            pins.Altitude is not null ? "pinned" : place.IsCustom ? "auto: sea level (custom latitude)" : $"auto: the valley floor of {place.Name} ({place.Spot})")
        { Number = altitude, Unit = "m" };

        // Season and date, each deciding the other's auto value.
        Season season;
        DateOnly date;
        string hemisphere = place.Southern ? "southern" : "northern";
        if (pins.Date is DateOnly pinnedDate)
        {
            date = pinnedDate;
            season = pins.Season ?? SeasonCalendar.SeasonOf(date, place.LatitudeDegrees);
            readings[Slider.Date] = new SliderReading(Iso(date), true, "pinned");
            readings[Slider.Season] = new SliderReading(JsonName(season.ToString()), pins.Season is not null,
                pins.Season is not null ? "pinned" : $"auto: the pinned date's, in the {hemisphere} hemisphere");
            Season actual = SeasonCalendar.SeasonOf(date, place.LatitudeDegrees);
            if (pins.Season is Season pinnedSeason && pinnedSeason != actual)
                implausible.Add($"Implausible: {Iso(date)} is {JsonName(actual.ToString())} at {place.Name}, but the season is pinned to {JsonName(pinnedSeason.ToString())}.");
        }
        else
        {
            (season, string seasonFrom) = pins.Season is Season pinnedSeason
                ? (pinnedSeason, "pinned")
                : pushes.Season is Season pushed ? (pushed, "auto: " + pushes.By(Slider.Season)) : (Season.Summer, "auto: the default season");
            date = SeasonCalendar.DateFor(season, place.LatitudeDegrees);
            readings[Slider.Season] = new SliderReading(JsonName(season.ToString()), pins.Season is not null, seasonFrom);
            readings[Slider.Date] = new SliderReading(Iso(date), false, $"auto: {JsonName(season.ToString())} in the {hemisphere} hemisphere (the 15th of its middle month)");
        }

        // The time of day: the preset, then the solar time it (or a pin) gives.
        (TimeOfDay time, string timeFrom) = pins.Time is TimeOfDay pinnedTime
            ? (pinnedTime, "pinned")
            : pushes.Time is TimeOfDay pushedTime ? (pushedTime, "auto: " + pushes.By(Slider.Time)) : (TimeOfDay.Day, "auto: the default time of day");
        readings[Slider.Time] = new SliderReading(JsonName(time.ToString()), pins.Time is not null, timeFrom);
        Almanac almanac = Almanac.Resolve(place, date, pins.Date is null, season, time, pins.SolarTime, pins.SunElevation);
        bool timePinned = pins.IsPinned(Slider.SolarTime);
        readings[Slider.SolarTime] = new SliderReading(Almanac.FormatHours(almanac.SolarHours), timePinned,
            timePinned ? almanac.SolarTimeFrom : "auto: " + almanac.SolarTimeFrom)
        { Number = almanac.SolarHours, Unit = "h" };

        // The climate's day at the valley floor, and the temperature at the hour.
        ClimateNormals climate = Weather.Climate.For(place);
        DayNormals stationDay = climate.On(date);
        DayNormals day = stationDay.Raised(altitude - climate.StationAltitudeMetres);
        double climateTemperature = DailyTemperature.At(day, almanac.SolarHours, DailyTemperature.MinimumHours(almanac.Daylight));
        double temperature;
        string temperatureFrom;
        if (pins.Temperature is double pinnedTemperature)
        {
            temperature = pinnedTemperature;
            temperatureFrom = "pinned";
        }
        else
        {
            temperature = climateTemperature;
            temperatureFrom = Invariant($"auto: {climate.Station} normals ({climate.Period}) for {Iso(date)}, low {stationDay.LowCelsius:0.0} / high {stationDay.HighCelsius:0.0} °C at {climate.StationAltitudeMetres:0} m, at {altitude:0} m on the daily curve at {Almanac.FormatHours(almanac.SolarHours)}");
            if (pushes.TemperatureCeilingCelsius is double ceiling && temperature > ceiling)
            {
                temperature = ceiling;
                temperatureFrom += Invariant($", lowered to {ceiling:0.#} °C ({pushes.By(Slider.Temperature)})");
            }
        }
        readings[Slider.Temperature] = new SliderReading(Celsius(temperature, pins.TemperatureUnit), pins.Temperature is not null, temperatureFrom)
        { Number = temperature, Unit = "°C" };

        // Humidity, from the temperature and the day's dew point.
        double dewPoint = day.LowCelsius;
        double humidity = pins.Humidity ?? Humidity.RelativePercent(temperature, dewPoint);
        readings[Slider.Humidity] = new SliderReading(Invariant($"{humidity:0.#}%"), pins.Humidity is not null,
            pins.Humidity is not null ? "pinned" : Invariant($"auto: {temperature:0.0} °C air with its dew point at the day's low, {dewPoint:0.0} °C"))
        { Number = humidity, Unit = "%" };

        // The freezing level, and the snowline: the climate's season, nudged by a temperature off the climate's.
        double freezingLevel = Lapse.FreezingLevel(altitude, temperature);
        double climateSnowline = SnowCover.Snowline(climate, date, SliderPins.MaxSnowlineMetres);
        double nudge = SnowFollowsFreezingLevel * (temperature - climateTemperature) / Lapse.RateCelsiusPerMetre;
        double snowline = pins.Snowline ?? Math.Clamp(climateSnowline + nudge, 0.0, SliderPins.MaxSnowlineMetres);
        string snowFrom = pins.Snowline is not null
            ? "pinned"
            : Invariant($"auto: the season's snow cover from the {climate.Station} normals, {climateSnowline:0} m")
                + (nudge == 0.0 ? string.Empty : Invariant($", moved {nudge:+0;−0} m with the freezing level"));
        readings[Slider.Snow] = new SliderReading(Invariant($"{snowline:0} m"), pins.Snowline is not null, snowFrom) { Number = snowline, Unit = "m" };

        // Haze, from the day's humidity or a pinned visibility. The haze layer (1.2 km deep) sees the day's air, not the ground's
        // swing from a saturated dawn to a dry afternoon, which is dew and fog (M4); so the hour leaves the air's tables as they are.
        double dayTemperature = pins.Temperature
            ?? (pushes.TemperatureCeilingCelsius is double dayCeiling ? Math.Min(day.MeanCelsius, dayCeiling) : day.MeanCelsius);
        double hazeHumidity = pins.Humidity ?? Humidity.RelativePercent(dayTemperature, dewPoint);
        AtmosphereModel model = air ?? AtmosphereModel.Default;
        double opticalDepth;
        double visibility;
        string hazeFrom;
        if (pins.Visibility is double pinnedVisibility)
        {
            visibility = pinnedVisibility;
            opticalDepth = Haze.OpticalDepthForVisibility(model, visibility, altitude);
            hazeFrom = Invariant($"pinned; haze optical depth {opticalDepth:0.000} at 550 nm");
            double clean = Haze.CleanAirVisibilityKm(model, altitude);
            if (visibility > clean)
                notes.Add(Invariant($"Clean air limits the view to {clean:0} km at {altitude:0} m, so the pinned {visibility:0} km renders as air without haze."));
        }
        else
        {
            opticalDepth = Haze.OpticalDepth(hazeHumidity);
            visibility = Haze.VisibilityKm(model with { AerosolOpticalDepth550 = opticalDepth }, altitude);
            hazeFrom = pins.Humidity is not null
                ? Invariant($"auto: haze grown by the pinned {hazeHumidity:0.#}% humidity, optical depth {opticalDepth:0.000} at 550 nm")
                : Invariant($"auto: haze grown by the day's {hazeHumidity:0.#}% humidity (at {dayTemperature:0.0} °C), optical depth {opticalDepth:0.000} at 550 nm");
        }
        readings[Slider.Haze] = new SliderReading(Invariant($"{visibility:0.#} km"), pins.Visibility is not null, hazeFrom) { Number = visibility, Unit = "km" };

        // Contradictions: rendered as asked, with a hint.
        bool frozenLake = recipe.SetPieces.Contains(SetPiece.FrozenLake);
        if (frozenLake && pins.Temperature is not null && temperature > FrozenLakeImplausibleCelsius)
            implausible.Add(Invariant($"Implausible: a frozen lake with the valley at {Celsius(temperature, pins.TemperatureUnit)} (the temperature is pinned)."));
        if (frozenLake && pins.Season == Season.Summer)
            implausible.Add("Implausible: a frozen lake in summer (the season is pinned).");
        if ((pins.Snowline is not null || pins.Temperature is not null) && snowline < freezingLevel - SnowBelowFreezingLevelImplausibleMetres)
            implausible.Add(Invariant($"Implausible: seasonal snow down to {snowline:0} m with the freezing level at {freezingLevel:0} m."));

        return new SliderState(pins, pushes, almanac, altitude, climate, stationDay, day, climateTemperature, temperature, dewPoint, humidity,
            freezingLevel, climateSnowline, snowline, hazeHumidity, opticalDepth, visibility, readings, implausible, notes);
    }

    /// <summary>A temperature as shown: °C, or °F with °C when the unit asks for it.</summary>
    public static string Celsius(double celsius, TemperatureUnit unit) => unit == TemperatureUnit.Fahrenheit
        ? Invariant($"{celsius * 9.0 / 5.0 + 32.0:0.0} °F ({celsius:0.0} °C)")
        : Invariant($"{celsius:0.0} °C");

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string JsonName(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
