namespace Vista.Weather;

/// <summary>
/// The rough climate of a custom latitude (vista-plan §3.6), which has no station: sea-level normals from the latitude alone,
/// after North, Cahalan &amp; Coakley's (1981, Rev. Geophys. 19:91, table 1) fit of zonal-mean sea-level temperature: an annual
/// mean of 28.9 − 42 sin²φ °C, and an annual swing that grows with sin φ — 15.5 sin φ °C north of the equator (about 40% land),
/// about half that south of it (about 20% land). The coldest month is about a month after the winter solstice; the daily range
/// is one number. A latitude says nothing about whether the place is on a coast or deep in a continent, so this fits neither
/// well: Lofoten, warmed by the Gulf Stream, is far milder than its latitude here, and that is what the dropdown's real normals are
/// for. Its polar annual mean (−13 °C) is too warm for the real poles, which the fit's two terms cannot follow.
/// </summary>
public static class LatitudeClimate
{
    /// <summary>The fit's annual mean at the equator, °C.</summary>
    public const double EquatorMeanCelsius = 28.9;

    /// <summary>How much colder the annual mean is at a pole than at the equator, °C (it falls with sin²φ).</summary>
    public const double PoleDropCelsius = 42.0;

    /// <summary>Half the difference between the warmest and coldest month at a northern pole, °C (it grows with sin φ).</summary>
    public const double NorthernSwingCelsius = 15.5;

    /// <summary>The same at a southern pole, °C: the southern hemisphere's oceans hold its seasons back.</summary>
    public const double SouthernSwingCelsius = 8.0;

    /// <summary>The difference between the mean daily high and low, °C.</summary>
    public const double DailyRangeCelsius = 9.0;

    /// <summary>The coldest day of the year north of the equator (about 20 January); six months later south of it.</summary>
    public const double ColdestDayOfYear = 20.0;

    /// <summary>The model's normals at <paramref name="latitudeDegrees"/>, at sea level.</summary>
    public static ClimateNormals For(double latitudeDegrees)
    {
        if (!(latitudeDegrees is >= -90.0 and <= 90.0))
            throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), latitudeDegrees, "Latitude must be in [−90, 90] degrees.");
        double sin = Math.Sin(latitudeDegrees * Math.PI / 180.0);
        double annual = EquatorMeanCelsius - PoleDropCelsius * sin * sin;
        double swing = (latitudeDegrees < 0.0 ? SouthernSwingCelsius : NorthernSwingCelsius) * Math.Abs(sin);
        double coldest = latitudeDegrees < 0.0 ? ColdestDayOfYear + 365.0 / 2.0 : ColdestDayOfYear;
        var highs = new double[12];
        var lows = new double[12];
        for (int m = 0; m < 12; m++)
        {
            // The middle of month m in a common year, as a day of the year.
            double middle = new DateOnly(2026, m + 1, 1).DayOfYear - 1 + DateTime.DaysInMonth(2026, m + 1) / 2.0;
            double mean = annual - swing * Math.Cos(2.0 * Math.PI * (middle - coldest) / 365.0);
            highs[m] = mean + DailyRangeCelsius / 2.0;
            lows[m] = mean - DailyRangeCelsius / 2.0;
        }
        return new ClimateNormals("latitude model", 0.0, "model",
            "North, Cahalan & Coakley 1981 (Rev. Geophys. 19:91), zonal-mean sea-level fit; daily range 9 °C; tier 5", highs, lows);
    }
}
