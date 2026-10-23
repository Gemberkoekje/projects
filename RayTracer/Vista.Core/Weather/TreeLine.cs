namespace Vista.Weather;

/// <summary>
/// The altitude above which no forest grows (vista-plan §10 M1.12b), from the place's climate: where the warmest month's normal
/// mean temperature, lowered along the lapse rate, falls to <see cref="WarmestMonthCelsius"/>. The climatic tree line lies where
/// the growing season averages about 6.4 °C (Körner and Paulsen 2004), which in the Alps is a warmest month of about 10 °C; that
/// puts Zermatt's at about 2280 m, where its larch and stone pine thin out. It lies some 600 m below the grass line
/// (<see cref="GrassLine"/>), as closed alpine grassland reaches above the trees. The rules lower it on poleward faces and raise it
/// on equator-facing ones (<see cref="World.MaterialRules"/>).
/// </summary>
public static class TreeLine
{
    /// <summary>The warmest month's mean daily mean temperature at the tree line, °C.</summary>
    public const double WarmestMonthCelsius = 10.0;

    /// <summary>The tree line of <paramref name="climate"/> above sea level, m.</summary>
    public static double AltitudeMetres(ClimateNormals climate)
    {
        ArgumentNullException.ThrowIfNull(climate);
        double warmest = Enumerable.Range(0, 12).Max(climate.MonthlyMean);
        return climate.StationAltitudeMetres + (warmest - WarmestMonthCelsius) / Lapse.RateCelsiusPerMetre;
    }
}
