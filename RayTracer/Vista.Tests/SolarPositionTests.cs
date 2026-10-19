using System.Globalization;
using System.Numerics;
using Vista.Astronomy;

namespace Vista.Tests;

/// <summary>
/// The sun's position against NOAA's solar calculator (vista-plan §10 M1.2: within 0.05°). The expected values
/// were computed on 2026-10-19 by running the calculator's own JavaScript (gml.noaa.gov/grad/solcalc/main.js:
/// <c>calcAzEl</c>, <c>calcSunriseSet</c>) at longitude 0 and time zone 0, where its universal time plus its
/// equation of time is exactly the local solar time Vista takes. NOAA's elevation includes refraction.
/// </summary>
[TestClass]
public sealed class SolarPositionTests
{
    /// <summary>The plan's tolerance for azimuth and elevation, in degrees.</summary>
    private const double Degrees = 0.05;

    /// <summary>Tolerance for sunrise and sunset: NOAA iterates twice, Vista to convergence.</summary>
    private const double Minutes = 0.25;

    [TestMethod]
    // Greenwich, March equinox.
    [DataRow(51.4769, "2026-03-20", 532.530019, 126.2910, 25.1567)]
    [DataRow(51.4769, "2026-03-20", 712.567009, 177.6268, 38.4761)]
    [DataRow(51.4769, "2026-03-20", 982.622552, 250.5259, 14.9615)]
    // Zermatt's latitude, June solstice and mid-October (the low evening sun through sunset into twilight).
    [DataRow(46.02, "2026-06-21", 418.221034, 82.9207, 26.5410)]
    [DataRow(46.02, "2026-06-21", 698.178740, 167.1055, 66.9982)]
    [DataRow(46.02, "2026-06-21", 1138.112324, 296.4482, 7.3790)]
    [DataRow(46.02, "2026-10-15", 494.207787, 121.0769, 15.8614)]
    [DataRow(46.02, "2026-10-15", 794.254672, 202.0076, 32.8928)]
    [DataRow(46.02, "2026-10-15", 1034.291885, 255.7548, 1.8698)]
    [DataRow(46.02, "2026-10-15", 1046.293739, 257.9165, 0.0809)]
    [DataRow(46.02, "2026-10-15", 1059.295747, 260.2417, -2.5726)]
    [DataRow(46.02, "2026-10-15", 1094.301148, 266.4682, -8.6972)]
    // Torres del Paine's latitude, December solstice (southern summer) and April (southern autumn).
    [DataRow(-50.94, "2026-12-21", 362.046817, 104.9083, 18.3484)]
    [DataRow(-50.94, "2026-12-21", 781.902321, 330.6836, 60.0097)]
    [DataRow(-50.94, "2026-12-21", 1201.757764, 232.2913, 1.2856)]
    [DataRow(-50.94, "2026-04-15", 599.956573, 32.6119, 23.9188)]
    [DataRow(-50.94, "2026-04-15", 839.996480, 327.4224, 23.8670)]
    [DataRow(-50.94, "2026-04-15", 1050.031262, 282.0614, -2.9482)]
    // Cape Town's latitude, September equinox.
    [DataRow(-33.9, "2026-09-23", 487.554367, 70.9736, 26.1179)]
    [DataRow(-33.9, "2026-09-23", 907.656803, 297.3343, 34.7256)]
    // Svalbard: midnight sun in June, noon below the horizon in December.
    [DataRow(78.22, "2026-06-21", 8.282998, 1.9399, 11.7409)]
    [DataRow(78.22, "2026-06-21", 358.230100, 84.5125, 22.8641)]
    [DataRow(78.22, "2026-06-21", 718.175720, 179.4878, 35.2404)]
    [DataRow(78.22, "2026-12-21", 2.170611, 0.6094, -35.2061)]
    [DataRow(78.22, "2026-12-21", 721.922968, 180.4504, -11.6297)]
    // McMurdo: midnight sun in December, polar night in June.
    [DataRow(-77.85, "2026-12-21", 2.170611, 179.4923, 11.3639)]
    [DataRow(-77.85, "2026-12-21", 721.922968, 359.4576, 35.6093)]
    [DataRow(-77.85, "2026-06-21", 718.175720, 0.4267, -11.2595)]
    // The equator at the equinox, and Lofoten's latitude at a January noon barely above the horizon.
    [DataRow(0.0, "2026-03-20", 412.505376, 90.1290, 13.1940)]
    [DataRow(0.0, "2026-03-20", 592.542345, 90.1444, 58.1455)]
    [DataRow(0.0, "2026-03-20", 892.604030, 270.0089, 46.8641)]
    [DataRow(68.2, "2026-01-15", 710.593438, 177.8055, 1.1079)]
    public void At_MatchesNoaa(double latitude, string date, double solarMinutes, double azimuth, double elevation)
    {
        SunPosition sun = SolarPosition.At(latitude, Date(date), solarMinutes / 60.0);
        Assert.AreEqual(0.0, AngleDifference(azimuth, sun.AzimuthDegrees), Degrees, $"azimuth {sun.AzimuthDegrees:0.0000}");
        Assert.AreEqual(elevation, sun.ElevationDegrees, Degrees, "elevation");
    }

    [TestMethod]
    [DataRow(46.02, "2026-10-15", 390.8907, 1048.3880)]
    [DataRow(46.02, "2026-06-21", 247.3269, 1192.6691)]
    [DataRow(-50.94, "2026-04-15", 403.6555, 1035.5348)]
    [DataRow(-50.94, "2026-12-21", 223.9686, 1216.0454)]
    [DataRow(51.4769, "2026-03-20", 355.3593, 1085.6455)]
    [DataRow(0.0, "2026-03-20", 356.6680, 1083.3320)]
    [DataRow(68.2, "2026-01-15", 628.6946, 811.9782)]
    public void Daylight_MatchesNoaaSunriseAndSunset(double latitude, string date, double sunriseMinutes, double sunsetMinutes)
    {
        Daylight daylight = SolarPosition.DaylightOn(latitude, Date(date));
        Assert.AreEqual(DaylightKind.RisesAndSets, daylight.Kind);
        Assert.AreEqual(sunriseMinutes, daylight.SunriseHours * 60.0, Minutes, "sunrise");
        Assert.AreEqual(sunsetMinutes, daylight.SunsetHours * 60.0, Minutes, "sunset");
    }

    [TestMethod]
    [DataRow(78.22, "2026-06-21", DaylightKind.PolarDay)]
    [DataRow(78.22, "2026-12-21", DaylightKind.PolarNight)]
    [DataRow(-77.85, "2026-12-21", DaylightKind.PolarDay)]
    [DataRow(-77.85, "2026-06-21", DaylightKind.PolarNight)]
    [DataRow(68.2, "2026-12-21", DaylightKind.PolarNight)]
    [DataRow(90.0, "2026-06-21", DaylightKind.PolarDay)]
    [DataRow(-90.0, "2026-06-21", DaylightKind.PolarNight)]
    // Just inside the Arctic Circle the midnight sun's upper limb, lifted by refraction, stays up; NOAA's
    // calculator also finds no sunrise or sunset there.
    [DataRow(66.0, "2026-06-21", DaylightKind.PolarDay)]
    public void Daylight_KnowsPolarDayAndNight(double latitude, string date, DaylightKind expected)
    {
        Daylight daylight = SolarPosition.DaylightOn(latitude, Date(date));
        Assert.AreEqual(expected, daylight.Kind);
        Assert.IsFalse(daylight.HasSunriseAndSunset);
        Assert.IsTrue(double.IsNaN(daylight.SunriseHours) && double.IsNaN(daylight.SunsetHours));
    }

    [TestMethod]
    public void Sunrise_PutsTheUpperLimbOnTheHorizon()
    {
        // At NOAA's sunrise the geometric centre is 0.833° down. NOAA's refraction fit gives only 0.40° there
        // (its 0.833 assumes 0.57° at the horizon), so the rendered centre sits 0.44° down: still just hidden.
        DateOnly date = Date("2026-10-15");
        Daylight daylight = SolarPosition.DaylightOn(46.02, date);
        SunPosition sun = SolarPosition.At(46.02, date, daylight.SunriseHours);
        Assert.AreEqual(-0.833, sun.TrueElevationDegrees, 1e-4);
        Assert.AreEqual(-0.436, sun.ElevationDegrees, 0.005);
    }

    [TestMethod]
    [DataRow(46.02, 180.0)]
    [DataRow(-51.08, 0.0)]
    public void SolarNoon_IsDueSouthOrDueNorth(double latitude, double azimuth)
    {
        SunPosition sun = SolarPosition.At(latitude, Date("2026-10-15"), 12.0);
        Assert.AreEqual(0.0, AngleDifference(azimuth, sun.AzimuthDegrees), 1e-9);
        Assert.AreEqual(90.0 - Math.Abs(latitude - sun.DeclinationDegrees), sun.TrueElevationDegrees, 1e-9);
    }

    [TestMethod]
    public void ToSun_UsesTheEngineAxes()
    {
        static Vector3 To(double az, double el) => new SunPosition(az, el, el, 0.0, 0.0).ToSun;
        AssertClose(Vector3.UnitX, To(90.0, 0.0), "east is +X");
        AssertClose(Vector3.UnitZ, To(0.0, 0.0), "north is +Z");
        AssertClose(-Vector3.UnitZ, To(180.0, 0.0), "south is −Z");
        AssertClose(Vector3.UnitY, To(123.0, 90.0), "zenith is +Y");
        Assert.AreEqual(MathF.Sin(30f * MathF.PI / 180f), To(250.0, 30.0).Y, 1e-6f);
    }

    [TestMethod]
    public void Refraction_IsAboutHalfADegreeAtTheHorizonAndNothingOverhead()
    {
        Assert.AreEqual(0.48, SolarPosition.RefractionDegrees(0.0), 0.01);
        Assert.AreEqual(0.0, SolarPosition.RefractionDegrees(89.0));
        Assert.IsTrue(SolarPosition.RefractionDegrees(30.0) is > 0.02 and < 0.04);
    }

    [TestMethod]
    public void JulianDay_MatchesMeeus()
    {
        Assert.AreEqual(2451544.5, SolarPosition.JulianDay(new DateOnly(2000, 1, 1)));
        Assert.AreEqual(2436115.5, SolarPosition.JulianDay(new DateOnly(1957, 10, 4)), "Meeus example 7.a: 1957 October 4.81 is JD 2436116.31");
        Assert.AreEqual(2461328.5, SolarPosition.JulianDay(new DateOnly(2026, 10, 15)));
    }

    [TestMethod]
    public void BadArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SolarPosition.At(90.5, Date("2026-01-01"), 12.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SolarPosition.At(10.0, Date("2026-01-01"), 24.5));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SolarPosition.At(double.NaN, Date("2026-01-01"), 12.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SolarPosition.DaylightOn(-91.0, Date("2026-01-01")));
    }

    private static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static double AngleDifference(double a, double b)
    {
        double d = (a - b) % 360.0;
        return d > 180.0 ? d - 360.0 : d < -180.0 ? d + 360.0 : d;
    }

    private static void AssertClose(Vector3 expected, Vector3 actual, string message) =>
        Assert.IsTrue(Vector3.Distance(expected, actual) < 1e-6f, $"{message}: {actual}");
}
