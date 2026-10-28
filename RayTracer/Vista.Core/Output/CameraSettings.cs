using System.Globalization;

namespace Vista.Output;

/// <summary>
/// Vista's camera (vista-plan §10 M1.17): the settings a photographer sets on a real camera, which Vista applies to the light it renders
/// unless told otherwise. They are the user's picks of 2026-10-28 from the sign-off's sheets, judged against the user's own photos: a
/// camera's daylight white balance, the incident meter's exposure plus a stop and a half, an eye that follows dusk's fall in light half
/// way, and AgX's punchy look kept a little short of the gamut's edge. The EXR is the light itself; only the PNG goes through the camera.
/// A camera the user wields (ISO, shutter time, aperture and white balance as on a real one) is parked for later (vista-plan §10).
/// </summary>
public static class CameraSettings
{
    /// <summary>The white balance: a Planckian light of 5500 K renders neutral, a camera's daylight setting (<see cref="DisplayGrade"/>).</summary>
    public const double WhiteBalanceKelvin = 5500.0;

    /// <summary>The stops the incident meter adds on its own, beside the camera's EV (<see cref="IncidentMeter.DefaultCompensationEv"/>).</summary>
    public const double CompensationEv = 1.5;

    /// <summary>How far the exposure follows the light down into dusk (<see cref="IncidentMeter.DefaultAdaptation"/>): half the fall, in stops.</summary>
    public const double DuskAdaptation = 0.5;

    /// <summary>The look on AgX's curve (<see cref="DisplayTransform.DefaultLook"/>).</summary>
    public const DisplayLook Look = DisplayLook.Punchy;

    /// <summary>How far short of the gamut's edge the punchy look's saturation stops, a share of each colour's luminance (<see cref="DisplayGrade"/>).</summary>
    public const double SkyMargin = 0.03;

    /// <summary>
    /// The camera's settings a render was made with, in words, from its <paramref name="sidecar"/>: the white balance, the exposure, how far
    /// it followed dusk and the look.
    /// </summary>
    public static string Describe(Sidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        ImageInfo image = sidecar.Image;
        double kelvin = image.WhiteBalanceKelvin;
        string balance = kelvin <= 0.0 ? "the display's own white (D65)"
            : string.Create(CultureInfo.InvariantCulture, $"{kelvin:0} K{(Math.Abs(kelvin - WhiteBalanceKelvin) < 0.5 ? " (daylight)" : string.Empty)}");
        string exposure = "no meter";
        if (sidecar.Exposure is { } e)
        {
            string meter = e.Meter == Metering.Incident ? "the incident meter" : "the average meter";
            string camera = Math.Abs(e.CompensationEv) < 0.05 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" and {e.CompensationEv:+0.0;-0.0} EV on the camera");
            exposure = string.Create(CultureInfo.InvariantCulture, $"{meter} {e.MeterCompensationEv:+0.0;-0.0} EV{camera}");
            if (e.Adaptation is double a)
            {
                string held = e.AdaptationEv < 0.0 ? string.Create(CultureInfo.InvariantCulture, $", {e.AdaptationEv:0.0} EV held back") : string.Empty;
                if (a >= 1.0)
                    exposure += ", following dusk all the way";
                else if (a <= 0.0)
                    exposure += $", not following dusk{held}";
                else
                    exposure += string.Create(CultureInfo.InvariantCulture, $", following dusk {a:0.##} of the way{held}");
            }
        }
        string look = image.Look == DisplayLook.None ? image.Display.ToString().ToLowerInvariant() : $"AgX {image.Look.ToString().ToLowerInvariant()}";
        if (image.SkyMargin > 0.0)
            look += string.Create(CultureInfo.InvariantCulture, $" with a {image.SkyMargin:0.###} margin");
        return $"white balance {balance}; exposure by {exposure}; {look}";
    }
}
