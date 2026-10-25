using Vista.Atmosphere;

namespace Vista.Weather;

/// <summary>
/// Haze from humidity, and visibility (vista-plan §3.6, §10 M1.11). Aerosol particles take up water as the air grows humid, and
/// their extinction grows with them: f(RH) = (1 − RH)^−γ (Hänel's growth law, as Kasten 1969 wrote it for visibility; humidified
/// nephelometers fit it so, e.g. Zieger et al. 2013), here scaled so the air at <see cref="ReferenceHumidityPercent"/> has the
/// model's clear-day haze, <see cref="ReferenceOpticalDepth"/> at 550 nm from sea level. Growth stops at
/// <see cref="GrowthLimitPercent"/>: wetter air is fog, which is M4's. The haze's colour (its Ångström exponent) is left as it
/// is: measured growth hardly depends on the wavelength (under 5% across 450–700 nm at 85%, Zieger et al. 2013).
///
/// <para>The fog/haze slider is the visibility at the scene's valley floor as weather reports give it: the WMO's meteorological
/// optical range, the path that dims a beam to 5% (<see cref="VisibilityConstant"/> = ln 20 over the air's extinction at 550 nm
/// there, molecules and haze; WMO-No. 8, vol. I, ch. 9). So a pinned visibility sets the haze's optical depth, and the auto
/// visibility reports the humidity's haze in the same unit. Clean air alone limits it, to about 260 km at sea level.</para>
/// </summary>
public static class Haze
{
    /// <summary>The haze's vertical optical depth at 550 nm from sea level at <see cref="ReferenceHumidityPercent"/>: <see cref="AtmosphereModel"/>'s clear day.</summary>
    public const double ReferenceOpticalDepth = AtmosphereModel.ClearDayOpticalDepth550;

    /// <summary>The humidity at which the haze is the model's clear day, %.</summary>
    public const double ReferenceHumidityPercent = 50.0;

    /// <summary>The growth exponent γ: 0.3–0.45 for continental, rural and alpine aerosol (Zieger et al. 2013; marine air 0.55–0.7).</summary>
    public const double GrowthExponent = 0.4;

    /// <summary>The humidity above which the haze grows no more, %: the air is near fog (M4).</summary>
    public const double GrowthLimitPercent = 95.0;

    /// <summary>−ln 0.05: the meteorological optical range is this over the extinction coefficient.</summary>
    public static readonly double VisibilityConstant = Math.Log(20.0);

    /// <summary>The haze's optical depth at 550 nm from sea level for air at <paramref name="humidityPercent"/>.</summary>
    public static double OpticalDepth(double humidityPercent)
    {
        double rh = Math.Clamp(humidityPercent, 0.0, GrowthLimitPercent) / 100.0;
        return ReferenceOpticalDepth * Math.Pow((1.0 - rh) / (1.0 - ReferenceHumidityPercent / 100.0), -GrowthExponent);
    }

    /// <summary>The visibility at <paramref name="altitudeMetres"/> in <paramref name="model"/>'s air, km.</summary>
    public static double VisibilityKm(AtmosphereModel model, double altitudeMetres)
    {
        ArgumentNullException.ThrowIfNull(model);
        return VisibilityConstant / Extinction(model, altitudeMetres) / 1000.0;
    }

    /// <summary>
    /// The haze optical depth (550 nm, from sea level) that gives <paramref name="visibilityKm"/> at <paramref name="altitudeMetres"/>
    /// in <paramref name="model"/>'s air without its valley haze, other things equal; 0 when clean air alone already limits the view to less.
    /// </summary>
    public static double OpticalDepthForVisibility(AtmosphereModel model, double visibilityKm, double altitudeMetres)
    {
        ArgumentNullException.ThrowIfNull(model);
        double haze = VisibilityConstant / (visibilityKm * 1000.0) - Extinction(Clean(model), altitudeMetres);
        // At 550 nm the haze's extinction per unit optical depth is 1 / its scale height, times its own profile here.
        return Math.Max(0.0, haze * model.AerosolScaleHeightMetres / Math.Exp(-altitudeMetres / model.AerosolScaleHeightMetres));
    }

    /// <summary>The longest visibility clean air allows at <paramref name="altitudeMetres"/>, km.</summary>
    public static double CleanAirVisibilityKm(AtmosphereModel model, double altitudeMetres)
    {
        ArgumentNullException.ThrowIfNull(model);
        return VisibilityKm(Clean(model), altitudeMetres);
    }

    /// <summary><paramref name="model"/>'s air with no haze at all, its own or a valley's.</summary>
    public static AtmosphereModel Clean(AtmosphereModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model with { AerosolOpticalDepth550 = 0.0, ValleyHazeExtinction550 = 0.0 };
    }

    private static double Extinction(AtmosphereModel model, double altitudeMetres)
    {
        AtmosphereCoefficients k = model.CoefficientsAt(550.0);
        return k.Rayleigh * model.RayleighDensity(altitudeMetres) + k.AerosolExtinction * model.AerosolDensity(altitudeMetres)
            + k.Ozone * model.OzoneDensity(altitudeMetres);
    }
}
