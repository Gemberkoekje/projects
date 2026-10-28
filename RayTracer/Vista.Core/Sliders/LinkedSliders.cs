using System.Globalization;
using System.Text.Json;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Recipes;
using Vista.Weather;
using Vista.World;

namespace Vista.Sliders;

/// <summary>
/// The linked sliders resolved (vista-plan §3.4, §3.6, §10 M1.11): every slider's value, whether it was pinned, and where an auto
/// value came from, plus what the weather chain worked out on the way.
/// </summary>
/// <param name="Pins">The pins it was resolved from.</param>
/// <param name="Pushes">What the recipe's set pieces pushed.</param>
/// <param name="Almanac">The place, date, time and sun.</param>
/// <param name="BaseAltitudeMetres">
/// The scene's base altitude above sea level (world <c>y = 0</c>); since M1.12c the world's lowest valleys stand
/// <see cref="FootingMetres"/> above it, at <see cref="ValleyFloorMetres"/>.
/// </param>
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

    /// <summary>The pinned sliders an <see cref="Implausible"/> hint is about, so the window marks each one's row (vista-plan §10 M1.16).</summary>
    public IReadOnlySet<Slider> ImplausibleSliders { get; init; } = new HashSet<Slider>();

    /// <summary>
    /// How far the world's lowest valleys stand above its height 0, m (vista-plan §10 M1.12c, <see cref="WorldFooting"/>); 0 when
    /// resolved without a world.
    /// </summary>
    public double FootingMetres { get; init; }

    /// <summary>
    /// The altitude of the scene's valley floor above sea level, m: the base altitude plus <see cref="FootingMetres"/>, where the
    /// weather chain reads the air (the temperature, the humidity, the visibility).
    /// </summary>
    public double ValleyFloorMetres { get; init; }

    /// <summary>
    /// The valley haze slider's layer (vista-plan §10 M1.13a): since M1.13b the scene's own valley's pool, its haze's mean under its top;
    /// none when resolved before it existed.
    /// </summary>
    public ValleyHazeLayer ValleyHaze { get; init; } = ValleyHazeLayer.None;

    /// <summary>What the weather tells every valley's pool (vista-plan §10 M1.13b, <see cref="ValleyPools"/>); none when resolved before it existed.</summary>
    public PoolWeather Pools { get; init; } = PoolWeather.None;

    /// <summary>
    /// <paramref name="model"/> with the haze the sliders settled on and a valley haze layer: <paramref name="layer"/> (the valleys' pools
    /// averaged over the world, <see cref="ValleyPools.AverageLayer"/>), or without one the scene's own valley's pool as one flat layer.
    /// </summary>
    public AtmosphereModel Air(AtmosphereModel model, (double TopMetres, double Extinction550)? layer = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        AtmosphereModel air = model with { AerosolOpticalDepth550 = AerosolOpticalDepth550 };
        if (layer is { } average)
            return average.Extinction550 > 0.0 ? air with { ValleyHazeTopMetres = average.TopMetres, ValleyHazeExtinction550 = average.Extinction550 } : air;
        return ValleyHaze.Any ? air with { ValleyHazeTopMetres = ValleyHaze.TopMetres, ValleyHazeExtinction550 = ValleyHaze.Extinction550 } : air;
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
/// time → the climate's day at the valley floor → temperature → humidity → freezing level → snow; the day's humidity → haze; the day's
/// range, the hour, the snow and the haze → the valley haze; the valley haze and the dew point (the humidity normals) → the mist. Pinning one
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
/// a pinned humidity grows it instead, and a pinned visibility sets it. <b>The valley haze</b> (since M1.13a, <see cref="Weather.ValleyHaze"/>)
/// is the haze a night's cold pool holds under its lid: deepest at dawn, broken up by the morning's sun (slowly over a snowy floor),
/// forming again in the evening; a pinned visibility under the lid sets it at any hour.</para>
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

    /// <summary>
    /// Resolves <paramref name="pins"/> for <paramref name="recipe"/>'s set pieces, the haze in <paramref name="air"/> (the default
    /// model when null), on a world standing on <paramref name="footing"/> (vista-plan §10 M1.12c): the auto altitude puts the world's
    /// lowest valleys, not its height 0, at the place's valley floor, and the weather is read there. Without a footing height 0 is the
    /// valley floor, as before M1.12c.
    /// </summary>
    public static SliderState Resolve(Recipe recipe, SliderPins pins, AtmosphereModel? air = null, WorldFooting? footing = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(pins);
        SliderPushes pushes = SliderPushes.For(recipe.SetPieces);
        var readings = new Dictionary<Slider, SliderReading>();
        var implausible = new List<string>();
        var flagged = new HashSet<Slider>();
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

        // The scene's base altitude: pinned, or where it puts the world's lowest valleys at the place's valley floor (never below sea
        // level); the weather is read at the valley floor, the world's footing above it.
        double lift = footing?.HeightMetres ?? 0.0;
        double altitude;
        string altitudeFrom;
        if (pins.Altitude is double pinnedAltitude)
        {
            altitude = pinnedAltitude;
            altitudeFrom = "pinned";
        }
        else
        {
            altitude = Math.Max(place.ValleyAltitudeMetres - lift, 0.0);
            altitudeFrom = place.IsCustom ? "auto: sea level (custom latitude)" : $"auto: the valley floor of {place.Name} ({place.Spot})";
            if (footing is not null)
            {
                altitudeFrom += Invariant($", where the world's lowest valleys stand (its {footing.Percentile:0.#}th percentile, {lift:0} m above its height 0)");
                if (place.ValleyAltitudeMetres - lift < 0.0)
                    altitudeFrom += ", held at sea level";
            }
        }
        readings[Slider.Altitude] = new SliderReading(Invariant($"{altitude:0.#} m"), pins.Altitude is not null, altitudeFrom) { Number = altitude, Unit = "m" };
        double floor = altitude + lift;

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
            {
                implausible.Add($"Implausible: {Iso(date)} is {JsonName(actual.ToString())} at {place.Name}, but the season is pinned to {JsonName(pinnedSeason.ToString())}.");
                flagged.UnionWith([Slider.Date, Slider.Season]);
            }
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
        DayNormals day = stationDay.Raised(floor - climate.StationAltitudeMetres);
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
            temperatureFrom = Invariant($"auto: {climate.Station} normals ({climate.Period}) for {Iso(date)}, low {stationDay.LowCelsius:0.0} / high {stationDay.HighCelsius:0.0} °C at {climate.StationAltitudeMetres:0} m, at {floor:0} m on the daily curve at {Almanac.FormatHours(almanac.SolarHours)}");
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
        double freezingLevel = Lapse.FreezingLevel(floor, temperature);
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
            opticalDepth = Haze.OpticalDepthForVisibility(model, visibility, floor);
            hazeFrom = Invariant($"pinned; haze optical depth {opticalDepth:0.000} at 550 nm");
            double clean = Haze.CleanAirVisibilityKm(model, floor);
            if (visibility > clean)
                notes.Add(Invariant($"Clean air limits the view to {clean:0} km at {floor:0} m, so the pinned {visibility:0} km renders as air without haze."));
        }
        else
        {
            opticalDepth = Haze.OpticalDepth(hazeHumidity);
            visibility = Haze.VisibilityKm(model with { AerosolOpticalDepth550 = opticalDepth }, floor);
            hazeFrom = pins.Humidity is not null
                ? Invariant($"auto: haze grown by the pinned {hazeHumidity:0.#}% humidity, optical depth {opticalDepth:0.000} at 550 nm")
                : Invariant($"auto: haze grown by the day's {hazeHumidity:0.#}% humidity (at {dayTemperature:0.0} °C), optical depth {opticalDepth:0.000} at 550 nm");
        }
        readings[Slider.Haze] = new SliderReading(Invariant($"{visibility:0.#} km"), pins.Visibility is not null, hazeFrom) { Number = visibility, Unit = "km" };

        // The valley haze (M1.13a, M1.13b): the night's cold pool from the day's range and what the sun has left of it, each valley's
        // pool scaled by its shape from these (ValleyPools); the readings are the scene's own valley's, the deepest and densest.
        AtmosphereModel valleyAir = model with { AerosolOpticalDepth550 = opticalDepth };
        double hazeExtinction = valleyAir.CoefficientsAt(550.0).AerosolExtinction * valleyAir.AerosolDensity(floor);
        double cooling = Weather.ValleyHaze.Cooling(day);
        double? relief = footing is { } f && f.CrestMetres > f.HeightMetres ? f.ReliefMetres : null; // a footing without a crest: no cap
        double dawnDepth = Weather.ValleyHaze.Depth(cooling, relief);
        double deficit = Weather.ValleyHaze.HeatDeficit(cooling, dawnDepth, floor);
        double absorbed = Weather.ValleyHaze.Absorbed(floor, snowline);
        double minimum = DailyTemperature.MinimumHours(almanac.Daylight);
        (double poolShare, bool morning, double heat) = Weather.ValleyHaze.PoolShare(place.LatitudeDegrees, date, almanac.SolarHours, minimum, deficit, absorbed);

        // The dew point the pools' mist reads (M1.13b): a pinned humidity's at the hour, else the place's humidity normals (their station's
        // dew point carried to the valley floor), else the day's low.
        double mistDewPoint;
        string dewFrom;
        if (pins.Humidity is double pinnedHumidity)
        {
            mistDewPoint = Humidity.DewPointCelsius(temperature, pinnedHumidity);
            dewFrom = Invariant($"the pinned {pinnedHumidity:0.#}% humidity's dew point, {mistDewPoint:0.0} °C");
        }
        else if (climate.DewPointOn(date) is double stationDew)
        {
            double humidityAltitude = climate.HumidityAltitudeMetres ?? climate.StationAltitudeMetres;
            mistDewPoint = ValleyPools.DewPointAbove(stationDew, floor - humidityAltitude);
            dewFrom = Invariant($"a dew point of {mistDewPoint:0.0} °C from the humidity normals ({climate.HumiditySource})");
        }
        else
        {
            mistDewPoint = day.LowCelsius;
            dewFrom = Invariant($"a dew point at the day's low, {mistDewPoint:0.0} °C (no humidity normals)");
        }

        double? pinnedAdded = pins.ValleyHaze is double pinnedValley
            ? Math.Max(0.0, Haze.VisibilityConstant / (pinnedValley * 1000.0) - Haze.VisibilityConstant / (visibility * 1000.0))
            : null;
        var pools = new PoolWeather
        {
            FloorMetres = floor,
            DawnDepthMetres = dawnDepth,
            HeatRatio = deficit > 0.0 ? heat / deficit : 0.0,
            Morning = morning,
            Cooled = morning ? 0.0 : 1.0 - DailyTemperature.At(new DayNormals(0.0, 1.0), almanac.SolarHours, minimum),
            WholePool = pins.ValleyHaze is not null || pins.Mist is > 0.0,
            PinnedExtinction550 = pinnedAdded,
            TemperatureCelsius = temperature,
            DewPointCelsius = mistDewPoint,
            HazeExtinction550 = hazeExtinction,
            DayHumidityPercent = hazeHumidity,
            AerosolScaleHeightMetres = model.AerosolScaleHeightMetres,
            MistVisibilityKm = pins.Mist is > 0.0 ? pins.Mist : null,
            MistOff = pins.Mist is 0.0,
        };
        PoolCell reference = ValleyPools.Cell(pools, floor, 1.0, double.MaxValue);
        string valleyFrom;
        if (pins.ValleyHaze is not null)
        {
            valleyFrom = Invariant($"pinned; the night's whole pool, {dawnDepth:0} m deep");
        }
        else
        {
            string left = morning
                ? Invariant($"{poolShare:0%} of it left at {Almanac.FormatHours(almanac.SolarHours)} after {heat / 1e6:0.00} of its {deficit / 1e6:0.00} MJ/m² from the sun on a floor absorbing {absorbed:0%} of it")
                : Invariant($"{poolShare:0%} of it formed at {Almanac.FormatHours(almanac.SolarHours)} as the air cools after the day's high");
            valleyFrom = Invariant($"auto: a cold pool {dawnDepth:0} m deep at dawn from the night's {cooling:0.0} °C of cooling ({Weather.ValleyHaze.CoolingShareOfRange:0.##} of the day's {day.HighCelsius - day.LowCelsius:0.0} °C range), {left}");
        }
        var layer = ValleyHazeLayer.None;
        if (reference.DepthMetres > 0.0 && reference.HazeFloor550 > 0.0)
        {
            double under = Haze.VisibilityConstant / (Haze.VisibilityConstant / (visibility * 1000.0) + reference.HazeFloor550) / 1000.0;
            double share = dawnDepth > 0.0 ? reference.DepthMetres / dawnDepth : 0.0;
            layer = new ValleyHazeLayer(reference.TopMetres, reference.DepthMetres, 0.5 * (reference.HazeFloor550 + reference.HazeTop550), under, share,
                dawnDepth, cooling);
            valleyFrom += Invariant($"; at the floor of the scene's valley the haze adds {reference.HazeFloor550 * 1000.0:0.000}/km at 550 nm to the valley's {hazeExtinction * 1000.0:0.000}, {reference.HazeTop550 * 1000.0:0.000}/km at the pool's top, {reference.TopMetres:0} m; each valley's pool on its own floor, scaled by its shape");
        }
        readings[Slider.ValleyHaze] = new SliderReading(layer.Any ? Invariant($"{layer.VisibilityKm:0.#} km at the floor, its top at {layer.TopMetres:0} m") : "none",
            pins.ValleyHaze is not null, valleyFrom)
        { Number = layer.Any ? layer.VisibilityKm : visibility, Unit = "km" };

        // The mist (M1.13b): where the scene's valley's pool cools to its dew point.
        double mistKm = reference.MistMetres > 0.0 ? Haze.VisibilityConstant / ValleyPools.MistExtinction(reference.MistWater) / 1000.0 : 0.0;
        string mistFrom = pins.Mist is double pinnedMist
            ? pinnedMist > 0.0 ? Invariant($"pinned; filling {ValleyPools.PinnedMistShare:0%} of the scene's valley's pool, the night's whole pool") : "pinned: none"
            : reference.DepthMetres > 0.0
                ? Invariant($"auto: the scene's valley's floor at {reference.FloorMetres:0} m in its pool's air, with {dewFrom}")
                : Invariant($"auto: no pool at {Almanac.FormatHours(almanac.SolarHours)}, with {dewFrom}");
        readings[Slider.Mist] = new SliderReading(mistKm > 0.0 ? Invariant($"{mistKm * 1000.0:0} m to see, {reference.MistMetres:0} m deep") : "none",
            pins.Mist is not null, mistFrom)
        { Number = mistKm, Unit = "km" };

        // Contradictions: rendered as asked, with a hint.
        bool frozenLake = recipe.SetPieces.Contains(SetPiece.FrozenLake);
        if (frozenLake && pins.Temperature is not null && temperature > FrozenLakeImplausibleCelsius)
        {
            implausible.Add(Invariant($"Implausible: a frozen lake with the valley at {Celsius(temperature, pins.TemperatureUnit)} (the temperature is pinned)."));
            flagged.Add(Slider.Temperature);
        }
        if (frozenLake && pins.Season == Season.Summer)
        {
            implausible.Add("Implausible: a frozen lake in summer (the season is pinned).");
            flagged.Add(Slider.Season);
        }
        if ((pins.Snowline is not null || pins.Temperature is not null) && snowline < freezingLevel - SnowBelowFreezingLevelImplausibleMetres)
        {
            implausible.Add(Invariant($"Implausible: seasonal snow down to {snowline:0} m with the freezing level at {freezingLevel:0} m."));
            flagged.UnionWith(((Slider[])[Slider.Snow, Slider.Temperature]).Where(pins.IsPinned));
        }

        return new SliderState(pins, pushes, almanac, altitude, climate, stationDay, day, climateTemperature, temperature, dewPoint, humidity,
            freezingLevel, climateSnowline, snowline, hazeHumidity, opticalDepth, visibility, readings, implausible, notes)
        {
            ImplausibleSliders = flagged,
            FootingMetres = lift,
            ValleyFloorMetres = floor,
            ValleyHaze = layer,
            Pools = pools,
        };
    }

    /// <summary>A temperature as shown: °C, or °F with °C when the unit asks for it.</summary>
    public static string Celsius(double celsius, TemperatureUnit unit) => unit == TemperatureUnit.Fahrenheit
        ? Invariant($"{celsius * 9.0 / 5.0 + 32.0:0.0} °F ({celsius:0.0} °C)")
        : Invariant($"{celsius:0.0} °C");

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string JsonName(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
