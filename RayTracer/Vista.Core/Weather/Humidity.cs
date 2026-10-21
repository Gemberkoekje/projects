namespace Vista.Weather;

/// <summary>
/// Relative humidity from the temperature and the dew point (vista-plan §3.6). Without humidity normals, the dew point is taken
/// to be the day's low: air cools overnight to about its dew point, the rule FAO Irrigation and Drainage Paper 56 (Allen et al.
/// 1998) uses where humidity was not measured. So the auto humidity is near 100% around sunrise and lowest in mid-afternoon.
/// Saturation vapour pressure is the Magnus form over water with Alduchov &amp; Eskridge's (1996) coefficients.
/// </summary>
public static class Humidity
{
    /// <summary>Saturation vapour pressure over water at <paramref name="celsius"/>, hPa.</summary>
    public static double SaturationVapourPressureHpa(double celsius) => 6.1094 * Math.Exp(17.625 * celsius / (celsius + 243.04));

    /// <summary>
    /// The relative humidity, %, of air at <paramref name="celsius"/> with dew point <paramref name="dewPointCelsius"/>. A dew point
    /// above the temperature saturates the air (100%): the moisture condenses out.
    /// </summary>
    public static double RelativePercent(double celsius, double dewPointCelsius) =>
        100.0 * SaturationVapourPressureHpa(Math.Min(dewPointCelsius, celsius)) / SaturationVapourPressureHpa(celsius);
}
