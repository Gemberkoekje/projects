namespace Vista.Atmosphere;

/// <summary>
/// Optical constants of clear air (vista-plan §5.2, §6.1), as constants like the CIE tables: built-in tier 5
/// until M2's manifest. Rayleigh scattering comes from the refractive index of air, so its optical depth is a
/// prediction checked against published values, not a fitted number; ozone absorption is a measured table.
///
/// <para><b>Rayleigh:</b> the cross-section per molecule is
/// <c>σ(λ) = 24π³/(N_s² λ⁴) · ((n_s² − 1)/(n_s² + 2))² · F_K(λ)</c> (Bodhaine et al. 1999, eq. 2), with the
/// refractive index of standard air from Peck &amp; Reeder 1972 (15 °C, 101 325 Pa, 300 ppm CO₂) at the number
/// density <c>N_s</c> it was measured at, and the King factor of Bates 1984. The zenith optical depth is that
/// cross-section times the air column <c>p₀·N_A/(M·g₀)</c>: about 0.097 at 550 nm, as Bodhaine et al. and
/// Bucholtz 1995 tabulate for a sea-level standard atmosphere.</para>
///
/// <para><b>Ozone:</b> the Chappuis band, which is why blue hour is blue. Cross-sections of Serdyuchenko et al.
/// 2014 (IUP Bremen, 233 K), averaged over 10 nm bins from 360 nm as tabulated in Eric Bruneton's
/// <i>precomputed_atmospheric_scattering</i> demo (demo.cc, © 2017 Eric Bruneton, BSD-3-Clause), interpolated
/// between bin centres.</para>
///
/// <para>Thread-safe: every member is a pure function.</para>
/// </summary>
public static class AirOptics
{
    /// <summary>Temperature of standard air (Peck &amp; Reeder 1972), K.</summary>
    public const double StandardTemperatureK = 288.15;

    /// <summary>Pressure of standard air and of the sea-level standard atmosphere, Pa.</summary>
    public const double StandardPressurePa = 101_325.0;

    /// <summary>Boltzmann constant, J/K (SI 2019, exact).</summary>
    public const double Boltzmann = 1.380649e-23;

    /// <summary>Avogadro constant, 1/mol (SI 2019, exact).</summary>
    public const double Avogadro = 6.02214076e23;

    /// <summary>Molar mass of dry air, kg/mol (US Standard Atmosphere 1976).</summary>
    public const double MolarMassAir = 0.0289644;

    /// <summary>Standard gravity, m/s².</summary>
    public const double StandardGravity = 9.80665;

    /// <summary>CO₂ in percent by volume, matching the refractive-index formula (300 ppm).</summary>
    public const double CarbonDioxidePercent = 0.03;

    /// <summary>Molecules per square metre in one Dobson unit.</summary>
    public const double DobsonUnit = 2.687e20;

    /// <summary>Number density of standard air, 1/m³ (≈ 2.547·10²⁵).</summary>
    public static double StandardNumberDensity => StandardPressurePa / (Boltzmann * StandardTemperatureK);

    /// <summary>Molecules in a vertical column of the sea-level standard atmosphere, 1/m² (≈ 2.148·10²⁹).</summary>
    public static double AirColumn => StandardPressurePa * Avogadro / (MolarMassAir * StandardGravity);

    /// <summary>
    /// The scale height whose exponential profile, starting at <see cref="StandardNumberDensity"/>, holds exactly
    /// <see cref="AirColumn"/>: the isothermal pressure scale height <c>RT/(Mg)</c> at 15 °C, ≈ 8435 m. It fits the
    /// standard atmosphere's density to within a few percent through the troposphere, where the scenes are,
    /// better than the 8 km of Bruneton 2017 and Hillaire 2020.
    /// </summary>
    public static double RayleighScaleHeightMetres => AirColumn / StandardNumberDensity;

    /// <summary>The refractive index of standard air minus one at <paramref name="nm"/> (Peck &amp; Reeder 1972, eq. 2).</summary>
    public static double RefractivityMinusOne(double nm)
    {
        double sigma2 = 1.0 / (nm * 1e-3 * nm * 1e-3); // (1/λ)² in µm⁻²
        return (8060.51 + 2_480_990.0 / (132.274 - sigma2) + 17_455.7 / (39.32957 - sigma2)) * 1e-8;
    }

    /// <summary>The King (depolarisation) correction factor of air at <paramref name="nm"/> (Bates 1984, via Bodhaine et al. 1999 eqs. 5–6).</summary>
    public static double KingFactor(double nm)
    {
        double um2 = nm * 1e-3 * nm * 1e-3;
        double nitrogen = 1.034 + 3.17e-4 / um2;
        double oxygen = 1.096 + 1.385e-3 / um2 + 1.448e-4 / (um2 * um2);
        const double argon = 1.0, carbonDioxide = 1.15;
        const double n2 = 78.084, o2 = 20.946, ar = 0.934;
        return (n2 * nitrogen + o2 * oxygen + ar * argon + CarbonDioxidePercent * carbonDioxide)
            / (n2 + o2 + ar + CarbonDioxidePercent);
    }

    /// <summary>Rayleigh scattering cross-section of one air molecule at <paramref name="nm"/>, m².</summary>
    public static double RayleighCrossSection(double nm)
    {
        double n = 1.0 + RefractivityMinusOne(nm);
        double n2 = n * n;
        double lorentz = (n2 - 1.0) / (n2 + 2.0);
        double lambda = nm * 1e-9;
        double ns = StandardNumberDensity;
        return 24.0 * Math.PI * Math.PI * Math.PI / (ns * ns * lambda * lambda * lambda * lambda) * lorentz * lorentz * KingFactor(nm);
    }

    /// <summary>Rayleigh zenith optical depth of the sea-level standard atmosphere at <paramref name="nm"/>.</summary>
    public static double RayleighColumnOpticalDepth(double nm) => RayleighCrossSection(nm) * AirColumn;

    /// <summary>First wavelength of the ozone table, nm (its bins are [360, 370), [370, 380), …).</summary>
    public const double OzoneTableStartNm = 360.0;

    /// <summary>Width of an ozone table bin, nm.</summary>
    public const double OzoneTableStepNm = 10.0;

    /// <summary>Ozone absorption cross-sections, m², one per 10 nm bin from 360 nm (see the class remarks).</summary>
    public static IReadOnlyList<double> OzoneTable { get; } =
    [
        1.18e-27, 2.182e-28, 2.818e-28, 6.636e-28, 1.527e-27, 2.763e-27, 5.52e-27,
        8.451e-27, 1.582e-26, 2.316e-26, 3.669e-26, 4.924e-26, 7.752e-26, 9.016e-26,
        1.48e-25, 1.602e-25, 2.139e-25, 2.755e-25, 3.091e-25, 3.5e-25, 4.266e-25,
        4.672e-25, 4.398e-25, 4.701e-25, 5.019e-25, 4.305e-25, 3.74e-25, 3.215e-25,
        2.662e-25, 2.238e-25, 1.852e-25, 1.473e-25, 1.209e-25, 9.423e-26, 7.455e-26,
        6.566e-26, 5.105e-26, 4.15e-26, 4.228e-26, 3.237e-26, 2.451e-26, 2.801e-26,
        2.534e-26, 1.624e-26, 1.465e-26, 2.078e-26, 1.383e-26, 7.105e-27,
    ];

    /// <summary>
    /// The ozone absorption cross-section at <paramref name="nm"/>, m²: linear between the centres of the table's
    /// bins (each bin's average sits at its middle, 365 nm, 375 nm, …), constant beyond the first and last.
    /// </summary>
    public static double OzoneCrossSection(double nm)
    {
        IReadOnlyList<double> table = OzoneTable;
        double x = (nm - OzoneTableStartNm - 0.5 * OzoneTableStepNm) / OzoneTableStepNm;
        if (x <= 0.0)
            return table[0];
        if (x >= table.Count - 1)
            return table[^1];
        int i = (int)Math.Floor(x);
        double f = x - i;
        return table[i] + f * (table[i + 1] - table[i]);
    }

    /// <summary>The Rayleigh phase function (no depolarisation term), 1/sr: <c>3(1 + cos²γ)/(16π)</c>.</summary>
    public static double RayleighPhase(double cosAngle) => 3.0 / (16.0 * Math.PI) * (1.0 + cosAngle * cosAngle);

    /// <summary>
    /// The Henyey–Greenstein phase function with asymmetry <paramref name="g"/>, 1/sr: the M1 aerosol phase
    /// (spectral Mie is M4). <paramref name="cosAngle"/> is the cosine between the light's travel directions
    /// before and after scattering, so +1 is straight on.
    /// </summary>
    public static double HenyeyGreenstein(double cosAngle, double g)
    {
        double denominator = 1.0 + g * g - 2.0 * g * cosAngle;
        return (1.0 - g * g) / (4.0 * Math.PI * denominator * Math.Sqrt(denominator));
    }
}
