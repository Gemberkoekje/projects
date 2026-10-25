namespace Vista.Rendering;

/// <summary>
/// Snow's and ice's reflectance from ice absorption and grain size (vista-plan §4.5, §6.2 tier 5, §10 M1.13): the asymptotic radiative
/// transfer of Kokhanovsky and Zege (2004), whose spherical (white-sky) albedo of a deep snowpack is
/// <c>r = exp(−√((α + α_imp)·l))</c>, with <c>α = 4πκ/λ</c> ice's absorption coefficient, <c>α_imp</c> that of the light-absorbing
/// impurities in it, and <c>l = b²·d</c> the effective absorption length of grains of optical diameter <c>d</c> (b = 3.62 for the
/// irregular grains of real snow, so l ≈ 13.1·d; Negi and Kokhanovsky 2011). The impurities absorb as a power of the wavelength,
/// <c>α_imp(λ) = α₅₆₀·(λ/560 nm)^−m</c> (an Ångström absorption exponent m: about 1 for soot, 3–7 for mineral dust; Kokhanovsky et al.
/// 2018). Fine dry grains reflect nearly all visible light; coarse wet grains, firn and bubbly glacier ice reflect less, most of all in
/// the red, where ice absorbs, and impurities grey them, most of all in the blue.
///
/// <para>κ is Picard et al. (2016)'s from 350 to 595 nm (measured in Antarctic snow; up to 26 times Warren and Brandt's in the blue),
/// at 600 nm theirs from their absorption table, and Warren and Brandt (2008)'s from 610 nm on, interpolated linearly in log κ against
/// log λ as both tables are meant to be. Every material is a table of (wavelength, reflectance) pairs 5 nm apart from 380 to 780 nm,
/// which the integrator's own quadrature then samples (<see cref="IntegratorTables"/>).</para>
/// </summary>
public static class SnowSpectrum
{
    /// <summary>b² in the effective absorption length <c>l = b²·d</c> (b = 3.62, Negi and Kokhanovsky 2011, after Kokhanovsky and Zege 2004).</summary>
    public const double LengthPerDiameter = 3.62 * 3.62;

    /// <summary>The wavelength the impurities' absorption is given at, nm.</summary>
    public const double ImpurityReferenceNm = 560.0;

    // (nm, κ): Picard et al. 2016 (The Cryosphere 10, 2655; snow.univ-grenoble-alpes.fr/files/ice_refractive_picard2016.dat) to 595 nm,
    // 600 nm from their Table S1 (α = 0.13597 m⁻¹), then Warren and Brandt 2008 (atmos.uw.edu/ice_optical_constants, IOP_2008_ASCIItable.dat).
    private static readonly double[] Kappa =
    [
        350, 6.991e-10, 355, 6.781e-10, 360, 6.638e-10, 365, 6.553e-10, 370, 6.478e-10, 375, 6.399e-10, 380, 6.420e-10, 385, 6.278e-10,
        390, 6.340e-10, 395, 6.162e-10, 400, 6.275e-10, 405, 6.061e-10, 410, 5.849e-10, 415, 5.716e-10, 420, 5.765e-10, 425, 5.865e-10,
        430, 5.972e-10, 435, 6.196e-10, 440, 6.374e-10, 445, 6.612e-10, 450, 6.896e-10, 455, 7.186e-10, 460, 7.520e-10, 465, 7.900e-10,
        470, 8.402e-10, 475, 8.921e-10, 480, 9.469e-10, 485, 1.0089e-9, 490, 1.0791e-9, 495, 1.1593e-9, 500, 1.2457e-9, 505, 1.3446e-9,
        510, 1.4568e-9, 515, 1.5652e-9, 520, 1.6960e-9, 525, 1.8334e-9, 530, 1.9857e-9, 535, 2.1573e-9, 540, 2.3470e-9, 545, 2.5657e-9,
        550, 2.8008e-9, 555, 3.0635e-9, 560, 3.3461e-9, 565, 3.6448e-9, 570, 3.9598e-9, 575, 4.3020e-9, 580, 4.6519e-9, 585, 5.0425e-9,
        590, 5.4761e-9, 595, 5.9682e-9, 600, 6.494e-9,
        610, 6.890e-9, 620, 8.580e-9, 630, 1.040e-8, 640, 1.220e-8, 650, 1.430e-8, 660, 1.660e-8, 670, 1.890e-8, 680, 2.090e-8,
        690, 2.400e-8, 700, 2.900e-8, 710, 3.440e-8, 720, 4.030e-8, 730, 4.300e-8, 740, 4.920e-8, 750, 5.870e-8, 760, 7.080e-8,
        770, 8.580e-8, 780, 1.020e-7, 790, 1.180e-7, 800, 1.340e-7,
    ];

    /// <summary>The imaginary part of ice's refractive index at <paramref name="nm"/> (flat past the table's ends).</summary>
    public static double IceKappa(double nm)
    {
        if (nm <= Kappa[0])
            return Kappa[1];
        for (int k = 2; k < Kappa.Length; k += 2)
        {
            if (nm <= Kappa[k])
            {
                double t = Math.Log(nm / Kappa[k - 2]) / Math.Log(Kappa[k] / Kappa[k - 2]);
                return Math.Exp(Math.Log(Kappa[k - 1]) + t * Math.Log(Kappa[k + 1] / Kappa[k - 1]));
            }
        }
        return Kappa[^1];
    }

    /// <summary>Ice's absorption coefficient at <paramref name="nm"/>, 1/m: <c>4πκ/λ</c>.</summary>
    public static double IceAbsorption(double nm) => 4.0 * Math.PI * IceKappa(nm) / (nm * 1e-9);

    /// <summary>
    /// The white-sky albedo at <paramref name="nm"/> of a deep pack of grains <paramref name="grainMetres"/> across (optical diameter),
    /// with impurities absorbing <paramref name="impurity560"/> per metre at 560 nm and an Ångström exponent of <paramref name="angstrom"/>.
    /// </summary>
    public static double Albedo(double nm, double grainMetres, double impurity560, double angstrom)
    {
        double impurity = impurity560 * Math.Pow(nm / ImpurityReferenceNm, -angstrom);
        return Math.Exp(-Math.Sqrt((IceAbsorption(nm) + impurity) * LengthPerDiameter * grainMetres));
    }

    /// <summary>
    /// The reflectance spectrum of such a pack as (wavelength in nm, reflectance) pairs every 5 nm from 380 to 780 nm, rounded to
    /// 4 decimals (<see cref="Scene.VistaMaterial.Spectrum"/>).
    /// </summary>
    public static double[] Spectrum(double grainMetres, double impurity560, double angstrom)
    {
        var pairs = new double[2 * 81];
        for (int k = 0; k < 81; k++)
        {
            double nm = 380 + 5 * k;
            pairs[2 * k] = nm;
            pairs[2 * k + 1] = Math.Round(Albedo(nm, grainMetres, impurity560, angstrom), 4);
        }
        return pairs;
    }
}
