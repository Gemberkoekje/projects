namespace Vista.Weather;

/// <summary>
/// The altitude above which meadow gives way to bare ground (vista-plan §10 M1.12), from the place's climate: where the
/// warmest month's normal mean temperature, lowered along the lapse rate, falls to <see cref="WarmestMonthCelsius"/>. Trees end
/// where the growing season averages about 6.4 °C (Körner and Paulsen 2004), at a warmest month of about 10 °C in the Alps; closed
/// alpine grassland reaches some 500–700 m higher. 6 °C puts Zermatt's line at about 2890 m, where its meadows thin out into scree
/// on the sunny slopes. The rules lower it on poleward faces and raise it on equator-facing ones (<see cref="World.MaterialRules"/>).
/// </summary>
public static class GrassLine
{
    /// <summary>The warmest month's mean daily mean temperature at the grass line, °C.</summary>
    public const double WarmestMonthCelsius = 6.0;

    /// <summary>The grass line of <paramref name="climate"/> above sea level, m.</summary>
    public static double AltitudeMetres(ClimateNormals climate)
    {
        ArgumentNullException.ThrowIfNull(climate);
        double warmest = Enumerable.Range(0, 12).Max(climate.MonthlyMean);
        return climate.StationAltitudeMetres + (warmest - WarmestMonthCelsius) / Lapse.RateCelsiusPerMetre;
    }
}
