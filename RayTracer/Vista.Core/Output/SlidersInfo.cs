using System.Text.Json;
using Vista.Sliders;
using Vista.Weather;

namespace Vista.Output;

/// <summary>The linked sliders of one image (sidecar schema 12, vista-plan §10 M1.11).</summary>
/// <param name="File">The slider file read; null when none was given.</param>
/// <param name="PinsFrom">Where the pins came from: "slider file", "recipe" (its own place and time fields), "none", each "and options" when slider options added pins.</param>
/// <param name="Pins">The pins as resolved, options included: the sliders pinned and their values (the rest are null).</param>
/// <param name="Values">Every slider's value, pin and origin, by slider (<c>location</c>, <c>altitude</c>, <c>season</c>, <c>date</c>, <c>time</c>, <c>solarTime</c>, <c>temperature</c>, <c>humidity</c>, <c>snow</c>, <c>haze</c>).</param>
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

    /// <summary>Recorded in <see cref="Models"/>.</summary>
    public static readonly string ModelNotes = string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"monthly normals interpolated from mid-month; lapse rate {Lapse.RateCelsiusPerMetre * 1000.0:0.0} °C/km; daily curve: two half cosines, low at sunrise, high at {DailyTemperature.MaximumHours:0}:00 solar; " +
        $"dew point = the day's low (FAO-56); Magnus (Alduchov & Eskridge 1996); snow: degree-day pack, {SnowCover.PrecipitationMillimetresPerDay:0.#} mm/day, snow at ≤ {SnowCover.SnowfallCelsius:0.#} °C, {SnowCover.DegreeDayFactor:0.#} mm/°C/day melt, cover over {SnowCover.CoverMillimetres:0} mm, nudged by {LinkedSliders.SnowFollowsFreezingLevel:0.##} of the freezing level's move; " +
        $"haze: the day's humidity (at its mean temperature), (1 − RH)^−{Haze.GrowthExponent:0.##} growth, optical depth {Haze.ReferenceOpticalDepth:0.###} at {Haze.ReferenceHumidityPercent:0}% RH, capped at {Haze.GrowthLimitPercent:0}%; visibility ln 20/β at 550 nm (the WMO's meteorological optical range)");

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
        };
    }
}
