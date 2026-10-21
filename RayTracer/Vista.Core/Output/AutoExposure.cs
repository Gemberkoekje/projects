using Vista.Rendering;

namespace Vista.Output;

/// <summary>How a PNG's exposure is metered (vista-plan §10 M1.10a).</summary>
public enum Metering
{
    /// <summary>Not chosen.</summary>
    None,

    /// <summary>
    /// The default since M1.10a (<see cref="IncidentMeter"/>): a grey card where the camera stands, facing the sun, at middle grey.
    /// The light decides the exposure, not the framing, so a sunlit face is exposed the same whatever else is in the frame.
    /// </summary>
    Incident,

    /// <summary>
    /// M1.3's meter (<see cref="AutoExposure"/>): the frame's log-average luminance at middle grey. A frame half in shadow is
    /// exposed for the shadow, and its sunlit half comes out stops too bright.
    /// </summary>
    Average,
}

/// <summary>
/// How a PNG was exposed (vista-plan §10 M1 decision "physical units", M1.10a): day and twilight differ by about 10⁴ in luminance,
/// so the display transform meters each view, with the camera's exposure value as a compensation in stops on top. The EXR stays
/// scene-referred. The reading is in <see cref="Incident"/> or <see cref="Average"/>, whichever <see cref="Meter"/> made it.
/// </summary>
/// <param name="Meter">Which meter exposed the image.</param>
/// <param name="Method">How the exposure was found.</param>
/// <param name="CompensationEv">The camera's compensation, stops.</param>
/// <param name="MeteredEv">The stops the meter alone scaled the linear image by (log₂ of <see cref="MeterScale"/>); null when it found no light.</param>
/// <param name="Ev100">The scene's exposure value at ISO 100 by that meter's calibration; null when it found no light.</param>
/// <param name="AppliedEv">The stops the display transform scaled the linear image by: the metered stops plus the compensation.</param>
public sealed record ExposureInfo(Metering Meter, string Method, double CompensationEv, double? MeteredEv, double? Ev100, double AppliedEv)
{
    /// <summary>The average meter's reading (<see cref="Metering.Average"/>); null for the incident meter.</summary>
    public AverageReading? Average { get; init; }

    /// <summary>The incident meter's reading (<see cref="Metering.Incident"/>); null for the average meter.</summary>
    public IncidentReading? Incident { get; init; }

    /// <summary>
    /// The linear factor the meter alone asked for, before the compensation: <see cref="AutoExposure.Key"/> over the frame's log
    /// average, or π over the card's illuminance; NaN when the meter found no light. The denoiser's metered input scale.
    /// </summary>
    public double MeterScale() => (Average, Incident) switch
    {
        ({ LogAverageY: > 0.0 } average, _) => AutoExposure.Key / average.LogAverageY,
        (_, { IlluminanceY: > 0.0 } incident) => Math.PI / incident.IlluminanceY,
        _ => double.NaN,
    };
}

/// <summary>What the average meter read (<see cref="AutoExposure"/>).</summary>
/// <param name="LogAverageY">The metered mean luminance in the integrator's units (Y, W·m⁻²·sr⁻¹·nm⁻¹); 0 when no pixel has light.</param>
/// <param name="LogAverageLuminanceCdM2">The same in cd/m².</param>
public sealed record AverageReading(double LogAverageY, double LogAverageLuminanceCdM2)
{
    /// <summary>Width of the meter render, pixels.</summary>
    public int MeterWidth { get; init; }

    /// <summary>Height of the meter render, pixels.</summary>
    public int MeterHeight { get; init; }

    /// <summary>Samples per pixel of the meter render.</summary>
    public int MeterSamplesPerPixel { get; init; }
}

/// <summary>
/// M1.3's meter, kept as <see cref="Metering.Average"/> since M1.10a. The geometric mean of the pixels' luminance, with the darkest
/// and brightest <see cref="TrimFraction"/> clamped to their percentiles (the sun's disk must not decide the exposure), maps to
/// <see cref="Key"/>.
///
/// <para>The meter is a render of its own: the whole frame at <see cref="MeterWidth"/> pixels and
/// <see cref="MeterSamplesPerPixel"/> samples, whatever size the image is. So a preview, its 4K final and
/// any loupe of it are exposed the same, and a loupe's PNG stays a crop of the final's.</para>
/// </summary>
public static class AutoExposure
{
    /// <summary>Middle grey: the linear value the metered luminance is mapped to.</summary>
    public const double Key = 0.18;

    /// <summary>Fraction of pixels clamped at each end before the mean is taken.</summary>
    public const double TrimFraction = 0.01;

    /// <summary>Reflected-light meter calibration constant, cd·s/m² (ISO 2720).</summary>
    public const double MeterCalibration = 12.5;

    /// <summary>Width of the meter render, pixels (the preview's).</summary>
    public const int MeterWidth = 320;

    /// <summary>Samples per pixel of the meter render.</summary>
    public const int MeterSamplesPerPixel = 16;

    /// <summary>
    /// The meter render for an image rendered with <paramref name="image"/>: the whole frame at <see cref="MeterWidth"/>
    /// (the frame's aspect ratio kept) and <see cref="MeterSamplesPerPixel"/>, through the LUT atmosphere, every other
    /// setting the same. The reference atmosphere is never metered on: at 16 samples its twilight is mostly zeros and
    /// rare bright paths, and metering both modes alike exposes a LUT and a reference image the same, side by side.
    /// </summary>
    public static RenderSettings MeterSettings(RenderSettings image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int height = Math.Max(1, (int)Math.Round((double)MeterWidth * image.Height / image.Width, MidpointRounding.AwayFromZero));
        return image with
        {
            Width = MeterWidth,
            Height = height,
            Window = default,
            SamplesPerPixel = MeterSamplesPerPixel,
            Atmosphere = AtmosphereMode.Luts,
            ExposureMeter = true,
        };
    }

    /// <summary>Recorded in <see cref="ExposureInfo.Method"/>.</summary>
    public const string MethodName = "average: log-average luminance of a meter render of the whole frame, 1st–99th percentile, to 18% grey, plus the camera's EV";

    /// <summary>Recorded when no pixel has any light (e.g. night before M6's moon and stars).</summary>
    public const string BlackMethodName = "average: no pixel has light, so only the camera's EV is applied";

    /// <summary>Meters interleaved XYZ (<paramref name="xyz"/>, 3 floats per pixel) with <paramref name="compensationEv"/> stops on top.</summary>
    public static ExposureInfo Measure(ReadOnlySpan<float> xyz, double compensationEv)
    {
        if (xyz.Length % 3 != 0)
            throw new ArgumentException("XYZ data must hold three floats per pixel.", nameof(xyz));
        var logs = new List<double>(xyz.Length / 3);
        for (int p = 1; p < xyz.Length; p += 3)
        {
            float y = xyz[p];
            if (float.IsFinite(y) && y > 0f)
                logs.Add(Math.Log(y));
        }
        if (logs.Count == 0)
            return new ExposureInfo(Metering.Average, BlackMethodName, compensationEv, null, null, compensationEv) { Average = new AverageReading(0.0, 0.0) };

        logs.Sort();
        double low = logs[(int)Math.Floor(TrimFraction * (logs.Count - 1))];
        double high = logs[(int)Math.Ceiling((1.0 - TrimFraction) * (logs.Count - 1))];
        double sum = 0.0;
        foreach (double v in logs)
            sum += Math.Clamp(v, low, high);
        double meanY = Math.Exp(sum / logs.Count);
        double luminance = meanY * IntegratorTables.LuminancePerY;
        double metered = Math.Log2(Key / meanY);
        return new ExposureInfo(Metering.Average, MethodName, compensationEv, metered, Math.Log2(luminance * 100.0 / MeterCalibration), metered + compensationEv)
        {
            Average = new AverageReading(meanY, luminance),
        };
    }
}
