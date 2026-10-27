using System.Text.Json;
using Vista.Sliders;
using Vista.Weather;
using Vista.World;

namespace Vista.Output;

/// <summary>The linked sliders of one image (sidecar schema 12, vista-plan §10 M1.11).</summary>
/// <param name="File">The slider file read; null when none was given.</param>
/// <param name="PinsFrom">Where the pins came from: "slider file", "recipe" (its own place and time fields), "none", each "and options" when slider options added pins.</param>
/// <param name="Pins">The pins as resolved, options included: the sliders pinned and their values (the rest are null).</param>
/// <param name="Values">Every slider's value, pin and origin, by slider (<c>location</c>, <c>altitude</c>, <c>season</c>, <c>date</c>, <c>time</c>, <c>solarTime</c>, <c>temperature</c>, <c>humidity</c>, <c>snow</c>, <c>haze</c>, <c>valleyHaze</c>, <c>mist</c>).</param>
/// <param name="Weather">What the weather chain worked out on the way.</param>
/// <param name="Implausible">Contradictions between pins, or a pin and the set pieces: rendered as asked (also in the warnings).</param>
public sealed record SlidersInfo(
    string? File,
    string PinsFrom,
    SliderPins Pins,
    SortedDictionary<string, SliderReading> Values,
    WeatherInfo Weather,
    IReadOnlyList<string> Implausible)
{
    /// <summary>This record with the valleys' pools the render got (schema 23).</summary>
    public SlidersInfo WithPools(PoolsInfo pools) => this with { Weather = Weather with { Pools = pools } };

    /// <summary>The record of <paramref name="state"/>, read from <paramref name="file"/> (null for none) and <paramref name="pinsFrom"/>.</summary>
    public static SlidersInfo From(SliderState state, string? file, string pinsFrom)
    {
        ArgumentNullException.ThrowIfNull(state);
        var values = new SortedDictionary<string, SliderReading>(StringComparer.Ordinal);
        foreach ((Slider slider, SliderReading reading) in state.Readings)
            values[JsonNamingPolicy.CamelCase.ConvertName(slider.ToString())] = reading;
        return new SlidersInfo(file, pinsFrom, state.Pins, values, WeatherInfo.From(state), state.Implausible);
    }
}

/// <summary>The weather chain's working for one image (vista-plan §3.6): the climate's day and what followed from it.</summary>
/// <param name="Station">The climate normals' station, or the latitude model for a custom latitude.</param>
/// <param name="StationAltitudeMetres">The station's altitude above sea level.</param>
/// <param name="Period">The normals period.</param>
/// <param name="Source">Where the normals come from.</param>
/// <param name="StationLowCelsius">The date's normal low at the station.</param>
/// <param name="StationHighCelsius">The date's normal high at the station.</param>
/// <param name="LowCelsius">The date's normal low at the scene's valley floor, along the lapse rate.</param>
/// <param name="HighCelsius">The date's normal high at the valley floor.</param>
/// <param name="ClimateTemperatureCelsius">The climate's temperature at the valley floor at the rendered hour, on the daily curve.</param>
/// <param name="DewPointCelsius">The dew point at the valley floor (the day's low).</param>
/// <param name="FreezingLevelMetres">Where the air reaches 0 °C, m above sea level.</param>
/// <param name="ClimateSnowlineMetres">The season's snowline from the climate alone, m above sea level.</param>
/// <param name="HazeHumidityPercent">The humidity the haze grew by: pinned, or the day's (at its mean temperature), %.</param>
/// <param name="AerosolOpticalDepth550">The haze rendered: vertical optical depth at 550 nm from sea level.</param>
/// <param name="Models">How each step was modelled.</param>
/// <remarks>Since schema 20 (vista-plan §10 M1.12c) the valley floor is the world's lowest valleys, <see cref="FootingMetres"/> above the base altitude.</remarks>
public sealed record WeatherInfo(
    string Station,
    double StationAltitudeMetres,
    string Period,
    string Source,
    double StationLowCelsius,
    double StationHighCelsius,
    double LowCelsius,
    double HighCelsius,
    double ClimateTemperatureCelsius,
    double DewPointCelsius,
    double FreezingLevelMetres,
    double ClimateSnowlineMetres,
    double HazeHumidityPercent,
    double AerosolOpticalDepth550,
    string Models)
{
    /// <summary>The valley floor's altitude the weather was read at, m above sea level (schema 20).</summary>
    public double ValleyFloorMetres { get; init; }

    /// <summary>How far the valley floor stands above the base altitude, the world's footing, m (schema 20).</summary>
    public double FootingMetres { get; init; }

    /// <summary>The valley haze's layer: its lid, its haze and the pool it came from (schema 22, vista-plan §10 M1.13a).</summary>
    public ValleyHazeLayer ValleyHaze { get; init; } = ValleyHazeLayer.None;

    /// <summary>The valleys' pools the render got (schema 23, vista-plan §10 M1.13b); null for a sidecar written without them.</summary>
    public PoolsInfo? Pools { get; init; }

    /// <summary>Recorded in <see cref="Models"/>.</summary>
    public static readonly string ModelNotes = string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"monthly normals interpolated from mid-month; lapse rate {Lapse.RateCelsiusPerMetre * 1000.0:0.0} °C/km; daily curve: two half cosines, low at sunrise, high at {DailyTemperature.MaximumHours:0}:00 solar; " +
        $"dew point = the day's low (FAO-56) for the humidity and the haze, the humidity normals' carried by Hann's formula (vapour ×10 per {ValleyPools.VapourTenfoldMetres:0} m) for the mist; Magnus (Alduchov & Eskridge 1996); snow: degree-day pack, the place's monthly precipitation (else {SnowCover.PrecipitationMillimetresPerDay:0.#} mm/day) rising {SnowCover.PrecipitationGradientPerMetre * 100.0 * 100.0:0.#}%/100 m to {SnowCover.MaxPrecipitationFactor:0.#}×, snowfall ×{SnowCover.SnowCatchFactor:0.##} for the gauges' undercatch, snow at ≤ {SnowCover.SnowfallCelsius:0.#} °C, {SnowCover.DegreeDayFactor:0.#} mm/°C/day melt, cover over {SnowCover.CoverMillimetres:0} mm, nudged by {LinkedSliders.SnowFollowsFreezingLevel:0.##} of the freezing level's move; " +
        $"haze: the day's humidity (at its mean temperature), (1 − RH)^−{Haze.GrowthExponent:0.##} growth, optical depth {Haze.ReferenceOpticalDepth:0.###} at {Haze.ReferenceHumidityPercent:0}% RH, capped at {Haze.GrowthLimitPercent:0}%; visibility ln 20/β at 550 nm (the WMO's meteorological optical range); " +
        $"valley haze: a cold pool cooled {Weather.ValleyHaze.CoolingShareOfRange:0.##} of the day's range below the free air, its top where the inversion's {Weather.ValleyHaze.InversionLapseCelsiusPerMetre * 1000.0:0} °C/km rise and the lapse rate close it (at most {Weather.ValleyHaze.ReliefShare:0.##} of the relief), " +
        $"broken up by clear-sky sun (Meinel 1976) × {Weather.ValleyHaze.SensibleShare:0.##} sensible × {Weather.ValleyHaze.ValleyAmplification:0.#} valley amplification × the floor's absorption (albedo {Weather.ValleyHaze.GroundAlbedo:0.##}, snow {Weather.ValleyHaze.SnowAlbedo:0.##}) against its heat deficit, " +
        $"forming again on the daily curve after the high; its haze the valley's, held up to ×{Weather.ValleyHaze.Trapping:0.#} by dawn and grown by the hour's humidity; a logistic lid {Vista.Atmosphere.AtmosphereModel.Default.ValleyHazeSoftnessMetres:0} m soft; " +
        $"each valley's pool (M1.13b): the night's depth × its hold (1/(1 + (g/{PoolMap.HalfHoldGradient:0.##})²) on the floor's gradient over {PoolMap.GradientReachMetres:0} m) at most its cap (its surroundings over σ {PoolMap.ReliefSigmaMetres:0} m above its floor), on the floor its air drains to (≥ {PoolMap.ChannelAreaSquareMetres / 1e6:0.#} km²), left 1 − q/r² by the sun; " +
        $"its haze grown by the humidity at each height in it (the inversion {Weather.ValleyHaze.InversionLapseCelsiusPerMetre * 1000.0:0} °C/km, the dew point −{ValleyPools.DewPointLapseCelsiusPerMetre * 1000.0:0.#} °C/km); " +
        $"mist where it is colder than its dew point, {ValleyPools.MistWaterPerDegree:0.##} g/m³ of water per °C below it up to {ValleyPools.MistWaterLimit:0.###}, Kunkel's (1984) β = {ValleyPools.KunkelCoefficient:0.####}·LWC^{ValleyPools.KunkelExponent:0.##}; a pinned mist fills {ValleyPools.PinnedMistShare:0%} of the scene's valley's pool");

    /// <summary>The working of <paramref name="state"/>.</summary>
    public static WeatherInfo From(SliderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ClimateNormals climate = state.Climate;
        return new WeatherInfo(
            climate.Station,
            climate.StationAltitudeMetres,
            climate.Period,
            climate.Source,
            state.StationDay.LowCelsius,
            state.StationDay.HighCelsius,
            state.Day.LowCelsius,
            state.Day.HighCelsius,
            state.ClimateTemperatureCelsius,
            state.DewPointCelsius,
            state.FreezingLevelMetres,
            state.ClimateSnowlineMetres,
            state.HazeHumidityPercent,
            state.AerosolOpticalDepth550,
            ModelNotes)
        {
            ValleyFloorMetres = state.ValleyFloorMetres,
            FootingMetres = state.FootingMetres,
            ValleyHaze = state.ValleyHaze,
        };
    }
}

/// <summary>
/// The valleys' pools a render got (vista-plan §10 M1.13b; schema 23): how they were rendered, what the weather told them, what they
/// added up to over the world, and the flat layer the atmosphere's tables carried in their place (their altitude average).
/// </summary>
/// <param name="Mode"><c>pools</c> (each valley's in three dimensions), <c>flat</c> (M1.13a's one layer, <c>--pools flat</c>) or <c>none</c>.</param>
/// <param name="Weather">What the weather told the pools: the scene's valley's night's pool, its hour, its air and dew point.</param>
/// <param name="Stats">What they added up to over the world's map.</param>
/// <param name="LayerTopMetres">The tables' flat layer's top, m above sea level; null for none.</param>
/// <param name="LayerExtinction550">The tables' flat layer's extinction at 550 nm under its top, per metre; null for none.</param>
public sealed record PoolsInfo(string Mode, PoolWeather Weather, PoolStats Stats, double? LayerTopMetres, double? LayerExtinction550)
{
    /// <summary>The record of a render whose pools were <paramref name="pools"/> (none: <see cref="ValleyPools.None"/>) in <paramref name="air"/>.</summary>
    public static PoolsInfo Of(bool threeDimensional, PoolWeather weather, ValleyPools pools, Vista.Atmosphere.AtmosphereModel air)
    {
        ArgumentNullException.ThrowIfNull(weather);
        ArgumentNullException.ThrowIfNull(pools);
        ArgumentNullException.ThrowIfNull(air);
        bool layer = air.ValleyHazeExtinction550 > 0.0;
        string mode = !threeDimensional ? "flat" : pools.Any ? "pools" : "none";
        return new PoolsInfo(mode, weather, pools.Stats, layer ? air.ValleyHazeTopMetres : null, layer ? air.ValleyHazeExtinction550 : null);
    }
}
