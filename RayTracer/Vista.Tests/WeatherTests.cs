using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Weather;

namespace Vista.Tests;

/// <summary>
/// The weather chain's pieces (vista-plan §3.6, §10 M1.11): the climate normals and their daily interpolation, the daily curve,
/// humidity from the dew point, the degree-day snowline, haze from humidity and visibility, and the latitude model.
/// </summary>
[TestClass]
public sealed class WeatherTests
{
    private static readonly ClimateNormals Flat = new("flat", 1000.0, "test", "test",
        [10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10], [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

    private static readonly ClimateNormals Ramp = new("ramp", 0.0, "test", "test",
        [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);

    [TestMethod]
    public void EveryDropdownPlace_HasValidNormals_WithASource()
    {
        CollectionAssert.AreEquivalent(
            Places.All.Select(p => p.Place).ToArray(),
            ClimateTable.All.Select(e => e.Place).ToArray(),
            "every dropdown place has normals, once");
        foreach ((Place place, ClimateNormals normals) in ClimateTable.All)
        {
            CollectionAssert.AreEqual(Array.Empty<string>(), normals.Validate().ToArray(), place.ToString());
            Assert.IsTrue(normals.StationAltitudeMetres is >= 0.0 and < 3000.0, $"{place}: station altitude");
            StringAssert.Contains(normals.Period, "–", $"{place}: a period, e.g. 1991–2020");
        }
    }

    [TestMethod]
    public void TheWarmMonths_AreJulyInTheNorth_AndJanuaryInTheSouth()
    {
        foreach ((Place place, ClimateNormals normals) in ClimateTable.All)
        {
            double january = normals.MonthlyMean(0), july = normals.MonthlyMean(6);
            if (Places.Get(place).Southern)
                Assert.IsTrue(january > july, $"{place}: southern summer is January");
            else
                Assert.IsTrue(july > january, $"{place}: northern summer is July");
        }
    }

    [TestMethod]
    public void DailyNormals_AreTheMonthsAtMidMonth_AndRunOnAcrossTheYear()
    {
        // 15 July's middle is 15.5 days into a 31-day month: July's value exactly.
        DayNormals july = Ramp.On(new DateOnly(2026, 7, 16));
        Assert.AreEqual(6.0, july.LowCelsius, 1e-9);
        Assert.AreEqual(7.0, july.HighCelsius, 1e-9);
        // Between mid-December (11) and mid-January (0) the values fall across the new year without a jump.
        double dec31 = Ramp.On(new DateOnly(2026, 12, 31)).LowCelsius;
        double jan1 = Ramp.On(new DateOnly(2027, 1, 1)).LowCelsius;
        Assert.IsTrue(dec31 is < 11.0 and > 0.0 && jan1 is < 11.0 and > 0.0, $"{dec31}, {jan1}");
        Assert.IsTrue(Math.Abs(dec31 - jan1) < 0.5, "one day moves little");
        Assert.AreEqual(5.0, Flat.On(new DateOnly(2026, 2, 28)).MeanCelsius, 1e-12);
        Assert.AreEqual(5.0 - 6.5, Flat.On(new DateOnly(2026, 2, 28)).Raised(1000.0).MeanCelsius, 1e-12, "6.5 °C per 1000 m");
    }

    [TestMethod]
    public void DailyCurve_IsLowAtSunrise_HighAt14_AndContinuous()
    {
        var day = new DayNormals(2.0, 16.0);
        Assert.AreEqual(2.0, DailyTemperature.At(day, 6.0, 6.0), 1e-12);
        Assert.AreEqual(16.0, DailyTemperature.At(day, 14.0, 6.0), 1e-12);
        Assert.AreEqual(DailyTemperature.At(day, 24.0, 6.0), DailyTemperature.At(day, 0.0, 6.0), 1e-12, "midnight joins up");
        double previous = double.NegativeInfinity;
        for (double t = 6.0; t <= 14.0; t += 0.25)
        {
            double value = DailyTemperature.At(day, t, 6.0);
            Assert.IsTrue(value >= previous, $"rising at {t}");
            previous = value;
        }
        for (double t = 14.25; t < 30.0; t += 0.25)
        {
            double value = DailyTemperature.At(day, t % 24.0, 6.0);
            Assert.IsTrue(value <= previous + 1e-12, $"falling at {t}");
            previous = value;
        }
        Assert.AreEqual(0.0, DailyTemperature.MinimumHours(new Daylight(DaylightKind.PolarDay, double.NaN, double.NaN)));
    }

    [TestMethod]
    public void Humidity_IsSaturatedAtTheDewPoint_AndFallsAsTheAirWarms()
    {
        Assert.AreEqual(6.1094, Humidity.SaturationVapourPressureHpa(0.0), 1e-12);
        Assert.AreEqual(23.37, Humidity.SaturationVapourPressureHpa(20.0), 0.05, "the tables' 23.39 hPa");
        Assert.AreEqual(100.0, Humidity.RelativePercent(10.0, 10.0), 1e-9);
        Assert.AreEqual(100.0, Humidity.RelativePercent(5.0, 10.0), 1e-9, "a dew point above the air saturates it");
        Assert.AreEqual(52.5, Humidity.RelativePercent(20.0, 10.0), 0.5);
        Assert.IsTrue(Humidity.RelativePercent(25.0, 10.0) < Humidity.RelativePercent(20.0, 10.0));
    }

    [TestMethod]
    public void SnowPack_OnlyGrowsWithHeight()
    {
        ClimateNormals zermatt = ClimateTable.For(Place.Zermatt);
        foreach (DateOnly date in new[] { new DateOnly(2026, 1, 15), new DateOnly(2026, 4, 15), new DateOnly(2026, 7, 15), new DateOnly(2026, 10, 15) })
        {
            double previous = -1.0;
            for (double z = 0.0; z <= 5000.0; z += 100.0)
            {
                double pack = SnowCover.WaterEquivalent(zermatt, date, z);
                Assert.IsTrue(pack >= previous, $"{date} at {z} m");
                previous = pack;
            }
        }
    }

    [TestMethod]
    public void Snowline_IsLowInWinter_HighInSummer_AndWhereThePackBegins()
    {
        ClimateNormals zermatt = ClimateTable.For(Place.Zermatt);
        double winter = SnowCover.Snowline(zermatt, new DateOnly(2026, 1, 15), 12000.0);
        double summer = SnowCover.Snowline(zermatt, new DateOnly(2026, 7, 15), 12000.0);
        Assert.IsTrue(winter < 1608.0, $"winter snow reaches Zermatt's valley floor, got {winter:0} m");
        Assert.IsTrue(summer is > 2500.0 and < 4000.0, $"summer's seasonal snow lies high, got {summer:0} m");
        Assert.IsTrue(SnowCover.WaterEquivalent(zermatt, new DateOnly(2026, 7, 15), summer + 1.0) > SnowCover.CoverMillimetres);
        Assert.IsTrue(SnowCover.WaterEquivalent(zermatt, new DateOnly(2026, 7, 15), summer - 1.0) <= SnowCover.CoverMillimetres);
        Assert.AreEqual(2000.0, SnowCover.Snowline(new ClimateNormals("warm", 0.0, "t", "t",
            [.. Enumerable.Repeat(30.0, 12)], [.. Enumerable.Repeat(20.0, 12)]), new DateOnly(2026, 1, 15), 2000.0), "none below the top");
        Assert.AreEqual(0.0, SnowCover.Snowline(new ClimateNormals("cold", 0.0, "t", "t",
            [.. Enumerable.Repeat(-10.0, 12)], [.. Enumerable.Repeat(-20.0, 12)]), new DateOnly(2026, 1, 15), 2000.0), "snow to sea level");
    }

    [TestMethod]
    public void SnowIsHistory_SpringKeepsSnowLowerThanAutumn_AtTheSameTemperature()
    {
        // The day in autumn whose normal mean is nearest a spring day's: same temperature now, a different winter behind it.
        ClimateNormals zermatt = ClimateTable.For(Place.Zermatt);
        var spring = new DateOnly(2026, 4, 25);
        double mean = zermatt.On(spring).MeanCelsius;
        DateOnly autumn = Enumerable.Range(0, 120).Select(d => new DateOnly(2026, 9, 1).AddDays(d))
            .MinBy(d => Math.Abs(zermatt.On(d).MeanCelsius - mean));
        Assert.AreEqual(mean, zermatt.On(autumn).MeanCelsius, 0.2, autumn.ToString());
        double springLine = SnowCover.Snowline(zermatt, spring, 12000.0);
        double autumnLine = SnowCover.Snowline(zermatt, autumn, 12000.0);
        Assert.IsTrue(springLine + 300.0 < autumnLine, $"spring {springLine:0} m against autumn ({autumn}) {autumnLine:0} m");
    }

    [TestMethod]
    public void Haze_GrowsWithHumidity_FromTheClearDayAtFiftyPercent()
    {
        Assert.AreEqual(Haze.ReferenceOpticalDepth, Haze.OpticalDepth(Haze.ReferenceHumidityPercent), 1e-15);
        double previous = 0.0;
        for (double rh = 0.0; rh <= 100.0; rh += 5.0)
        {
            double tau = Haze.OpticalDepth(rh);
            Assert.IsTrue(tau >= previous, $"{rh}%");
            previous = tau;
        }
        Assert.AreEqual(Haze.OpticalDepth(Haze.GrowthLimitPercent), Haze.OpticalDepth(100.0), "growth stops short of fog");
        Assert.AreEqual(0.1 * Math.Pow(0.1, -Haze.GrowthExponent), Haze.OpticalDepth(95.0), 1e-12);
    }

    [TestMethod]
    public void Visibility_RoundTripsThroughTheHaze_AndCleanAirLimitsIt()
    {
        AtmosphereModel model = AtmosphereModel.Default;
        foreach (double altitude in new[] { 0.0, 1608.0, 3000.0 })
        {
            foreach (double tau in new[] { 0.02, 0.1, 0.4 })
            {
                double km = Haze.VisibilityKm(model with { AerosolOpticalDepth550 = tau }, altitude);
                Assert.AreEqual(tau, Haze.OpticalDepthForVisibility(model, km, altitude), 1e-9, $"{tau} at {altitude} m");
            }
        }
        double clean = Haze.CleanAirVisibilityKm(model, 0.0);
        Assert.IsTrue(clean is > 240.0 and < 280.0, $"ln 20 over the sea-level Rayleigh extinction, got {clean:0} km");
        Assert.AreEqual(0.0, Haze.OpticalDepthForVisibility(model, clean + 50.0, 0.0), "clearer than clean air is clean air");
        double sea = Haze.VisibilityKm(model, 0.0), high = Haze.VisibilityKm(model, 3000.0);
        Assert.IsTrue(high > 2.0 * sea, "thinner air sees farther");
    }

    [TestMethod]
    public void LatitudeModel_IsWarmAtTheEquator_SwingsWithTheSeasons_AndLofotenIsMilder()
    {
        ClimateNormals equator = LatitudeClimate.For(0.0);
        Assert.AreEqual(LatitudeClimate.EquatorMeanCelsius, equator.MonthlyMean(0), 1e-9, "no seasons at the equator");
        CollectionAssert.AreEqual(Array.Empty<string>(), LatitudeClimate.For(67.93).Validate().ToArray());
        Assert.IsTrue(LatitudeClimate.For(46.0).MonthlyMean(6) > LatitudeClimate.For(46.0).MonthlyMean(0) + 20.0, "northern summer");
        Assert.IsTrue(LatitudeClimate.For(-46.0).MonthlyMean(0) > LatitudeClimate.For(-46.0).MonthlyMean(6), "southern summer is January");

        ClimateNormals lofoten = ClimateTable.For(Place.Lofoten);
        ClimateNormals model = LatitudeClimate.For(Places.Get(Place.Lofoten).LatitudeDegrees);
        double Annual(ClimateNormals n) => Enumerable.Range(0, 12).Average(n.MonthlyMean);
        Assert.IsTrue(Annual(lofoten) > Annual(model) + 5.0, $"Lofoten {Annual(lofoten):0.0} °C against its latitude's {Annual(model):0.0} °C");
        Assert.IsTrue(lofoten.MonthlyMean(0) > model.MonthlyMean(0) + 10.0, "a Gulf Stream January");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LatitudeClimate.For(91.0));
    }

    [TestMethod]
    public void FreezingLevel_FollowsTheLapseRate()
    {
        Assert.AreEqual(2000.0, Lapse.FreezingLevel(1000.0, 6.5), 1e-9);
        Assert.AreEqual(0.0, Lapse.FreezingLevel(1000.0, -6.5), 1e-9);
    }
}
