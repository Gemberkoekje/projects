using Vista.Astronomy;
using Vista.Atmosphere;

namespace Vista.Weather;

/// <summary>The valley haze a render gets (vista-plan §10 M1.13a): its lid, its haze, and the pool it came from.</summary>
/// <param name="TopMetres">The lid's altitude above sea level, m (the middle of its fall).</param>
/// <param name="DepthMetres">The lid's height above the valley floor, m.</param>
/// <param name="Extinction550">The extinction the layer adds under its lid at 550 nm, per metre; 0 for none.</param>
/// <param name="VisibilityKm">The visibility under the lid, the valley's haze and the layer's together, km.</param>
/// <param name="PoolShare">What is left of the night's pool, 0 to 1 (1 when the valley haze is pinned).</param>
/// <param name="DawnDepthMetres">The whole pool's depth above the valley floor at dawn, m.</param>
/// <param name="CoolingCelsius">The night's cooling at the valley floor below the free air, °C.</param>
public sealed record ValleyHazeLayer(double TopMetres, double DepthMetres, double Extinction550, double VisibilityKm, double PoolShare,
    double DawnDepthMetres, double CoolingCelsius)
{
    /// <summary>No valley haze.</summary>
    public static ValleyHazeLayer None { get; } = new(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

    /// <summary>True when the layer adds any haze.</summary>
    public bool Any => Extinction550 > 0.0;
}

/// <summary>
/// The valley haze from the weather (vista-plan §3.6, §10 M1.13a): on clear, still nights the ground cools the air in contact
/// with it, and the cold air drains into the valleys and pools there under an inversion, a lid of warmer air that holds the
/// valley's haze below it. By dawn the pool is deepest; the morning sun heats the ground, the ground the air, and the pool breaks
/// up from the heat it gets; in the evening, as the air cools again, it forms anew. The chain, from the climate's day:
/// <list type="number">
///   <item><b>The night's cooling</b> at the valley floor below the free air: <see cref="CoolingShareOfRange"/> of the day's normal
///   range, which is itself the climate's measure of clear, still nights (a cloudy or windy coast has a small range: Lofoten's is
///   3–5 °C, Zermatt's 8–12 °C, Yosemite's 10–18 °C). At Zermatt the dawn pool is about 480 m deep in April, at Lofoten under
///   200 m, at Yosemite in July 800 m: valley inversions are a few hundred metres to a kilometre deep (Whiteman 2000).</item>
///   <item><b>The pool's depth</b>: the cooling is undone at its top, where the inversion's own rise
///   (<see cref="InversionLapseCelsiusPerMetre"/>) and the free air's fall (<see cref="Lapse.RateCelsiusPerMetre"/>) have closed it;
///   at most <see cref="ReliefShare"/> of the world's relief, because a pool fills its valleys, not the peaks above them.</item>
///   <item><b>How much of it is left</b> (<see cref="PoolShare"/>): between sunrise and the day's high, the share of the pool's heat
///   deficit the sun has not yet made up (Whiteman and McKee 1982's thermodynamic model of break-up, simplified): the sunlight
///   since sunrise, clear-sky (<see cref="ClearSkyIrradiance"/>), times the ground's absorption (snow on the valley floor reflects
///   most of it, so a snowy valley keeps its pool all day), the share that heats the air (<see cref="SensibleShare"/>) and the
///   valley's amplification (<see cref="ValleyAmplification"/>). After the day's high, the share of the night's cooling the air has
///   done since, on the daily curve (<see cref="DailyTemperature"/>), or what was left of the morning's pool if more.</item>
///   <item><b>The haze under the lid</b>: the valley's own haze (the fog/haze slider's, at the valley floor), held under the lid as it
///   accumulates through the night (up to <see cref="Trapping"/> times as much by dawn, Trapping − 1 of it the night's) and grown by
///   the hour's humidity against the day's (<see cref="Haze.OpticalDepth"/>'s growth law); the layer is what that adds to the haze
///   the valley already has.</item>
/// </list>
/// The lid stands at the valley floor plus the pool's depth times what is left of it: a pool sinks as it breaks up (the top of a
/// valley inversion descends through the morning; Whiteman 1982) and deepens as it forms. A pinned visibility under the lid sets
/// the layer's haze instead and keeps the night's whole pool, so a pinned valley haze stands at any hour.
///
/// <para>Pure: the same inputs give the same layer, bit for bit.</para>
/// </summary>
public static class ValleyHaze
{
    /// <summary>
    /// The share of the day's normal range the valley floor cools below the free air by dawn. Vista's skies are clear (clouds are M4's),
    /// so the night before was clear too, and a clear day's range runs about half as much again as the normals' average of clear and
    /// cloudy days; about half of that is the valley's own cooling below the free air at night, the rest the day's heating.
    /// </summary>
    public const double CoolingShareOfRange = 0.75;

    /// <summary>
    /// How fast the air warms upward inside the pool, °C per metre: 10 °C per kilometre, a valley inversion's typical rise (Whiteman
    /// 2000, <i>Mountain Meteorology</i>, ch. 9).
    /// </summary>
    public const double InversionLapseCelsiusPerMetre = 0.010;

    /// <summary>The most of the world's relief (its peaks above its valleys) a pool fills.</summary>
    public const double ReliefShare = 0.5;

    /// <summary>How many times its daytime haze the pool holds by dawn: the night's own haze kept under the lid.</summary>
    public const double Trapping = 2.0;

    /// <summary>The share of the sunlight the ground absorbs that goes into the air as sensible heat.</summary>
    public const double SensibleShare = 0.3;

    /// <summary>
    /// How much faster a valley's air warms than air over a plain from the same sunlight: its sunlit slopes heat a smaller volume of
    /// air (Whiteman 1990's topographic amplification factor, about 2 for a V-shaped valley).
    /// </summary>
    public const double ValleyAmplification = 2.0;

    /// <summary>The albedo of the snow-free valley floor (grass, forest, soil).</summary>
    public const double GroundAlbedo = 0.2;

    /// <summary>The albedo of a snow-covered valley floor.</summary>
    public const double SnowAlbedo = 0.8;

    /// <summary>The band, m, over which the valley floor goes from snow-free to snow-covered around the snowline.</summary>
    public const double SnowBandMetres = 200.0;

    /// <summary>The specific heat of air at constant pressure, J/(kg·K).</summary>
    public const double AirHeatCapacity = 1005.0;

    /// <summary>The step of the sunlight's integral since sunrise, h.</summary>
    public const double SunStepHours = 1.0 / 12.0;

    /// <summary>
    /// Clear-sky sunlight on level ground with the sun <paramref name="elevationDegrees"/> high, W/m²: the solar constant through
    /// Meinel and Meinel's (1976) attenuation by air mass, <c>1361·sin e·0.7^(m^0.678)</c> with <c>m = 1/sin e</c>; 0 with the sun down.
    /// </summary>
    public static double ClearSkyIrradiance(double elevationDegrees)
    {
        double sine = Math.Sin(elevationDegrees * Math.PI / 180.0);
        if (!(sine > 0.0))
            return 0.0;
        return 1361.0 * sine * Math.Pow(0.7, Math.Pow(1.0 / sine, 0.678));
    }

    /// <summary>
    /// The clear-sky sunlight on level ground at <paramref name="latitudeDegrees"/> on <paramref name="date"/> between
    /// <paramref name="fromHours"/> and <paramref name="toHours"/> local solar time, J/m² (midpoint steps of at most <see cref="SunStepHours"/>).
    /// </summary>
    public static double Sunlight(double latitudeDegrees, DateOnly date, double fromHours, double toHours)
    {
        if (!(toHours > fromHours))
            return 0.0;
        int steps = Math.Max(1, (int)Math.Ceiling((toHours - fromHours) / SunStepHours));
        double dt = (toHours - fromHours) / steps;
        double sum = 0.0;
        for (int i = 0; i < steps; i++)
            sum += ClearSkyIrradiance(SolarPosition.At(latitudeDegrees, date, fromHours + (i + 0.5) * dt).TrueElevationDegrees);
        return sum * dt * 3600.0;
    }

    /// <summary>The night's cooling at the valley floor below the free air by dawn, °C, on a day with <paramref name="day"/>'s normals.</summary>
    public static double Cooling(DayNormals day) => CoolingShareOfRange * Math.Max(day.HighCelsius - day.LowCelsius, 0.0);

    /// <summary>
    /// The dawn pool's depth above the valley floor, m: where <paramref name="coolingCelsius"/> is undone, at most
    /// <see cref="ReliefShare"/> of <paramref name="reliefMetres"/> (none given: no limit).
    /// </summary>
    public static double Depth(double coolingCelsius, double? reliefMetres)
    {
        double depth = Math.Max(coolingCelsius, 0.0) / (InversionLapseCelsiusPerMetre + Lapse.RateCelsiusPerMetre);
        return reliefMetres is double relief ? Math.Min(depth, ReliefShare * Math.Max(relief, 0.0)) : depth;
    }

    /// <summary>
    /// The pool's heat deficit, J/m²: the heat that warms it to the free air's temperature, <c>ρ·c_p·ΔT·D/2</c> (its cooling falls
    /// linearly from <paramref name="coolingCelsius"/> at the floor to none at its top <paramref name="depthMetres"/> up), with the
    /// air's density at <paramref name="floorMetres"/> above sea level.
    /// </summary>
    public static double HeatDeficit(double coolingCelsius, double depthMetres, double floorMetres)
    {
        double density = AirOptics.StandardPressurePa * AirOptics.MolarMassAir / (8.314462618 * AirOptics.StandardTemperatureK)
            * Math.Exp(-floorMetres / AirOptics.RayleighScaleHeightMetres);
        return density * AirHeatCapacity * Math.Max(coolingCelsius, 0.0) * Math.Max(depthMetres, 0.0) / 2.0;
    }

    /// <summary>The share of the sunlight a valley floor absorbs: snow-free, snow-covered, or in between across <see cref="SnowBandMetres"/> about the snowline.</summary>
    public static double Absorbed(double floorMetres, double snowlineMetres)
    {
        double snow = Math.Clamp((floorMetres - snowlineMetres) / SnowBandMetres + 0.5, 0.0, 1.0);
        return 1.0 - (GroundAlbedo + snow * (SnowAlbedo - GroundAlbedo));
    }

    /// <summary>
    /// What is left of the pool at <paramref name="solarHours"/>, 0 to 1, and how that was found: before the day's high, the share of
    /// <paramref name="heatDeficit"/> (J/m²) the heat since the day's low at <paramref name="minimumHours"/> has not made up; after it,
    /// the share of the night's cooling the daily curve has done since the high, or the morning's remnant at the high if more.
    /// </summary>
    public static (double Share, bool Morning, double HeatJoules) PoolShare(double latitudeDegrees, DateOnly date, double solarHours, double minimumHours,
        double heatDeficit, double absorbed)
    {
        double Remaining(double toHours, out double heat)
        {
            heat = ValleyAmplification * SensibleShare * absorbed * Sunlight(latitudeDegrees, date, minimumHours, toHours);
            return heatDeficit > 0.0 ? Math.Clamp(1.0 - heat / heatDeficit, 0.0, 1.0) : 0.0;
        }

        double t = solarHours < minimumHours ? solarHours + 24.0 : solarHours;
        if (t <= DailyTemperature.MaximumHours)
        {
            double share = Remaining(t, out double heat);
            return (share, true, heat);
        }
        double remnant = Remaining(DailyTemperature.MaximumHours, out double heatByHigh);
        double cooled = 1.0 - DailyTemperature.At(new DayNormals(0.0, 1.0), solarHours, minimumHours);
        return (Math.Max(remnant, cooled), false, heatByHigh);
    }

    /// <summary>
    /// The extinction the layer adds under its lid at 550 nm, per metre: the valley's haze extinction <paramref name="hazeExtinction"/>
    /// (per metre, at the valley floor) held under the lid (<see cref="Trapping"/>, by <paramref name="share"/> of the night) and grown
    /// by the hour's humidity against the day's, less what the valley already has.
    /// </summary>
    public static double AddedExtinction(double hazeExtinction, double share, double humidityPercent, double dayHumidityPercent)
    {
        double growth = Haze.OpticalDepth(humidityPercent) / Haze.OpticalDepth(dayHumidityPercent);
        double held = 1.0 + (Trapping - 1.0) * Math.Clamp(share, 0.0, 1.0);
        return Math.Max(0.0, hazeExtinction * (held * growth - 1.0));
    }
}
