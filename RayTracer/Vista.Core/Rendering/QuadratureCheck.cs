using Vista.Atmosphere;
using Vista.Scene;

namespace Vista.Rendering;

/// <summary>
/// How true a <see cref="WavelengthTable"/> integrates colour (vista-plan §10 M1.12a): a set of spectra a landscape shows, each
/// integrated through the table and by dense 1 nm integration over the CIE 1931 functions (<see cref="CieObserver"/>), and the
/// difference as ΔE₇₆ and as a luminance ratio. Each spectrum is scaled to luminance 1 by the dense integral and judged in CIELAB
/// against equal-energy white (the flat spectrum's dense XYZ), so a light and a surface are judged alike: the cast a white card
/// would show under that light, or a surface of that colour at the brightness of white. Since M1.13 the twilight cases light snow's
/// own spectrum (<see cref="MaterialCatalog.SnowId"/>) in place of a flat 0.9, with the sun 1°, 1.5° and 1.8° below the horizontal,
/// and grass lit by light already bounced off grass joins them.
/// </summary>
public static class QuadratureCheck
{
    /// <summary>The sun's elevation for the twilight case, degrees: just below the horizon of the valley, still on the summits.</summary>
    public const double TwilightSunElevationDegrees = -1.0;

    /// <summary>The sun's elevations of the twilight cases since M1.13, degrees: alpenglow, deepening.</summary>
    public static IReadOnlyList<double> TwilightElevationsDegrees { get; } = [-1.0, -1.5, -1.8];

    /// <summary>Where the twilight case's light lands, m above sea level: a summit lit after the valley is in shadow.</summary>
    public const double TwilightSummitMetres = 4000.0;


    /// <summary>
    /// Pure water's absorption coefficient, m⁻¹, every 10 nm from 380 to 800 nm: Pope and Fry (1997) to 720 nm, Smith and Baker
    /// (1981) beyond (rounded). Flat past the ends, where the colour-matching functions hold almost nothing.
    /// </summary>
    public static IReadOnlyList<double> WaterAbsorption { get; } =
    [
        0.01137, 0.00851, 0.00663, 0.00473, 0.00454, 0.00495, 0.00635, 0.00922, 0.00979, 0.0106, 0.0127, 0.0150, 0.0204, 0.0325,
        0.0409, 0.0434, 0.0474, 0.0565, 0.0619, 0.0695, 0.0896, 0.1351, 0.2224, 0.2644, 0.2755, 0.2916, 0.3108, 0.3400, 0.4100,
        0.4390, 0.4650, 0.5160, 0.6240, 0.8270, 1.231, 1.80, 2.38, 2.47, 2.55, 2.51, 2.36, 2.16, 2.07,
    ];

    /// <summary>The spectra the check runs: a name and the spectrum as a function of the wavelength in nm.</summary>
    public static IReadOnlyList<QuadratureCase> Cases { get; } = BuildCases();

    /// <summary>
    /// The spectra M1.12a's equal bins integrate within 1 ΔE₇₆ and 1% in luminance: every case but the red end's (the twilight on snow
    /// and the twice-bounced grass), which they miss and which the default table since M1.13 (<see cref="Quadrature.Blend"/>) holds too.
    /// </summary>
    public static bool IsHeldToTheLimit(QuadratureCase spectrum)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        return !spectrum.Name.StartsWith("twilight", StringComparison.Ordinal) && !spectrum.Name.EndsWith("twice bounced", StringComparison.Ordinal);
    }

    /// <summary>How far <paramref name="table"/> integrates <paramref name="spectrum"/> from the dense integral.</summary>
    public static QuadratureError Measure(WavelengthTable table, Func<double, double> spectrum)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(spectrum);
        (double rx, double ry, double rz) = CieObserver.Integrate(spectrum);
        if (!(ry > 0.0))
            throw new ArgumentException("The spectrum has no luminance.", nameof(spectrum));
        return Difference((rx, ry, rz), table.Integrate(spectrum));
    }

    /// <summary>
    /// How far <paramref name="other"/> lies from <paramref name="reference"/> (two XYZ colours) as <see cref="Measure"/> judges it: both
    /// scaled so the reference has luminance 1, in CIELAB against equal-energy white. A pixel of two renders compares the same way.
    /// </summary>
    public static QuadratureError Difference((double X, double Y, double Z) reference, (double X, double Y, double Z) other)
    {
        if (!(reference.Y > 0.0))
            throw new ArgumentException("The reference has no luminance.", nameof(reference));
        double scale = 1.0 / reference.Y;
        (double l0, double a0, double b0) = Lab(reference.X * scale, 1.0, reference.Z * scale, White.X, White.Y, White.Z);
        (double l1, double a1, double b1) = Lab(other.X * scale, other.Y * scale, other.Z * scale, White.X, White.Y, White.Z);
        double de = Math.Sqrt((l1 - l0) * (l1 - l0) + (a1 - a0) * (a1 - a0) + (b1 - b0) * (b1 - b0));
        return new QuadratureError(de, other.Y * scale - 1.0, a1 - a0, b1 - b0);
    }

    /// <summary>Equal-energy white: a flat spectrum of 1 by dense integration.</summary>
    private static (double X, double Y, double Z) White { get; } = CieObserver.Integrate(_ => 1.0);

    /// <summary>
    /// The solar spectrum through <paramref name="airMass"/> times the default air's zenith column from sea level (Rayleigh, haze and
    /// ozone): the sun at elevations from overhead (1) to about 3° (20).
    /// </summary>
    public static double SunThroughAirMass(double nm, double airMass)
    {
        (double rayleigh, double aerosol, double ozone) = AtmosphereModel.Default.ZenithOpticalDepths(nm, 0.0);
        return SolarSpectrum.Radiance(nm) * Math.Exp(-airMass * (rayleigh + aerosol + ozone));
    }

    /// <summary>
    /// The sunlight that reaches <see cref="TwilightSummitMetres"/> with the sun at <see cref="TwilightSunElevationDegrees"/>, below the
    /// horizontal: the solar spectrum through the default air along the straight ray toward it over the curved Earth, which grazes the
    /// air about 3 km above sea level, and its optical depth in air masses of the sea-level zenith column at 550 nm.
    /// </summary>
    public static (Func<double, double> Spectrum, double AirMass) TwilightSun() => TwilightLight.Sun(TwilightSunElevationDegrees, TwilightSummitMetres);

    /// <summary>
    /// Columns of relative density, m, along the straight ray from <paramref name="altitudeMetres"/> at <paramref name="elevationDegrees"/>
    /// to the top of the air, over the sphere of <paramref name="model"/> (midpoints of 20 m steps).
    /// </summary>
    public static DensityColumns SlantColumns(AtmosphereModel model, double altitudeMetres, double elevationDegrees) =>
        TwilightLight.SlantColumns(model, altitudeMetres, elevationDegrees);

    /// <summary>Pure water's absorption at <paramref name="nm"/>, m⁻¹ (<see cref="WaterAbsorption"/>, linear between its points).</summary>
    public static double WaterAbsorptionAt(double nm)
    {
        IReadOnlyList<double> table = WaterAbsorption;
        double x = (nm - 380.0) / 10.0;
        if (x <= 0.0)
            return table[0];
        if (x >= table.Count - 1)
            return table[^1];
        int i = (int)Math.Floor(x);
        return table[i] + (x - i) * (table[i + 1] - table[i]);
    }

    private static List<QuadratureCase> BuildCases()
    {
        var cases = new List<QuadratureCase> { new("flat", _ => 1.0) };
        foreach (VistaMaterial material in MaterialCatalog.Ground)
        {
            IReadOnlyList<double> spectrum = material.Spectrum!;
            cases.Add(new QuadratureCase(material.Key, nm => IntegratorTables.ReflectanceAt(spectrum, nm)));
        }
        foreach (double metres in (double[])[2.0, 10.0])
            cases.Add(new QuadratureCase($"water {metres:0} m", nm => Math.Exp(-WaterAbsorptionAt(nm) * metres)));
        foreach (double airMass in (double[])[1.0, 2.0, 5.0, 10.0, 20.0])
            cases.Add(new QuadratureCase($"sun, air mass {airMass:0}", nm => SunThroughAirMass(nm, airMass)));
        cases.Add(new QuadratureCase("Rayleigh sky", nm => SolarSpectrum.Radiance(nm) * Math.Pow(550.0 / nm, 4.0)));
        IReadOnlyList<double> grass = MaterialCatalog.Ground[MaterialCatalog.GrassId].Spectrum!;
        cases.Add(new QuadratureCase("grass, twice bounced", nm => IntegratorTables.ReflectanceAt(grass, nm) * IntegratorTables.ReflectanceAt(grass, nm)));
        IReadOnlyList<double> snow = MaterialCatalog.Ground[MaterialCatalog.SnowId].Spectrum!;
        foreach (double elevation in TwilightElevationsDegrees)
        {
            (Func<double, double> twilight, double mass) = TwilightLight.Sun(elevation, TwilightSummitMetres);
            cases.Add(new QuadratureCase(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"twilight on snow, sun {elevation:0.0#}°, air mass {mass:0}"),
                nm => IntegratorTables.ReflectanceAt(snow, nm) * twilight(nm)));
        }
        return cases;
    }

    /// <summary>CIELAB of (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) against the white (<paramref name="wx"/>, …).</summary>
    private static (double L, double A, double B) Lab(double x, double y, double z, double wx, double wy, double wz)
    {
        static double F(double t) => t > 216.0 / 24389.0 ? Math.Cbrt(t) : (24389.0 / 27.0 * t + 16.0) / 116.0;
        double fx = F(x / wx), fy = F(y / wy), fz = F(z / wz);
        return (116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
    }
}

/// <summary>One spectrum of <see cref="QuadratureCheck"/>.</summary>
/// <param name="Name">What it is.</param>
/// <param name="Spectrum">Its value at a wavelength in nm (any scale: the check normalises it).</param>
public sealed record QuadratureCase(string Name, Func<double, double> Spectrum);

/// <summary>How far a quadrature's colour of one spectrum lies from the dense integral's.</summary>
/// <param name="DeltaE76">The CIELAB distance, the spectrum at the luminance of white.</param>
/// <param name="LuminanceError">The quadrature's luminance over the dense integral's, minus 1.</param>
/// <param name="DeltaA">a*'s shift (negative: greener).</param>
/// <param name="DeltaB">b*'s shift (negative: bluer).</param>
public readonly record struct QuadratureError(double DeltaE76, double LuminanceError, double DeltaA, double DeltaB);
