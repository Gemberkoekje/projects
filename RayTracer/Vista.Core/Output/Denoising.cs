using Vista.Rendering;

namespace Vista.Output;

/// <summary>Which device a denoiser runs on (vista-plan §10 M1.6).</summary>
public enum DenoiseDevice
{
    /// <summary>Not chosen.</summary>
    None,
    /// <summary>The CPU: Open Image Denoise's reference device, and the default.</summary>
    Cpu,
    /// <summary>An NVIDIA GPU through CUDA (the RTX 3070): measured as an option.</summary>
    Cuda,
}

/// <summary>The filter's quality mode: Open Image Denoise's high (for final frames), balanced or fast (for interactive use).</summary>
public enum DenoiseQuality
{
    /// <summary>Not chosen.</summary>
    None,
    /// <summary>The best image, the slowest.</summary>
    High,
    /// <summary>Somewhat lower quality, faster, less memory.</summary>
    Balanced,
    /// <summary>Noticeably lower quality, for previews.</summary>
    Fast,
}

/// <summary>How the albedo and normal images reach the filter.</summary>
public enum DenoiseAuxiliary
{
    /// <summary>Not chosen.</summary>
    None,
    /// <summary>As rendered, with the filter told they may be noisy (Open Image Denoise's default).</summary>
    Noisy,
    /// <summary>As rendered, with the filter told they are noise-free (<c>cleanAux</c>).</summary>
    Clean,
    /// <summary>Each denoised first by a filter of its own, then given as noise-free: the recommendation for final frames.</summary>
    Prefiltered,
}

/// <summary>What 1.0 of the colour the filter sees stands for (Open Image Denoise's <c>inputScale</c>).</summary>
public enum DenoiseInputScale
{
    /// <summary>Not chosen.</summary>
    None,
    /// <summary>
    /// The filter's own guess from the image it is given (its log-average luminance to 0.18). A loupe or a tile is then
    /// scaled by its own pixels, not its frame's.
    /// </summary>
    Auto,
    /// <summary>
    /// Physical units, the filter's stated convention: 1 is 100 cd/m². From the EXR's units, that is
    /// <see cref="IntegratorTables.LuminancePerY"/> / 100.
    /// </summary>
    Physical,
    /// <summary>
    /// The exposure meter's (<see cref="ExposureInfo.MeterScale"/>), before the camera's compensation: middle grey at 0.18 — since
    /// M1.10a by default the incident meter's grey card, with the average meter the frame's log-average luminance, as the filter's
    /// own guess does — but metered on the whole view, so a loupe is scaled like its frame.
    /// </summary>
    Metered,
}

/// <summary>How to denoise (vista-plan §10 M1.6). The defaults are the measured choice; the other values are kept for measuring.</summary>
public sealed record DenoiseSettings
{
    /// <summary>The device.</summary>
    public DenoiseDevice Device { get; init; } = DenoiseDevice.Cpu;

    /// <summary>The filter's quality mode.</summary>
    public DenoiseQuality Quality { get; init; } = DenoiseQuality.High;

    /// <summary>How albedo and normal are given.</summary>
    public DenoiseAuxiliary Auxiliary { get; init; } = DenoiseAuxiliary.Clean;

    /// <summary>What the colour's 1.0 stands for.</summary>
    public DenoiseInputScale InputScale { get; init; } = DenoiseInputScale.Metered;
}

/// <summary>
/// The images a denoiser is given, all of the rendered window's size, 3 floats per pixel, the top row first: the beauty in
/// linear Rec.709 (the EXR's values, which are never negative here), and the first hit's albedo in [0, 1] and world-space
/// normal, both averaged over the pixel's samples (0 where every sample saw sky).
/// </summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Color">The beauty.</param>
/// <param name="Albedo">The first hit's albedo.</param>
/// <param name="Normal">The first hit's normal.</param>
/// <param name="InputScale">The factor that maps <paramref name="Color"/> to the filter's units; NaN lets the filter choose.</param>
public sealed record DenoiseInput(int Width, int Height, float[] Color, float[] Albedo, float[] Normal, float InputScale)
{
    /// <summary>Pixels per image.</summary>
    public int Pixels => Width * Height;
}

/// <summary>What a denoiser reports about one run, as the sidecar records it.</summary>
/// <param name="Library">The library, e.g. "Intel Open Image Denoise".</param>
/// <param name="Version">Its version, as the loaded library reports it.</param>
/// <param name="Device">The device it ran on, with its name.</param>
/// <param name="Filter">The filter and its parameters.</param>
/// <param name="InputScale">The input scale given to the filter; null when the filter chose its own.</param>
/// <param name="CommitSeconds">Wall time preparing the filter (0 when a prepared one was reused).</param>
/// <param name="ExecuteSeconds">Wall time filtering, the input's upload and the output's download included.</param>
public sealed record DenoiserRun(string Library, string Version, string Device, string Filter, double? InputScale, double CommitSeconds, double ExecuteSeconds);

/// <summary>The denoised beauty, or why there is none.</summary>
public sealed record DenoiseResult
{
    private DenoiseResult(float[] color, DenoiserRun? run, string problem)
    {
        Color = color;
        Run = run;
        Problem = problem;
    }

    /// <summary>The denoised linear Rec.709, 3 floats per pixel; empty when <see cref="Succeeded"/> is false.</summary>
    public float[] Color { get; }

    /// <summary>What ran; null when nothing did.</summary>
    public DenoiserRun? Run { get; }

    /// <summary>Why nothing was denoised; empty on success.</summary>
    public string Problem { get; }

    /// <summary>True when <see cref="Color"/> holds the denoised image.</summary>
    public bool Succeeded => Problem.Length == 0;

    /// <summary>A denoised image.</summary>
    public static DenoiseResult Denoised(float[] color, DenoiserRun run)
    {
        ArgumentNullException.ThrowIfNull(color);
        ArgumentNullException.ThrowIfNull(run);
        return new DenoiseResult(color, run, string.Empty);
    }

    /// <summary>No image, because of <paramref name="problem"/>.</summary>
    public static DenoiseResult Failed(string problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(problem);
        return new DenoiseResult([], null, problem);
    }
}

/// <summary>
/// The seam between the pure pipeline and a denoiser (vista-plan §5.7, §10 M1.6): Intel Open Image Denoise in Vista.App;
/// tests use a fake.
/// </summary>
public interface IDenoiser
{
    /// <summary>
    /// Denoises <paramref name="input"/> as <paramref name="settings"/> ask. A denoiser that cannot run — its library missing,
    /// no such device, a filter that failed — returns a result that says why instead of throwing, so the caller keeps the raw
    /// image and warns.
    /// </summary>
    DenoiseResult Denoise(DenoiseInput input, DenoiseSettings settings);
}

/// <summary>The denoising of one image as its sidecar records it: what was asked for, and what ran or why nothing did.</summary>
/// <param name="Settings">What was asked for.</param>
/// <param name="Run">What ran; null when nothing was denoised.</param>
/// <param name="Problem">Why nothing was denoised; null when something was.</param>
/// <param name="ClampedValues">Colour values the filter could not take (negative, out of the Rec.709 gamut, or not finite), given to it as 0.</param>
/// <param name="Inputs">What the filter was fed.</param>
public sealed record DenoiseInfo(DenoiseSettings Settings, DenoiserRun? Run, string? Problem, long ClampedValues, string Inputs)
{
    /// <summary>Recorded in <see cref="Inputs"/>.</summary>
    public const string InputsDescription =
        "color: the EXR's linear Rec.709, negatives as 0; albedo and normal: the first hit's, averaged over the pixel's samples (0 for sky)";
}

/// <summary>Builds what a denoiser is given from a render (vista-plan §10 M1.6).</summary>
public static class Denoising
{
    /// <summary>
    /// The input scale <paramref name="settings"/> asks for: NaN for <see cref="DenoiseInputScale.Auto"/>, physical units, or
    /// the meter's (NaN when the meter saw no light, so the filter chooses).
    /// </summary>
    public static float InputScale(DenoiseSettings settings, ExposureInfo exposure)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(exposure);
        return settings.InputScale switch
        {
            DenoiseInputScale.Auto => float.NaN,
            DenoiseInputScale.Physical => (float)(IntegratorTables.LuminancePerY / 100.0),
            DenoiseInputScale.Metered => (float)exposure.MeterScale(),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.InputScale, "Unknown input scale."),
        };
    }

    /// <summary>
    /// The input for <paramref name="linear"/> (the EXR's linear Rec.709 of <paramref name="output"/>) and the albedo and
    /// normal the render returned, with <paramref name="inputScale"/> for the filter to apply. Values the filter cannot take
    /// become 0 (colour) or are clamped to [0, 1] (albedo) and [−1, 1] (normal); <paramref name="clamped"/> counts the colour
    /// values changed.
    /// </summary>
    public static DenoiseInput Prepare(RenderOutput output, ReadOnlySpan<float> linear, float inputScale, out long clamped)
    {
        ArgumentNullException.ThrowIfNull(output);
        int values = output.Width * output.Height * 3;
        if (linear.Length != values)
            throw new ArgumentException($"Expected {values} colour floats, got {linear.Length}.", nameof(linear));
        float[] albedo = Aux(output, Aov.Albedo, values);
        float[] normal = Aux(output, Aov.Normal, values);

        var color = new float[values];
        clamped = 0;
        for (int i = 0; i < values; i++)
        {
            float c = linear[i];
            if (float.IsFinite(c) && c >= 0f)
            {
                color[i] = c;
            }
            else
            {
                clamped++;
            }
            albedo[i] = float.IsFinite(albedo[i]) ? Math.Clamp(albedo[i], 0f, 1f) : 0f;
            normal[i] = float.IsFinite(normal[i]) ? Math.Clamp(normal[i], -1f, 1f) : 0f;
        }
        return new DenoiseInput(output.Width, output.Height, color, albedo, normal, inputScale);
    }

    private static float[] Aux(RenderOutput output, Aov aov, int values)
    {
        if (!output.Aovs.TryGetValue(aov, out float[]? data))
            throw new InvalidOperationException($"Renderer did not return the {aov} output the denoiser needs.");
        if (data.Length != values)
            throw new InvalidOperationException($"Renderer returned {data.Length} floats for {aov}, expected {values}.");
        return (float[])data.Clone();
    }
}
