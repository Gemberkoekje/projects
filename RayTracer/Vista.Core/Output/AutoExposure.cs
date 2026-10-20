using Vista.Rendering;

namespace Vista.Output;

/// <summary>
/// How a PNG was exposed (vista-plan §10 M1 decision "physical units"): day and twilight differ by about 10⁴ in
/// luminance, so the display transform meters each view. The geometric mean of the pixels' luminance, with the
/// darkest and brightest <see cref="AutoExposure.TrimFraction"/> clamped to their percentiles (the sun's disk
/// must not decide the exposure), maps to <see cref="AutoExposure.Key"/>; the camera's exposure value is a
/// compensation in stops on top. The EXR stays scene-referred.
///
/// <para>The meter is a render of its own: the whole frame at <see cref="AutoExposure.MeterWidth"/> pixels and
/// <see cref="AutoExposure.MeterSamplesPerPixel"/> samples, whatever size the image is. So a preview, its 4K final and
/// any loupe of it are exposed the same, and a loupe's PNG stays a crop of the final's.</para>
/// </summary>
/// <param name="Method">How the exposure was found.</param>
/// <param name="CompensationEv">The camera's compensation, stops.</param>
/// <param name="LogAverageY">The metered mean luminance in the integrator's units (Y, W·m⁻²·sr⁻¹·nm⁻¹).</param>
/// <param name="LogAverageLuminanceCdM2">The same in cd/m².</param>
/// <param name="Ev100">The scene's exposure value at ISO 100 for that luminance (reflected-light meter, K = 12.5); null when no pixel has light.</param>
/// <param name="AppliedEv">The stops the display transform scaled the linear image by (log₂ of the scale).</param>
public sealed record ExposureInfo(string Method, double CompensationEv, double LogAverageY, double LogAverageLuminanceCdM2, double? Ev100, double AppliedEv)
{
    /// <summary>Width of the meter render, pixels.</summary>
    public int MeterWidth { get; init; }

    /// <summary>Height of the meter render, pixels.</summary>
    public int MeterHeight { get; init; }

    /// <summary>Samples per pixel of the meter render.</summary>
    public int MeterSamplesPerPixel { get; init; }
}

/// <summary>Meters an image for its PNG (see <see cref="ExposureInfo"/>).</summary>
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
    public const string MethodName = "auto: log-average luminance of a meter render of the whole frame, 1st–99th percentile, to 18% grey, plus the camera's EV";

    /// <summary>Recorded when no pixel has any light (e.g. night before M6's moon and stars).</summary>
    public const string BlackMethodName = "auto: no pixel has light, so only the camera's EV is applied";

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
            return new ExposureInfo(BlackMethodName, compensationEv, 0.0, 0.0, null, compensationEv);

        logs.Sort();
        double low = logs[(int)Math.Floor(TrimFraction * (logs.Count - 1))];
        double high = logs[(int)Math.Ceiling((1.0 - TrimFraction) * (logs.Count - 1))];
        double sum = 0.0;
        foreach (double v in logs)
            sum += Math.Clamp(v, low, high);
        double meanY = Math.Exp(sum / logs.Count);
        double luminance = meanY * IntegratorTables.LuminancePerY;
        double applied = Math.Log2(Key / meanY) + compensationEv;
        return new ExposureInfo(MethodName, compensationEv, meanY, luminance, Math.Log2(luminance * 100.0 / MeterCalibration), applied);
    }
}
