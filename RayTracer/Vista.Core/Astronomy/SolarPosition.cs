using System.Numerics;

namespace Vista.Astronomy;

/// <summary>
/// Where the sun is in the sky (vista-plan §3.5): azimuth clockwise from north, elevation above the horizon.
/// </summary>
/// <param name="AzimuthDegrees">Compass bearing of the sun in [0, 360): 0 north, 90 east, 180 south.</param>
/// <param name="ElevationDegrees">
/// Apparent elevation: the geometric elevation lifted by the atmosphere's refraction (about 0.57° at the
/// horizon), which is where a camera sees the sun. This is the elevation the renderer uses.
/// </param>
/// <param name="TrueElevationDegrees">Geometric elevation, without refraction.</param>
/// <param name="DeclinationDegrees">The sun's declination on the date.</param>
/// <param name="EquationOfTimeMinutes">Apparent minus mean solar time at the instant.</param>
public readonly record struct SunPosition(
    double AzimuthDegrees,
    double ElevationDegrees,
    double TrueElevationDegrees,
    double DeclinationDegrees,
    double EquationOfTimeMinutes)
{
    /// <summary>
    /// Unit vector toward the sun in the engine's axes (+X east, +Y up, +Z north; vista-plan §10 M0), at the
    /// apparent elevation.
    /// </summary>
    public Vector3 ToSun
    {
        get
        {
            double az = AzimuthDegrees * SolarPosition.Radians;
            double el = ElevationDegrees * SolarPosition.Radians;
            return Vector3.Normalize(new Vector3(
                (float)(Math.Sin(az) * Math.Cos(el)),
                (float)Math.Sin(el),
                (float)(Math.Cos(az) * Math.Cos(el))));
        }
    }
}

/// <summary>Whether the sun rises and sets on a date, or stays up or down all day.</summary>
public enum DaylightKind
{
    /// <summary>Not computed.</summary>
    None,
    /// <summary>The sun rises and sets.</summary>
    RisesAndSets,
    /// <summary>Polar day: the sun's upper limb never drops below the horizon.</summary>
    PolarDay,
    /// <summary>Polar night: the sun's upper limb never clears the horizon.</summary>
    PolarNight,
}

/// <summary>Sunrise and sunset on a date, in local solar time.</summary>
/// <param name="Kind">Whether the sun rises and sets at all.</param>
/// <param name="SunriseHours">Sunrise in local solar hours; NaN unless <see cref="Kind"/> is <see cref="DaylightKind.RisesAndSets"/>.</param>
/// <param name="SunsetHours">Sunset in local solar hours; NaN unless <see cref="Kind"/> is <see cref="DaylightKind.RisesAndSets"/>.</param>
public readonly record struct Daylight(DaylightKind Kind, double SunriseHours, double SunsetHours)
{
    /// <summary>True when the sun rises and sets on the date.</summary>
    public bool HasSunriseAndSunset => Kind == DaylightKind.RisesAndSets;
}

/// <summary>
/// The sun's position from a latitude, a date and a local solar time, by the NOAA Global Monitoring
/// Laboratory's solar calculator (gml.noaa.gov/grad/solcalc), which implements Meeus, <i>Astronomical
/// Algorithms</i> (1991) in its low-precision form: good to about 0.01° in this century. Tests pin it to values
/// from NOAA's own code (vista-plan §10 M1.2).
///
/// <para><b>Local solar time</b> is apparent (sundial) time: the sun crosses the meridian at exactly 12:00 and
/// the hour angle is 15° per hour from there, so neither the longitude nor a time zone is needed (§3.5). The
/// one thing they would still fix is the <i>instant</i>, and with it the declination: this class takes the
/// instant on the prime meridian (universal time = solar time − equation of time). For a real place at
/// longitude λ the true instant differs by λ/15 hours, which moves the declination by at most about 0.2°
/// (λ = ±180° at an equinox, where it changes 0.4° a day) and the sunrise by under a minute.</para>
///
/// <para>Sunrise and sunset are when the sun's centre is 0.833° below the geometric horizon (its 0.267°
/// radius plus 0.566° of standard refraction), NOAA's definition.</para>
///
/// <para>Thread-safe: every member is a pure function.</para>
/// </summary>
public static class SolarPosition
{
    /// <summary>The geometric zenith angle of the sun's centre at sunrise and sunset (NOAA).</summary>
    public const double SunriseZenithDegrees = 90.833;

    /// <summary>Degrees to radians.</summary>
    internal const double Radians = Math.PI / 180.0;

    private const double JulianDayJ2000 = 2451545.0;
    private const double DaysPerJulianCentury = 36525.0;

    /// <summary>
    /// The sun at <paramref name="latitudeDegrees"/> on <paramref name="date"/> at
    /// <paramref name="solarHours"/> local solar time (12 is solar noon; values from 0 to 24 inclusive).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The latitude is outside [−90, 90] or the time outside [0, 24].</exception>
    public static SunPosition At(double latitudeDegrees, DateOnly date, double solarHours)
    {
        CheckLatitude(latitudeDegrees);
        if (!(solarHours is >= 0.0 and <= 24.0))
            throw new ArgumentOutOfRangeException(nameof(solarHours), solarHours, "Local solar time must be in [0, 24] hours.");

        (double declination, double equationOfTime) = SunAt(date, solarHours);
        double phi = latitudeDegrees * Radians;
        double delta = declination * Radians;
        double hourAngle = (solarHours - 12.0) * 15.0 * Radians;

        double sinElevation = Math.Clamp(
            Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta) * Math.Cos(hourAngle), -1.0, 1.0);
        double trueElevation = Math.Asin(sinElevation) / Radians;

        // Horizon coordinates, azimuth clockwise from north: cos(el)·sin(az) = −cos δ·sin H and
        // cos(el)·cos(az) = sin δ·cos φ − cos δ·sin φ·cos H. NOAA's acos form gives the same angle away from the
        // zenith and the poles, where this one stays defined.
        double east = -Math.Cos(delta) * Math.Sin(hourAngle);
        double north = Math.Sin(delta) * Math.Cos(phi) - Math.Cos(delta) * Math.Sin(phi) * Math.Cos(hourAngle);
        double azimuth = Math.Atan2(east, north) / Radians;
        if (azimuth < 0.0)
            azimuth += 360.0;
        if (azimuth >= 360.0)
            azimuth -= 360.0;

        return new SunPosition(azimuth, trueElevation + RefractionDegrees(trueElevation), trueElevation, declination, equationOfTime);
    }

    /// <summary>
    /// Sunrise and sunset at <paramref name="latitudeDegrees"/> on <paramref name="date"/>, in local solar
    /// time, or polar day or night when the sun stays up or down. Each event uses the declination at its own
    /// instant (iterated), so the two are not quite symmetric about noon.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The latitude is outside [−90, 90].</exception>
    public static Daylight DaylightOn(double latitudeDegrees, DateOnly date)
    {
        CheckLatitude(latitudeDegrees);
        double noonArgument = SunriseHourAngleCosine(latitudeDegrees, SunAt(date, 12.0).Declination);
        if (noonArgument <= -1.0)
            return new Daylight(DaylightKind.PolarDay, double.NaN, double.NaN);
        if (noonArgument >= 1.0)
            return new Daylight(DaylightKind.PolarNight, double.NaN, double.NaN);
        return new Daylight(DaylightKind.RisesAndSets, SunEvent(latitudeDegrees, date, rising: true), SunEvent(latitudeDegrees, date, rising: false));
    }

    /// <summary>
    /// Atmospheric refraction in degrees for a geometric elevation, NOAA's piecewise fit (Saemundsson-style
    /// above 5°, a polynomial down to −0.575°, a small tail below it, and nothing above 85°).
    /// </summary>
    public static double RefractionDegrees(double trueElevationDegrees)
    {
        double e = trueElevationDegrees;
        if (e > 85.0)
            return 0.0;
        double te = Math.Tan(e * Radians);
        double arcSeconds = e switch
        {
            > 5.0 => 58.1 / te - 0.07 / (te * te * te) + 0.000086 / (te * te * te * te * te),
            > -0.575 => 1735.0 + e * (-518.2 + e * (103.4 + e * (-12.79 + e * 0.711))),
            _ => -20.774 / te,
        };
        return arcSeconds / 3600.0;
    }

    /// <summary>Julian day at 0h universal time on <paramref name="date"/> (Meeus 7.1, Gregorian calendar).</summary>
    public static double JulianDay(DateOnly date)
    {
        int year = date.Year;
        int month = date.Month;
        if (month <= 2)
        {
            year -= 1;
            month += 12;
        }
        int a = year / 100;
        int b = 2 - a + a / 4;
        return Math.Floor(365.25 * (year + 4716)) + Math.Floor(30.6001 * (month + 1)) + date.Day + b - 1524.5;
    }

    /// <summary>
    /// Declination (degrees) and equation of time (minutes) at <paramref name="solarHours"/> on
    /// <paramref name="date"/>, the instant taken on the prime meridian. Universal time is solar time minus the
    /// equation of time, which itself depends on the instant; three fixed-point steps converge far below a
    /// second (it changes by under 30 s a day).
    /// </summary>
    internal static (double Declination, double EquationOfTime) SunAt(DateOnly date, double solarHours)
    {
        double day = JulianDay(date);
        double universalHours = solarHours;
        double equationOfTime = 0.0;
        for (int i = 0; i < 3; i++)
        {
            equationOfTime = EquationOfTimeMinutes(Centuries(day + universalHours / 24.0));
            universalHours = solarHours - equationOfTime / 60.0;
        }
        double t = Centuries(day + universalHours / 24.0);
        return (DeclinationDegrees(t), EquationOfTimeMinutes(t));
    }

    private static double SunEvent(double latitudeDegrees, DateOnly date, bool rising)
    {
        double hours = rising ? 6.0 : 18.0;
        for (int i = 0; i < 4; i++)
        {
            double cosine = SunriseHourAngleCosine(latitudeDegrees, SunAt(date, hours).Declination);
            // On the first or last day of a polar day or night the noon test can pass while the event's own
            // instant just fails it; the sun then grazes the horizon at midnight or noon.
            double hourAngle = Math.Acos(Math.Clamp(cosine, -1.0, 1.0)) / Radians;
            hours = rising ? 12.0 - hourAngle / 15.0 : 12.0 + hourAngle / 15.0;
        }
        return hours;
    }

    private static double SunriseHourAngleCosine(double latitudeDegrees, double declinationDegrees)
    {
        double phi = latitudeDegrees * Radians;
        double delta = declinationDegrees * Radians;
        double denominator = Math.Cos(phi) * Math.Cos(delta);
        if (Math.Abs(denominator) < 1e-12)
            return Math.Sign(latitudeDegrees) == Math.Sign(declinationDegrees) ? -1.0 : 1.0;
        return Math.Cos(SunriseZenithDegrees * Radians) / denominator - Math.Tan(phi) * Math.Tan(delta);
    }

    private static double Centuries(double julianDay) => (julianDay - JulianDayJ2000) / DaysPerJulianCentury;

    private static double GeometricMeanLongitude(double t)
    {
        double l0 = 280.46646 + t * (36000.76983 + t * 0.0003032);
        l0 %= 360.0;
        return l0 < 0.0 ? l0 + 360.0 : l0;
    }

    private static double GeometricMeanAnomaly(double t) => 357.52911 + t * (35999.05029 - 0.0001537 * t);

    private static double Eccentricity(double t) => 0.016708634 - t * (0.000042037 + 0.0000001267 * t);

    private static double EquationOfCentre(double t)
    {
        double m = GeometricMeanAnomaly(t) * Radians;
        return Math.Sin(m) * (1.914602 - t * (0.004817 + 0.000014 * t))
            + Math.Sin(2.0 * m) * (0.019993 - 0.000101 * t)
            + Math.Sin(3.0 * m) * 0.000289;
    }

    private static double ApparentLongitude(double t)
    {
        double trueLongitude = GeometricMeanLongitude(t) + EquationOfCentre(t);
        double omega = 125.04 - 1934.136 * t;
        return trueLongitude - 0.00569 - 0.00478 * Math.Sin(omega * Radians);
    }

    private static double Obliquity(double t)
    {
        double seconds = 21.448 - t * (46.8150 + t * (0.00059 - t * 0.001813));
        double mean = 23.0 + (26.0 + seconds / 60.0) / 60.0;
        double omega = 125.04 - 1934.136 * t;
        return mean + 0.00256 * Math.Cos(omega * Radians);
    }

    private static double DeclinationDegrees(double t) =>
        Math.Asin(Math.Sin(Obliquity(t) * Radians) * Math.Sin(ApparentLongitude(t) * Radians)) / Radians;

    private static double EquationOfTimeMinutes(double t)
    {
        double epsilon = Obliquity(t) * Radians;
        double l0 = GeometricMeanLongitude(t) * Radians;
        double e = Eccentricity(t);
        double m = GeometricMeanAnomaly(t) * Radians;
        double y = Math.Tan(epsilon / 2.0);
        y *= y;
        double radians = y * Math.Sin(2.0 * l0)
            - 2.0 * e * Math.Sin(m)
            + 4.0 * e * y * Math.Sin(m) * Math.Cos(2.0 * l0)
            - 0.5 * y * y * Math.Sin(4.0 * l0)
            - 1.25 * e * e * Math.Sin(2.0 * m);
        return radians / Radians * 4.0;
    }

    private static void CheckLatitude(double latitudeDegrees)
    {
        if (!(latitudeDegrees is >= -90.0 and <= 90.0))
            throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), latitudeDegrees, "Latitude must be in [−90, 90] degrees.");
    }
}
