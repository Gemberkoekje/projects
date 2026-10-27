using Vista.Output;

namespace Vista.Rendering;

/// <summary>An estimate in seconds: its low end, its middle and its high end (vista-plan §7.2: "a range, not a single number").</summary>
/// <param name="Low">The low end.</param>
/// <param name="Mid">The middle.</param>
/// <param name="High">The high end.</param>
public readonly record struct TimeRange(double Low, double Mid, double High)
{
    /// <summary>Nothing: zero seconds.</summary>
    public static TimeRange Zero => default;

    /// <summary><paramref name="mid"/> seconds, give or take <paramref name="margin"/> of them (a share, 0 to 1).</summary>
    public static TimeRange Around(double mid, double margin)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mid);
        ArgumentOutOfRangeException.ThrowIfNegative(margin);
        return new TimeRange(mid * Math.Max(0.0, 1.0 - margin), mid, mid * (1.0 + margin));
    }

    /// <summary>The sum of two estimates, end by end: errors that may all fall the same way.</summary>
    public static TimeRange operator +(TimeRange a, TimeRange b) => new(a.Low + b.Low, a.Mid + b.Mid, a.High + b.High);

    /// <summary>True when <paramref name="seconds"/> lies inside the range, its ends included.</summary>
    public bool Contains(double seconds) => seconds >= Low && seconds <= High;

    /// <summary>The range for a reader: "52 s – 1 m 10 s (about 1 m 1 s)".</summary>
    public override string ToString() => $"{Describe(Low)} – {Describe(High)} (about {Describe(Mid)})";

    /// <summary>
    /// A duration for a reader, to the second under an hour: "0.4 s", "45 s", "12 m 5 s", "1 h 3 m".
    /// </summary>
    public static string Describe(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0.0)
            return "?";
        if (seconds < 10.0)
            return seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";
        long whole = (long)Math.Round(seconds);
        if (whole < 60)
            return $"{whole} s";
        if (whole < 3600)
            return $"{whole / 60} m {whole % 60} s";
        long minutes = (long)Math.Round(seconds / 60.0);
        return $"{minutes / 60} h {minutes % 60} m";
    }
}

/// <summary>
/// The final's trace projected from a probe at its own resolution and detail (vista-plan §7.2, §10 M1.15): the final's own first passes,
/// traced on the final's mesh as the final will trace them (<see cref="BatchPlannerOptions.StopAfterGpuSeconds"/>), up to the pass size
/// the rest of the final keeps. The final's trace is those passes as measured, plus its remaining samples at the last pass's GPU cost
/// per pixel-sample and wall time per GPU second (a shared GPU's pauses, <see cref="GpuShare"/>, and each submission's overhead).
///
/// <para>The last pass, not the mean: the planner's passes grow as its cost estimate settles, and a sample costs more in a larger pass
/// (on M1.15's snow view 5.2 ns at one sample a pixel, 6.7 at four, with the same rays), so the early passes would read the rest low.
/// The passes of the last one's size give the cost and its spread. The wall ratio is the median of every pass's after the first: a
/// pass the desktop interrupts reads 2.9 where the final's whole trace reads 2.3 (M1.15's down-slope view), and the ratio hardly
/// depends on the pass's size, since a shared GPU's dispatches are cut to 30 ms whatever the pass.</para>
/// </summary>
/// <param name="ProbePasses">Passes the probe traced.</param>
/// <param name="ProbeSamples">Samples per pixel the probe traced.</param>
/// <param name="ProbeGpuSeconds">The probe's GPU seconds.</param>
/// <param name="ProbeWallSeconds">The probe's trace's wall seconds.</param>
/// <param name="FramePixels">The frame's pixels.</param>
/// <param name="SteadySamples">Samples per pixel in the last pass: the size the final's later passes keep.</param>
/// <param name="CostNs">GPU nanoseconds per pixel-sample over the passes of that size.</param>
/// <param name="CostSpread">Half their range of cost, as a share of <see cref="CostNs"/> (0 with one such pass).</param>
/// <param name="WallPerGpu">The median wall seconds per GPU second of the passes after the first.</param>
/// <param name="RaysPerSample">Rays per pixel-sample in the probe.</param>
/// <param name="RaysPerSecond">Rays per GPU second in the probe.</param>
/// <param name="Margin">The share the wall time is given or taken: the larger of <see cref="TraceProjection.MinMargin"/> and twice the spread.</param>
public sealed record TraceProjection(int ProbePasses, int ProbeSamples, double ProbeGpuSeconds, double ProbeWallSeconds, long FramePixels,
    int SteadySamples, double CostNs, double CostSpread, double WallPerGpu, double RaysPerSample, double RaysPerSecond, double Margin)
{
    /// <summary>
    /// The least margin on a trace's wall time: what other programs on the user's GPU and its clocks change between the probe and a
    /// final run a minute later (vista-plan §10 M1.15: on its seven test views, twice, the final's GPU time ran −8% to +16% of the
    /// probe's projection).
    /// </summary>
    public const double MinMargin = 0.2;

    /// <summary>
    /// The projection from <paramref name="passes"/> (a probe's, at the final's size and detail, of a frame of <paramref name="framePixels"/>)
    /// that traced <paramref name="rays"/> rays.
    /// </summary>
    public static TraceProjection From(IReadOnlyList<PassTiming> passes, long framePixels, ulong rays)
    {
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framePixels);
        if (passes.Count == 0)
            throw new ArgumentException("A projection needs at least one pass.", nameof(passes));
        int steady = passes[^1].SampleCount;
        PassTiming[] used = [.. passes.Where(p => p.SampleCount == steady)];
        if (used.Length > 1 && used[0] == passes[0])
            used = used[1..]; // the first pass carries the planner's ramp
        double gpu = used.Sum(p => p.GpuSeconds);
        long pixelSamples = used.Sum(p => p.PixelSamples);
        if (gpu <= 0.0 || pixelSamples <= 0)
            throw new ArgumentException("The passes measured no GPU time.", nameof(passes));
        double cost = gpu * 1e9 / pixelSamples;
        double spread = used.Length > 1 ? (used.Max(p => p.CostNs) - used.Min(p => p.CostNs)) / (2.0 * cost) : 0.0;
        double[] ratios = [.. passes.Skip(passes.Count > 1 ? 1 : 0).Where(p => p.GpuSeconds > 0.0).Select(p => p.WallSeconds / p.GpuSeconds).Order()];
        double wallPerGpu = ratios.Length % 2 == 1 ? ratios[ratios.Length / 2] : 0.5 * (ratios[ratios.Length / 2 - 1] + ratios[ratios.Length / 2]);
        double allGpu = passes.Sum(p => p.GpuSeconds);
        long allPixelSamples = passes.Sum(p => p.PixelSamples);
        return new TraceProjection(passes.Count, passes.Sum(p => p.SampleCount), allGpu, passes.Sum(p => p.WallSeconds), framePixels, steady, cost,
            spread, wallPerGpu, (double)rays / allPixelSamples, rays / allGpu, Math.Max(MinMargin, 2.0 * spread));
    }

    /// <summary>The GPU seconds of a trace of <paramref name="samplesPerPixel"/>: the probe's, and the rest at the steady cost.</summary>
    public double GpuSeconds(int samplesPerPixel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplesPerPixel);
        return samplesPerPixel <= ProbeSamples
            ? ProbeGpuSeconds * samplesPerPixel / ProbeSamples
            : ProbeGpuSeconds + (samplesPerPixel - ProbeSamples) * (double)FramePixels * CostNs * 1e-9;
    }

    /// <summary>The wall seconds of a trace of <paramref name="samplesPerPixel"/>, as a range.</summary>
    public TimeRange WallSeconds(int samplesPerPixel)
    {
        double mid = samplesPerPixel <= ProbeSamples
            ? ProbeWallSeconds * samplesPerPixel / ProbeSamples
            : ProbeWallSeconds + (GpuSeconds(samplesPerPixel) - ProbeGpuSeconds) * WallPerGpu;
        return TimeRange.Around(mid, Margin);
    }
}

/// <summary>
/// How an estimate (<c>--estimate</c>, vista-plan §7.2, §10 M1.15) puts a final run together: the stages the final repeats, measured
/// by running them as it will; its trace projected from a probe (<see cref="TraceProjection"/>); its files and its denoising measured on
/// the probe; each with its margin, summed end to end.
/// </summary>
public static class FinalEstimate
{
    /// <summary>
    /// The GPU seconds the probe measures at least (<see cref="BatchPlannerOptions.StopAfterGpuSeconds"/>): it traces the final's own first
    /// passes, at least two, past this, until they reach the size the final's later passes keep (at most ten times this). A probe of a few
    /// one-sample passes is not enough: on M1.15's snow view a sample cost 5.2 ns in passes of one sample a pixel and 6.7 in passes of four,
    /// with the same rays. A second rather than half of one: half held two or three passes on a costly view, too few for a median wall
    /// ratio.
    /// </summary>
    public const double ProbeGpuSeconds = 1.0;

    /// <summary>The margin on the stages the final repeats (the world read, the mesh, the shaders, the air's tables): run to run on the dev box.</summary>
    public const double SetupMargin = 0.15;

    /// <summary>The margin on writing the files.</summary>
    public const double WriteMargin = 0.25;

    /// <summary>The margin on denoising, scaled from a crop of the frame.</summary>
    public const double DenoiseMargin = 0.3;

    /// <summary>The share of the frame's area the denoising is measured on (a centred crop): one sixteenth.</summary>
    public const int DenoiseCropDivisor = 4;

    /// <summary>
    /// The stages a final repeats, by name, from the session's <paramref name="timings"/> (all but the render, whose trace is projected
    /// instead) and the probe's own <paramref name="renderStages"/> (the wall-clock ones: those ending in <c>Gpu</c> are GPU time
    /// inside them, and the trace is the probe's).
    /// </summary>
    public static SortedDictionary<string, double> SetupStages(IReadOnlyDictionary<string, double> timings, IReadOnlyDictionary<string, double> renderStages)
    {
        ArgumentNullException.ThrowIfNull(timings);
        ArgumentNullException.ThrowIfNull(renderStages);
        var setup = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach ((string key, double seconds) in timings)
        {
            if (key != "render")
                setup[key] = seconds;
        }
        foreach ((string key, double seconds) in renderStages)
        {
            if (key != "trace" && !key.EndsWith("Gpu", StringComparison.Ordinal))
                setup["render." + key] = seconds;
        }
        return setup;
    }

    /// <summary>The whole run: the parts' ranges summed end to end (errors that may all fall the same way).</summary>
    public static TimeRange Total(TimeRange setup, TimeRange trace, TimeRange write, TimeRange? denoise) =>
        setup + trace + write + (denoise ?? TimeRange.Zero);
}

/// <summary>
/// The noise a final will show, from the preview's per-pixel variance (vista-plan §7.2's noise target, §10 M1.15), in steps of the
/// 8-bit PNG: what a viewer sees. A pixel's luminance varies from sample to sample by σ (the preview's variance of its mean times its
/// samples); through the exposure and the display curve, a relative change of the luminance moves the PNG by the curve's local slope,
/// so one sample's noise in steps is <c>255 · slope · σ / Y</c>, and n samples' is that over √n. Clipped highlights have no slope and
/// show no noise; black, no light. The frame's noise is a high percentile of its pixels' (<see cref="Percentile"/>), so a few
/// fireflies do not set the number of samples for the rest.
///
/// <para>A preview pixel covers a patch of final pixels, so its samples also vary with what the patch holds (an edge, a texture): the
/// preview reads the final's noise high there. The range the estimate gives for a noise target carries that (measured on M1.15's seven
/// test views at 4K and 64 spp against their finals' own variance: the final needed 0.34–1.00 of the preview's samples, the least on the
/// sunlit snow and the day's terrain, all of them on the sky and in the shade; <see cref="FinalShareLow"/> to <see cref="FinalShareHigh"/>).</para>
/// </summary>
public static class NoiseMeter
{
    /// <summary>The share of the frame's pixels that must meet a noise target: the 95th percentile.</summary>
    public const double Percentile = 0.95;

    /// <summary>The relative step of the luminance the display slope is measured over (a central difference).</summary>
    public const double SlopeStep = 0.05;

    /// <summary>The low end of the samples the final needs for a target, as a share of the preview's count (M1.15's views: 0.34 at least).</summary>
    public const double FinalShareLow = 1.0 / 3.0;

    /// <summary>The high end, as a share of the preview's count (M1.15's views: 1.00 at most; the preview never asked for too few).</summary>
    public const double FinalShareHigh = 1.0;

    /// <summary>
    /// Each pixel's noise after one sample, in steps of the 8-bit PNG, from the preview's <paramref name="linearRgb"/> (interleaved linear
    /// Rec.709), its <paramref name="xyz"/>, and its <paramref name="varianceOfMean"/> (the variance of each pixel's mean luminance after
    /// <paramref name="samples"/> samples), displayed as the PNG is.
    /// </summary>
    public static float[] OneSampleSteps(ReadOnlySpan<float> linearRgb, ReadOnlySpan<float> xyz, ReadOnlySpan<float> varianceOfMean, int samples,
        double exposureEv, DisplayCurve curve, DisplayLook look)
    {
        int pixels = varianceOfMean.Length;
        if (linearRgb.Length != pixels * 3 || xyz.Length != pixels * 3)
            throw new ArgumentException("The image and its variance must cover the same pixels.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);
        var up = new float[pixels * 3];
        var down = new float[pixels * 3];
        var sigma = new double[pixels];
        for (int p = 0; p < pixels; p++)
        {
            double y = xyz[p * 3 + 1];
            sigma[p] = Math.Sqrt(Math.Max(0.0, varianceOfMean[p]) * samples);
            if (y > 0.0)
            {
                for (int c = 0; c < 3; c++)
                {
                    up[p * 3 + c] = linearRgb[p * 3 + c] * (float)(1.0 + SlopeStep);
                    down[p * 3 + c] = linearRgb[p * 3 + c] * (float)(1.0 - SlopeStep);
                }
            }
            else
            {
                // No light on average: the step one σ of grey light makes from black.
                up[p * 3] = up[p * 3 + 1] = up[p * 3 + 2] = (float)sigma[p];
            }
        }
        float[] hi = DisplayTransform.Encode(up, exposureEv, curve, look);
        float[] lo = DisplayTransform.Encode(down, exposureEv, curve, look);
        var steps = new float[pixels];
        for (int p = 0; p < pixels; p++)
        {
            double dLuma = Luma(hi, p) - Luma(lo, p);
            double y = xyz[p * 3 + 1];
            steps[p] = y > 0.0
                ? (float)(255.0 * dLuma / (2.0 * SlopeStep) * sigma[p] / y)
                : (float)(255.0 * dLuma);
        }
        return steps;
    }

    /// <summary>The value below which <paramref name="share"/> of <paramref name="values"/> lie (nearest rank); 0 for none.</summary>
    public static double PercentileOf(ReadOnlySpan<float> values, double share)
    {
        if (values.Length == 0)
            return 0.0;
        if (!(share is >= 0.0 and <= 1.0))
            throw new ArgumentOutOfRangeException(nameof(share), share, "A share from 0 to 1.");
        float[] sorted = values.ToArray();
        Array.Sort(sorted);
        int rank = (int)Math.Ceiling(share * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    /// <summary>The noise in steps after <paramref name="samples"/>, from the frame's one-sample noise <paramref name="oneSample"/>.</summary>
    public static double StepsAt(double oneSample, int samples)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);
        return oneSample / Math.Sqrt(samples);
    }

    /// <summary>
    /// The samples that bring the frame's one-sample noise <paramref name="oneSample"/> down to <paramref name="targetSteps"/>, at
    /// least 1 (and at most <paramref name="max"/>).
    /// </summary>
    public static int SamplesFor(double oneSample, double targetSteps, int max = 1 << 20)
    {
        if (!(targetSteps > 0.0))
            throw new ArgumentOutOfRangeException(nameof(targetSteps), targetSteps, "A noise target is a positive number of steps.");
        double n = Math.Ceiling(oneSample * oneSample / (targetSteps * targetSteps));
        return (int)Math.Clamp(n, 1.0, max);
    }

    private static double Luma(float[] encoded, int p) => 0.2126 * encoded[p * 3] + 0.7152 * encoded[p * 3 + 1] + 0.0722 * encoded[p * 3 + 2];
}
