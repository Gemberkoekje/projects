namespace Vista.Weather;

/// <summary>
/// The season's snowline from the climate (vista-plan §3.6: "snow cover is history, temperature is now"): a degree-day snowpack
/// run over the climate normals' year, at every altitude, up to the date. Each day's mean temperature at an altitude is the
/// station's normal mean less the lapse rate; on days at or below <see cref="SnowfallCelsius"/> the day's precipitation falls as
/// snow, and on days above 0 °C the pack melts by <see cref="DegreeDayFactor"/> per degree. The snowline is the lowest altitude
/// whose pack holds more than <see cref="CoverMillimetres"/> on the date. So snow lingers below the freezing level in spring and
/// lies above it in autumn, from the temperatures alone.
///
/// <para><b>Precipitation</b> (vista-plan §10 M1.13): a place's own monthly normals (<see cref="ClimateNormals.Precipitation"/>), each
/// day the month's daily rate interpolated between the months' middles, so a maritime place's deep snow (Fiordland, Lofoten) lies lower
/// and a dry one's (Yellowstone) higher than one rate for all would put them. It grows with height by
/// <see cref="PrecipitationGradientPerMetre"/> above the precipitation's station (shrinks below it), up to
/// <see cref="MaxPrecipitationFactor"/> times the station's (mountains' precipitation levels off near their tops), and snowfall counts
/// <see cref="SnowCatchFactor"/> times what the gauges caught (they miss snow). Normals without precipitation (a custom latitude)
/// assume <see cref="PrecipitationMillimetresPerDay"/> every day and at every height, as M1.11 did for every place.</para>
///
/// <para>Above the altitude where a year's snow outlasts its melt the pack grows without bound — permanent snow — which the recipe's
/// glaciers own (§4.5); the snowline is only where seasonal snow begins.</para>
///
/// <para>Pure and deterministic: the run starts <see cref="SpinUpYears"/> years before the date with no snow, which is enough for
/// a pack that melts out each summer to forget its start; one that never does stays snow either way.</para>
/// </summary>
public static class SnowCover
{
    /// <summary>The precipitation assumed every day where the normals have none, mm of water (about 1100 mm a year).</summary>
    public const double PrecipitationMillimetresPerDay = 3.0;

    /// <summary>
    /// How much more precipitation falls per metre above the precipitation's station, a share of the station's: 2.5% per 100 m, the
    /// upper end of the 1–2.5% per 100 m a global glacier model takes from snow accumulation measurements (Huss and Hock 2015).
    /// </summary>
    public const double PrecipitationGradientPerMetre = 0.00025;

    /// <summary>The most precipitation the gradient gives, in times the station's.</summary>
    public const double MaxPrecipitationFactor = 2.0;

    /// <summary>
    /// The snowfall per millimetre a gauge catches: gauges miss 15–30% of the precipitation above 1500 m, most of it snow (Sevruk 1985,
    /// in the Hydrological Atlas of Switzerland, plate 2.6), so 1.2.
    /// </summary>
    public const double SnowCatchFactor = 1.2;

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
        return Run(StationDays(climate, date), climate, altitudeMetres);
    }

    /// <summary>
    /// The lowest altitude above sea level with snow cover on <paramref name="date"/>, in [0, <paramref name="maxMetres"/>]:
    /// 0 when snow lies down to sea level, <paramref name="maxMetres"/> when there is none below it. The pack only grows with
    /// height (colder days snow more and melt less, and more falls), so a bisection finds it, to a centimetre.
    /// </summary>
    public static double Snowline(ClimateNormals climate, DateOnly date, double maxMetres)
    {
        ArgumentNullException.ThrowIfNull(climate);
        (double Mean, double Precipitation)[] days = StationDays(climate, date);
        bool Covered(double z) => Run(days, climate, z) > CoverMillimetres;
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

    /// <summary>
    /// The precipitation at <paramref name="altitudeMetres"/>, in times the precipitation's station's: 1 for normals without
    /// precipitation of their own (one rate everywhere).
    /// </summary>
    public static double PrecipitationFactor(ClimateNormals climate, double altitudeMetres)
    {
        ArgumentNullException.ThrowIfNull(climate);
        if (climate.Precipitation is null)
            return 1.0;
        double station = climate.PrecipitationAltitudeMetres ?? climate.StationAltitudeMetres;
        return Math.Clamp(1.0 + PrecipitationGradientPerMetre * (altitudeMetres - station), 0.0, MaxPrecipitationFactor);
    }

    // The station's normal daily mean and precipitation on each day of the run, oldest first, the date last.
    private static (double Mean, double Precipitation)[] StationDays(ClimateNormals climate, DateOnly date)
    {
        DateOnly first = date.AddYears(-SpinUpYears);
        var days = new (double, double)[date.DayNumber - first.DayNumber + 1];
        for (int i = 0; i < days.Length; i++)
        {
            DateOnly day = first.AddDays(i);
            days[i] = (climate.On(day).MeanCelsius, climate.PrecipitationOn(day) ?? PrecipitationMillimetresPerDay);
        }
        return days;
    }

    private static double Run((double Mean, double Precipitation)[] days, ClimateNormals climate, double altitudeMetres)
    {
        double cooling = Lapse.RateCelsiusPerMetre * (altitudeMetres - climate.StationAltitudeMetres);
        double snowfall = climate.Precipitation is null ? 1.0 : PrecipitationFactor(climate, altitudeMetres) * SnowCatchFactor;
        double pack = 0.0;
        foreach ((double stationMean, double precipitation) in days)
        {
            double t = stationMean - cooling;
            if (t <= SnowfallCelsius)
                pack += precipitation * snowfall;
            if (t > 0.0)
                pack = Math.Max(0.0, pack - DegreeDayFactor * t);
        }
        return pack;
    }
}
