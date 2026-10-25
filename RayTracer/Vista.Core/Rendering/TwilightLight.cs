using Vista.Atmosphere;

namespace Vista.Rendering;

/// <summary>
/// The sunlight on a summit at twilight (vista-plan §10 M1.12a, M1.13): the solar spectrum through the default air along the straight
/// ray from the summit toward a sun below the horizontal, over the curved Earth, which grazes the air above the valleys and leaves
/// little but the red: the light alpenglow is made of. Kept apart from <see cref="QuadratureCheck"/> so a wavelength table may be tuned
/// to it (<see cref="Quadrature.Blend"/>) without reaching the materials the check reads.
/// </summary>
public static class TwilightLight
{
    /// <summary>
    /// The sunlight that reaches <paramref name="summitMetres"/> with the sun at <paramref name="elevationDegrees"/> (geometric, below
    /// the horizontal when negative), and its optical depth in air masses of the sea-level zenith column at 550 nm.
    /// </summary>
    public static (Func<double, double> Spectrum, double AirMass) Sun(double elevationDegrees, double summitMetres)
    {
        AtmosphereModel model = AtmosphereModel.Default;
        DensityColumns slant = SlantColumns(model, summitMetres, elevationDegrees);
        AtmosphereCoefficients k550 = model.CoefficientsAt(550.0);
        double airMass = k550.OpticalDepth(slant) / k550.OpticalDepth(model.ZenithColumns(0.0));
        return (nm => SolarSpectrum.Radiance(nm) * Math.Exp(-model.CoefficientsAt(nm).OpticalDepth(slant)), airMass);
    }

    /// <summary>
    /// Columns of relative density, m, along the straight ray from <paramref name="altitudeMetres"/> at <paramref name="elevationDegrees"/>
    /// to the top of the air, over the sphere of <paramref name="model"/> (midpoints of 20 m steps).
    /// </summary>
    public static DensityColumns SlantColumns(AtmosphereModel model, double altitudeMetres, double elevationDegrees)
    {
        ArgumentNullException.ThrowIfNull(model);
        double r0 = model.PlanetRadiusMetres + altitudeMetres;
        double mu = Math.Sin(elevationDegrees * Math.PI / 180.0);
        double top = model.TopRadiusMetres;
        double length = -r0 * mu + Math.Sqrt(r0 * r0 * (mu * mu - 1.0) + top * top);
        const double step = 20.0;
        int steps = (int)Math.Ceiling(length / step);
        double dt = length / steps, rayleigh = 0.0, aerosol = 0.0, ozone = 0.0;
        for (int s = 0; s < steps; s++)
        {
            double t = (s + 0.5) * dt;
            double h = Math.Sqrt(r0 * r0 + t * t + 2.0 * r0 * mu * t) - model.PlanetRadiusMetres;
            if (h < 0.0)
                throw new ArgumentException("The ray meets the ground.", nameof(elevationDegrees));
            rayleigh += model.RayleighDensity(h) * dt;
            aerosol += model.AerosolDensity(h) * dt;
            ozone += model.OzoneDensity(h) * dt;
        }
        return new DensityColumns(rayleigh, aerosol, ozone);
    }
}
