namespace Vista.Weather;

/// <summary>
/// The season's snowline from the climate (vista-plan §3.6: "snow cover is history, temperature is now"): a degree-day snowpack
/// run over the climate normals' year, at every altitude, up to the date. Each day's mean temperature at an altitude is the
/// station's normal mean less the lapse rate; on days at or below <see cref="SnowfallCelsius"/> the day's precipitation falls as
/// snow, and on days above 0 °C the pack melts by <see cref="DegreeDayFactor"/> per degree. The snowline is the lowest altitude
/// whose pack holds more than <see cref="CoverMillimetres"/> on the date. So snow lingers below the freezing level in spring and
/// lies above it in autumn, from the temperatures alone.
///
/// <para>Precipitation is one assumed rate for every place and day (<see cref="PrecipitationMillimetresPerDay"/>, about
/// 1100 mm a year); the normals have none. Above the altitude where a year's snow outlasts its melt the pack grows without
/// bound — permanent snow — which the recipe's glaciers own (§4.5); the snowline is only where seasonal snow begins.</para>
///
/// <para>Pure and deterministic: the run starts <see cref="SpinUpYears"/> years before the date with no snow, which is enough for
/// a pack that melts out each summer to forget its start; one that never does stays snow either way.</para>
/// </summary>
public static class SnowCover
{
    /// <summary>The precipitation assumed every day, mm of water.</summary>
    public const double PrecipitationMillimetresPerDay = 3.0;

    /// <summary>The daily mean temperature at or below which precipitation falls as snow, °C.</summary>
    public const double SnowfallCelsius = 1.0;

    /// <summary>Melt per degree of daily mean above 0 °C, mm of water per °C per day (a typical value for snow).</summary>
    public const double DegreeDayFactor = 4.0;

    /// <summary>The pack, mm of water, above which the ground counts as snow-covered (a few centimetres of snow).</summary>
    public const double CoverMillimetres = 10.0;

    /// <summary>How many years before the date the pack starts, empty.</summary>
    public const int SpinUpYears = 3;

    /// <summary>The snow on the ground at <paramref name="altitudeMetres"/> on <paramref name="date"/>, mm of water.</summary>
    public static double WaterEquivalent(ClimateNormals climate, DateOnly date, double altitudeMetres)
    {
        ArgumentNullException.ThrowIfNull(climate);
        return Run(StationMeans(climate, date), altitudeMetres - climate.StationAltitudeMetres);
    }

    /// <summary>
    /// The lowest altitude above sea level with snow cover on <paramref name="date"/>, in [0, <paramref name="maxMetres"/>]:
    /// 0 when snow lies down to sea level, <paramref name="maxMetres"/> when there is none below it. The pack only grows with
    /// height (colder days snow more and melt less), so a bisection finds it, to a centimetre.
    /// </summary>
    public static double Snowline(ClimateNormals climate, DateOnly date, double maxMetres)
    {
        ArgumentNullException.ThrowIfNull(climate);
        double[] means = StationMeans(climate, date);
        bool Covered(double z) => Run(means, z - climate.StationAltitudeMetres) > CoverMillimetres;
        if (Covered(0.0))
            return 0.0;
        if (!Covered(maxMetres))
            return maxMetres;
        double low = 0.0, high = maxMetres;
        while (high - low > 0.01)
        {
            double middle = 0.5 * (low + high);
            if (Covered(middle))
                high = middle;
            else
                low = middle;
        }
        return high;
    }

    // The station's normal daily mean on each day of the run, oldest first, the date last.
    private static double[] StationMeans(ClimateNormals climate, DateOnly date)
    {
        DateOnly first = date.AddYears(-SpinUpYears);
        var means = new double[date.DayNumber - first.DayNumber + 1];
        for (int i = 0; i < means.Length; i++)
            means[i] = climate.On(first.AddDays(i)).MeanCelsius;
        return means;
    }

    private static double Run(double[] stationMeans, double metresAboveStation)
    {
        double cooling = Lapse.RateCelsiusPerMetre * metresAboveStation;
        double pack = 0.0;
        foreach (double stationMean in stationMeans)
        {
            double t = stationMean - cooling;
            if (t <= SnowfallCelsius)
                pack += PrecipitationMillimetresPerDay;
            if (t > 0.0)
                pack = Math.Max(0.0, pack - DegreeDayFactor * t);
        }
        return pack;
    }
}
