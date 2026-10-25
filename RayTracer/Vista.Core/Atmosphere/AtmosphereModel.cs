using System.Globalization;

namespace Vista.Atmosphere;

/// <summary>
/// The air above a Vista scene (vista-plan §5.2, §10 M1.3): a spherical shell from sea level to
/// <see cref="TopAltitudeMetres"/> holding three species, each a fixed density profile times its optical
/// coefficients.
/// <list type="bullet">
///   <item><b>Air molecules</b> (Rayleigh scattering): exponential with <see cref="RayleighScaleHeightMetres"/>,
///   coefficients from <see cref="AirOptics"/>.</item>
///   <item><b>Aerosol haze</b> (Mie, Henyey–Greenstein until M4): exponential with
///   <see cref="AerosolScaleHeightMetres"/>; its extinction follows Ångström's law,
///   <c>τ(λ) = τ₅₅₀·(λ/550)^−α</c>, a fraction <see cref="AerosolSingleScatteringAlbedo"/> of it scattering.</item>
///   <item><b>Ozone</b> (absorption only): a tent rising linearly from zero at
///   <see cref="OzonePeakAltitudeMetres"/> − <see cref="OzoneHalfWidthMetres"/> to its peak and back
///   (Bruneton 2017's approximate profile), holding <see cref="OzoneDobsonUnits"/>.</item>
/// </list>
/// Densities are relative: 1 at sea level for molecules and haze, 1 at the peak for ozone, so a coefficient
/// times a relative density is per metre, and a coefficient times a column of relative density (metres) is
/// an optical depth.
///
/// <para>The defaults are a clear continental day: haze of optical depth 0.1 at 550 nm (Ångström's α = 1.3)
/// and Bruneton 2017's aerosol albedo 0.9 and asymmetry 0.8; 300 Dobson units of ozone. Because the haze
/// profile starts at sea level, a scene high in the mountains sits above most of it, as real mountain air
/// does. Since M1.11 the fog/haze slider sets <see cref="AerosolOpticalDepth550"/> (Weather.Haze: from the day's humidity, or a
/// pinned visibility); this default is the clear day it is scaled from.</para>
///
/// <para><b>The valley haze</b> (vista-plan §10 M1.13a) is more of the same haze trapped under an inversion: a layer of
/// <see cref="ValleyHazeExtinction550"/> below <see cref="ValleyHazeTopMetres"/>, uniform under its lid and falling off over a
/// logistic of scale <see cref="ValleyHazeSoftnessMetres"/> above it, added to the haze's own profile. It has the haze's optical
/// properties, so the haze's coefficients serve it and its density is counted in the haze's unit (<see cref="ValleyHazeDensity"/>).
/// It depends on altitude alone, so every table and march takes it in as they take the haze; because its lid is sharper than
/// any march's steps, a march takes the layer's exact mean over each step (<see cref="AerosolStepDensity"/>), never a sample.
/// None by default.</para>
///
/// <para>The <b>ground</b> is not part of the model: a scene puts it at its base altitude
/// (<see cref="AtmosphereFrame"/>), a sphere that casts the Earth's shadow and stands in for the land beyond
/// the terrain tile, with a flat <see cref="GroundAlbedo"/>.</para>
/// </summary>
public sealed record AtmosphereModel
{
    /// <summary>The clear day's haze: vertical optical depth at 550 nm from sea level (the default <see cref="AerosolOpticalDepth550"/>).</summary>
    public const double ClearDayOpticalDepth550 = 0.1;

    /// <summary>Radius of the sea-level sphere, m (Bruneton 2017, Hillaire 2020).</summary>
    public double PlanetRadiusMetres { get; init; } = 6_360_000.0;

    /// <summary>Altitude of the top of the atmosphere above sea level, m (Hillaire 2020).</summary>
    public double TopAltitudeMetres { get; init; } = 100_000.0;

    /// <summary>Scale height of the air molecules, m (<see cref="AirOptics.RayleighScaleHeightMetres"/>).</summary>
    public double RayleighScaleHeightMetres { get; init; } = AirOptics.RayleighScaleHeightMetres;

    /// <summary>Vertical optical depth of the haze from sea level at 550 nm.</summary>
    public double AerosolOpticalDepth550 { get; init; } = ClearDayOpticalDepth550;

    /// <summary>Ångström exponent of the haze's extinction.</summary>
    public double AngstromExponent { get; init; } = 1.3;

    /// <summary>Fraction of the haze's extinction that is scattering.</summary>
    public double AerosolSingleScatteringAlbedo { get; init; } = 0.9;

    /// <summary>Henyey–Greenstein asymmetry of the haze.</summary>
    public double AerosolAsymmetry { get; init; } = 0.8;

    /// <summary>Scale height of the haze, m (Bruneton 2017, Hillaire 2020).</summary>
    public double AerosolScaleHeightMetres { get; init; } = 1200.0;

    /// <summary>Total ozone column, Dobson units.</summary>
    public double OzoneDobsonUnits { get; init; } = 300.0;

    /// <summary>Altitude of the ozone peak above sea level, m.</summary>
    public double OzonePeakAltitudeMetres { get; init; } = 25_000.0;

    /// <summary>Half-width of the ozone tent, m (it is zero below peak − this and above peak + this).</summary>
    public double OzoneHalfWidthMetres { get; init; } = 15_000.0;

    /// <summary>Lambertian albedo of the ground sphere (the land beyond the terrain tile), flat in wavelength.</summary>
    public double GroundAlbedo { get; init; } = 0.2;

    /// <summary>Altitude above sea level of the valley haze's lid, m: the middle of its fall, where the layer is half as dense.</summary>
    public double ValleyHazeTopMetres { get; init; }

    /// <summary>
    /// How soft the valley haze's lid is: the logistic's scale, m. The layer thins from 90% to 10% of its density over 4.4 times
    /// this (132 m at the default 30 m): a lid that reads crisp from ten kilometres and more, as an inversion's does.
    /// </summary>
    public double ValleyHazeSoftnessMetres { get; init; } = 30.0;

    /// <summary>The valley haze's extinction at 550 nm under its lid, per metre, on top of the haze's own; 0 for none.</summary>
    public double ValleyHazeExtinction550 { get; init; }

    /// <summary>The default clear-day atmosphere.</summary>
    public static AtmosphereModel Default { get; } = new();

    /// <summary>Radius of the top of the atmosphere, m.</summary>
    public double TopRadiusMetres => PlanetRadiusMetres + TopAltitudeMetres;

    /// <summary>
    /// The optical depth the haze's coefficients are built from (<see cref="CoefficientsAt"/>): the haze's own, or the clear day's
    /// when there is none but a valley haze, so the layer keeps its coefficients in air that is otherwise clean.
    /// </summary>
    public double HazeUnitOpticalDepth => AerosolOpticalDepth550 > 0.0 || !(ValleyHazeExtinction550 > 0.0) ? AerosolOpticalDepth550 : ClearDayOpticalDepth550;

    /// <summary>The haze's own profile in the haze's unit: exactly 1, or 0 when only a valley haze set the unit.</summary>
    public double HazeScale => HazeUnitOpticalDepth > 0.0 ? AerosolOpticalDepth550 / HazeUnitOpticalDepth : 0.0;

    /// <summary>The valley haze's density under its lid, in the haze's unit (the haze's own density at sea level); 0 for none.</summary>
    public double ValleyHazeDensity =>
        ValleyHazeExtinction550 > 0.0 ? ValleyHazeExtinction550 * AerosolScaleHeightMetres / HazeUnitOpticalDepth : 0.0;

    /// <summary>Ozone number density at the peak, 1/m³: the column spread over the tent's area (one half-width).</summary>
    public double OzonePeakNumberDensity => OzoneDobsonUnits * AirOptics.DobsonUnit / OzoneHalfWidthMetres;

    /// <summary>The optical coefficients at <paramref name="nm"/>, per metre at relative density 1.</summary>
    public AtmosphereCoefficients CoefficientsAt(double nm)
    {
        double rayleigh = AirOptics.RayleighCrossSection(nm) * AirOptics.StandardNumberDensity
            * (AirOptics.RayleighScaleHeightMetres / RayleighScaleHeightMetres);
        double aerosolExtinction = HazeUnitOpticalDepth * Math.Pow(nm / 550.0, -AngstromExponent) / AerosolScaleHeightMetres;
        return new AtmosphereCoefficients(
            rayleigh,
            aerosolExtinction * AerosolSingleScatteringAlbedo,
            aerosolExtinction,
            AirOptics.OzoneCrossSection(nm) * OzonePeakNumberDensity);
    }

    /// <summary>Relative density of the air molecules at <paramref name="altitudeMetres"/> above sea level.</summary>
    public double RayleighDensity(double altitudeMetres) => Math.Exp(-altitudeMetres / RayleighScaleHeightMetres);

    /// <summary>Relative density of the haze at <paramref name="altitudeMetres"/> above sea level: its own profile and the valley haze.</summary>
    public double AerosolDensity(double altitudeMetres) =>
        HazeScale * Math.Exp(-altitudeMetres / AerosolScaleHeightMetres) + ValleyHaze(altitudeMetres);

    /// <summary>The valley haze's relative density at <paramref name="altitudeMetres"/> above sea level: <see cref="ValleyHazeDensity"/> under a logistic lid.</summary>
    public double ValleyHaze(double altitudeMetres)
    {
        double density = ValleyHazeDensity;
        return density > 0.0 ? density / (1.0 + Math.Exp((altitudeMetres - ValleyHazeTopMetres) / ValleyHazeSoftnessMetres)) : 0.0;
    }

    /// <summary>
    /// The valley haze's mean relative density between altitudes <paramref name="a"/> and <paramref name="b"/> (either order): its
    /// integral over the altitudes, <c>w·(softplus(x_a) − softplus(x_b))</c> with <c>x = (top − h)/w</c>, over their difference, so
    /// a straight path between them gets the layer's exact optical depth whatever its steps; within 10⁻³ w, the density in the middle.
    /// </summary>
    public double ValleyHazeMean(double a, double b)
    {
        double density = ValleyHazeDensity;
        if (!(density > 0.0))
            return 0.0;
        double w = ValleyHazeSoftnessMetres;
        double xa = (ValleyHazeTopMetres - a) / w, xb = (ValleyHazeTopMetres - b) / w;
        double dx = xa - xb;
        if (Math.Abs(dx) < 1e-3)
            return density / (1.0 + Math.Exp(-0.5 * (xa + xb)));
        return density * (Softplus(xa) - Softplus(xb)) / dx;
    }

    /// <summary>
    /// The haze's relative density over one step of a march whose altitudes are <paramref name="start"/>, <paramref name="middle"/>
    /// and <paramref name="end"/> above sea level: its own profile at the middle, as every march samples it, and the valley haze's
    /// exact mean along the two halves (<see cref="ValleyHazeMean"/>), which also holds where a chord dips to its lowest inside the
    /// step. Without a valley haze, exactly <see cref="AerosolDensity"/> at the middle.
    /// </summary>
    public double AerosolStepDensity(double start, double middle, double end)
    {
        double own = HazeScale * Math.Exp(-middle / AerosolScaleHeightMetres);
        return ValleyHazeDensity > 0.0 ? own + 0.5 * (ValleyHazeMean(start, middle) + ValleyHazeMean(middle, end)) : own;
    }

    /// <summary>The valley haze's column from <paramref name="altitudeMetres"/> straight up to <paramref name="topMetres"/>, m of relative density.</summary>
    public double ValleyHazeColumn(double altitudeMetres, double topMetres)
    {
        double density = ValleyHazeDensity;
        if (!(density > 0.0))
            return 0.0;
        double w = ValleyHazeSoftnessMetres;
        return density * w * (Softplus((ValleyHazeTopMetres - altitudeMetres) / w) - Softplus((ValleyHazeTopMetres - topMetres) / w));
    }

    /// <summary><c>ln(1 + eˣ)</c> without overflow.</summary>
    public static double Softplus(double x) => Math.Max(x, 0.0) + Math.Log(1.0 + Math.Exp(-Math.Abs(x)));

    /// <summary>Relative density of ozone at <paramref name="altitudeMetres"/> above sea level (the tent).</summary>
    public double OzoneDensity(double altitudeMetres) =>
        Math.Max(0.0, 1.0 - Math.Abs(altitudeMetres - OzonePeakAltitudeMetres) / OzoneHalfWidthMetres);

    /// <summary>
    /// Columns of relative density, m, straight up from <paramref name="altitudeMetres"/> to the top: exact,
    /// because a vertical ray is radial (the integrals of an exponential and a tent).
    /// </summary>
    public DensityColumns ZenithColumns(double altitudeMetres)
    {
        double top = TopAltitudeMetres;
        double h = Math.Clamp(altitudeMetres, 0.0, top);
        double Exponential(double scale) => scale * (Math.Exp(-h / scale) - Math.Exp(-top / scale));
        return new DensityColumns(Exponential(RayleighScaleHeightMetres),
            HazeScale * Exponential(AerosolScaleHeightMetres) + ValleyHazeColumn(h, top), OzoneColumnAbove(h));
    }

    /// <summary>Zenith optical depths from <paramref name="altitudeMetres"/> at <paramref name="nm"/>: (Rayleigh, aerosol, ozone).</summary>
    public (double Rayleigh, double Aerosol, double Ozone) ZenithOpticalDepths(double nm, double altitudeMetres)
    {
        AtmosphereCoefficients k = CoefficientsAt(nm);
        DensityColumns c = ZenithColumns(altitudeMetres);
        return (k.Rayleigh * c.Rayleigh, k.AerosolExtinction * c.Aerosol, k.Ozone * c.Ozone);
    }

    /// <summary>Problems that make this model unusable (empty when it is valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        void Positive(string name, double value)
        {
            if (!(value > 0.0) || !double.IsFinite(value))
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"{name} must be positive, got {value}."));
        }
        Positive(nameof(PlanetRadiusMetres), PlanetRadiusMetres);
        Positive(nameof(TopAltitudeMetres), TopAltitudeMetres);
        Positive(nameof(RayleighScaleHeightMetres), RayleighScaleHeightMetres);
        Positive(nameof(AerosolScaleHeightMetres), AerosolScaleHeightMetres);
        Positive(nameof(OzoneHalfWidthMetres), OzoneHalfWidthMetres);
        if (!(AerosolOpticalDepth550 >= 0.0) || !(OzoneDobsonUnits >= 0.0))
            errors.Add("Haze optical depth and ozone column must not be negative.");
        if (!(AerosolSingleScatteringAlbedo is >= 0.0 and <= 1.0) || !(GroundAlbedo is >= 0.0 and <= 1.0))
            errors.Add("Albedos must be in [0, 1].");
        if (!(AerosolAsymmetry is > -1.0 and < 1.0))
            errors.Add("The haze asymmetry must be in (−1, 1).");
        if (OzonePeakAltitudeMetres + OzoneHalfWidthMetres > TopAltitudeMetres || OzonePeakAltitudeMetres - OzoneHalfWidthMetres < 0.0)
            errors.Add("The ozone layer must lie between sea level and the top of the atmosphere.");
        Positive(nameof(ValleyHazeSoftnessMetres), ValleyHazeSoftnessMetres);
        if (!(ValleyHazeExtinction550 >= 0.0) || !double.IsFinite(ValleyHazeExtinction550))
            errors.Add("The valley haze's extinction must not be negative.");
        if (!(ValleyHazeTopMetres >= 0.0 && ValleyHazeTopMetres <= TopAltitudeMetres))
            errors.Add("The valley haze's lid must lie between sea level and the top of the atmosphere.");
        return errors;
    }

    private double OzoneColumnAbove(double h)
    {
        double peak = OzonePeakAltitudeMetres;
        double w = OzoneHalfWidthMetres;
        // The tent's integral from h upward: its whole area (one half-width) minus the part below h.
        double below;
        if (h <= peak - w)
            below = 0.0;
        else if (h <= peak)
        {
            double rise = h - (peak - w);
            below = rise * rise / (2.0 * w);
        }
        else if (h < peak + w)
        {
            double fall = peak + w - h;
            below = w - fall * fall / (2.0 * w);
        }
        else
            below = w;
        return w - below;
    }
}

/// <summary>Optical coefficients at one wavelength, per metre at relative density 1.</summary>
/// <param name="Rayleigh">Molecular scattering (no absorption) at sea level.</param>
/// <param name="AerosolScattering">Haze scattering at sea level.</param>
/// <param name="AerosolExtinction">Haze extinction (scattering plus absorption) at sea level.</param>
/// <param name="Ozone">Ozone absorption at the peak.</param>
public readonly record struct AtmosphereCoefficients(double Rayleigh, double AerosolScattering, double AerosolExtinction, double Ozone)
{
    /// <summary>The optical depth of <paramref name="columns"/> at this wavelength.</summary>
    public double OpticalDepth(DensityColumns columns) =>
        Rayleigh * columns.Rayleigh + AerosolExtinction * columns.Aerosol + Ozone * columns.Ozone;
}

/// <summary>Columns of relative density along a path, m: optical depth is each one times its coefficient.</summary>
/// <param name="Rayleigh">Air molecules.</param>
/// <param name="Aerosol">Haze.</param>
/// <param name="Ozone">Ozone.</param>
public readonly record struct DensityColumns(double Rayleigh, double Aerosol, double Ozone);
