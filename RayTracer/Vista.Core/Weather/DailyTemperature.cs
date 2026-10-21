using Vista.Astronomy;

namespace Vista.Weather;

/// <summary>
/// The air temperature through a day (vista-plan §3.6): the day's normal low near sunrise and its high in mid-afternoon, joined by
/// two half cosines — rising from sunrise to <see cref="MaximumHours"/> and falling from there to the next sunrise — the simple
/// curve of crop models since the WAVE model (De Wit et al. 1978: the low at sunrise, the high at 14:00). Without a sunrise (polar
/// day or night) the low falls at solar midnight.
/// </summary>
public static class DailyTemperature
{
    /// <summary>The local solar time of the day's high: mid-afternoon, two hours after solar noon.</summary>
    public const double MaximumHours = 14.0;

    /// <summary>
    /// The temperature at <paramref name="solarHours"/> local solar time on a day with <paramref name="normals"/>, its low at
    /// <paramref name="minimumHours"/> (the day's sunrise).
    /// </summary>
    public static double At(DayNormals normals, double solarHours, double minimumHours)
    {
        if (!(minimumHours is >= 0.0 and < MaximumHours))
            throw new ArgumentOutOfRangeException(nameof(minimumHours), minimumHours, "The low must fall between midnight and the high.");
        double low = normals.LowCelsius;
        double range = normals.HighCelsius - normals.LowCelsius;
        double t = solarHours < minimumHours ? solarHours + 24.0 : solarHours;
        if (t <= MaximumHours)
            return low + range * 0.5 * (1.0 - Math.Cos(Math.PI * (t - minimumHours) / (MaximumHours - minimumHours)));
        return low + range * 0.5 * (1.0 + Math.Cos(Math.PI * (t - MaximumHours) / (minimumHours + 24.0 - MaximumHours)));
    }

    /// <summary>When the day's low falls: at sunrise, or at solar midnight when the sun neither rises nor sets.</summary>
    public static double MinimumHours(Daylight daylight) => daylight.HasSunriseAndSunset ? daylight.SunriseHours : 0.0;
}
