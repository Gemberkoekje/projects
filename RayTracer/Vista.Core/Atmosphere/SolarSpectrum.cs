namespace Vista.Atmosphere;

/// <summary>
/// The sun above the atmosphere (vista-plan §10 M1 decisions): a disk of the real angular radius radiating
/// the engine's 5778 K Planck spectrum, in physical units. Spectral radiance is in W·m⁻²·sr⁻¹·nm⁻¹, so the
/// irradiance at the top of the atmosphere integrates to about 1368 W/m² (the solar constant is 1361) and is
/// about 1.76 W·m⁻²·nm⁻¹ at 550 nm (measured: 1.86). The atmosphere does all the reddening; nothing here
/// depends on the sun's height.
/// </summary>
public static class SolarSpectrum
{
    /// <summary>Effective temperature of the sun, K (the engine's daylight blackbody).</summary>
    public const double TemperatureK = 5778.0;

    /// <summary>Angular radius of the sun's disk seen from the Earth (0.2666°), radians.</summary>
    public const double AngularRadiusRadians = 0.004653;

    /// <summary>
    /// <c>1 − cos</c> of <see cref="AngularRadiusRadians"/>, as <c>2·sin²(θ/2)</c>: in float the subtraction
    /// <c>1 − cos θ</c> loses about 0.2% (an M0 finding). Directions whose <c>1 − cos</c> to the sun is at most
    /// this are on the disk.
    /// </summary>
    public static double OneMinusCosRadius
    {
        get
        {
            double half = Math.Sin(AngularRadiusRadians * 0.5);
            return 2.0 * half * half;
        }
    }

    /// <summary>Solid angle of the disk, sr (≈ 6.80·10⁻⁵).</summary>
    public static double SolidAngle => 2.0 * Math.PI * OneMinusCosRadius;

    /// <summary>Spectral radiance of the disk at <paramref name="nm"/>, W·m⁻²·sr⁻¹·nm⁻¹ (Planck's law at <see cref="TemperatureK"/>).</summary>
    public static double Radiance(double nm)
    {
        const double h = 6.62607015e-34, c = 299_792_458.0, k = 1.380649e-23;
        double lambda = nm * 1e-9;
        double perMetre = 2.0 * h * c * c / (lambda * lambda * lambda * lambda * lambda) / (Math.Exp(h * c / (lambda * k * TemperatureK)) - 1.0);
        return perMetre * 1e-9;
    }

    /// <summary>Spectral irradiance at normal incidence above the atmosphere at <paramref name="nm"/>, W·m⁻²·nm⁻¹.</summary>
    public static double Irradiance(double nm) => Radiance(nm) * SolidAngle;
}
